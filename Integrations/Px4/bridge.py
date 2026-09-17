"""Dedicated SITL supervisor. No training environment dependency; owns only its process groups."""
import argparse, asyncio, contextlib, fcntl, json, math, os, pathlib, secrets, shlex, signal, subprocess, time
from mavsdk import System
from mavsdk.offboard import PositionNedYaw
import mavsdk

ROOT = pathlib.Path('/opt/llm2swarm/PX4-Autopilot')
BUILD = ROOT / 'build/px4_sitl_default'

class Bridge:
    def __init__(self, args):
        self.args=args; self.path=pathlib.Path(args.output); self.path.mkdir(parents=True,exist_ok=True)
        self.run=secrets.token_hex(8); self.token=secrets.token_hex(24); self.children=[]; self.jobs=[]
        self.sim=0.; self.clock_wall=0.; self.error=None; self.started=False; self.done=asyncio.Event(); self.last_request=time.monotonic()
        self.drones=[]; self.states=[]; self.goal_jobs={}; self.last_sequences={}
        (self.path/'supervisor.json').write_text(json.dumps(dict(pid=os.getpid(),runId=self.run)))
        observer_root=pathlib.Path(__file__).with_name('models')
        observer_models=str(observer_root)
        observer_worlds=str(pathlib.Path(__file__).with_name('worlds'))
        self.env=dict(os.environ,HEADLESS='1',GZ_PARTITION='llm2swarm-'+self.run,
            PX4_SYS_AUTOSTART='4010',PX4_SIM_MODEL='gz_x500_mono_cam',PX4_GZ_WORLD='default',
            PX4_GZ_MODELS=str(ROOT/'Tools/simulation/gz/models'),PX4_GZ_WORLDS=observer_worlds,
            GZ_SIM_RESOURCE_PATH=observer_models+':'+observer_worlds+':'+str(ROOT/'Tools/simulation/gz/models')+':'+str(ROOT/'Tools/simulation/gz/worlds'),
            GZ_SIM_SYSTEM_PLUGIN_PATH=str(BUILD/'src/modules/simulation/gz_plugins'),
            GZ_SIM_SERVER_CONFIG_PATH=str(ROOT/'src/modules/simulation/gz_bridge/server.config'))
    async def spawn(self,name,command,env=None,cwd=None,pipe=False):
        log=open(self.path/(name+'.log'),'wb')
        p=await asyncio.create_subprocess_exec(*map(str,command),cwd=cwd,env=env or self.env,start_new_session=True,
            stdout=asyncio.subprocess.PIPE if pipe else log,stderr=log)
        self.children.append((p,log,name));return p
    async def launch(self):
        capture_binary=pathlib.Path('/tmp')/('llm2swarm-camera-'+self.run)
        capture_source=pathlib.Path(__file__).with_name('camera_capture.cpp')
        flags=shlex.split(subprocess.check_output(['pkg-config','--cflags','--libs','gz-transport13','gz-msgs10','opencv4'],text=True))
        subprocess.run(['g++','-O2',str(capture_source),'-o',str(capture_binary),*flags],check=True)
        server=pathlib.Path(mavsdk.__file__).parent/'bin/mavsdk_server'
        for i in range(self.args.vehicles):
            await self.spawn(f'mavsdk-{i}',[server,'-p',50051+i,f'udpin://127.0.0.1:{14540+i}'])
            work=self.path/f'px4-{i}';work.mkdir(exist_ok=True)
            env=dict(self.env,PX4_GZ_MODEL_POSE=f'{10+20*i},0,0')
            if i:env['PX4_GZ_STANDALONE']='1'
            await self.spawn(f'px4-{i}',[BUILD/'bin/px4','-d',BUILD/'etc','-i',i,'-w',work],env,work)
            if i==0: await asyncio.sleep(5)
            drone=System(mavsdk_server_address='127.0.0.1',port=50051+i);await asyncio.wait_for(drone.connect(),60)
            self.drones.append(drone)
            self.states.append(dict(id=i,sysId=0,waypoint=0,north=0,east=0,down=0,vn=0,ve=0,vd=0,bootMs=0,
                receivedSim=0,wall=0,armed=False,healthy=False,connected=False,mode='',taskId=None,taskState='idle',origin=None,error=None))
            for stream in (self.position(i),self.heartbeat(i),self.health(i),self.armed(i),self.mode(i),self.connection(i)):
                self.jobs.append(asyncio.create_task(stream))
        for i in range(self.args.vehicles):
            topic=f'/world/default/model/x500_mono_cam_{i}/link/camera_link/sensor/imager/image'
            await self.spawn(f'camera-{i}',[capture_binary,topic,self.path/f'camera-{i}.jpg','5'])
        clock=await self.spawn('clock',['gz','topic','-e','-t','/world/default/clock','--json-output'],pipe=True)
        self.jobs.append(asyncio.create_task(self.clock(clock)))
        await self.wait(lambda: self.sim>0 and all(s['healthy'] and s['sysId']==s['id']+1 and s['wall']>0 for s in self.states),240,'ready health/identity/clock')
        for i,d in enumerate(self.drones):
            await d.telemetry.set_rate_position_velocity_ned(20)
            await d.param.set_param_float('MPC_XY_CRUISE',4.)
            await d.param.set_param_float('MPC_XY_VEL_MAX',4.)
            await d.action.set_takeoff_altitude(5+3*i)
            if abs(await d.param.get_param_float('MIS_TAKEOFF_ALT')-(5+3*i))>.01:
                raise RuntimeError('Takeoff altitude parameter readback mismatch')
            s=self.states[i];s['origin']=dict(north=s['north'],east=s['east'],down=s['down'],x=10+20*i,y=0,height=0)
        # Navigator consumes parameter updates asynchronously; prepare before arming.
        await asyncio.sleep(2)
        self.jobs.append(asyncio.create_task(self.watchdog()))
    async def wait(self,predicate,timeout,label):
        deadline=time.monotonic()+timeout
        while not predicate():
            if time.monotonic()>deadline:raise TimeoutError(label+' '+json.dumps(self.frame()))
            if self.error:raise RuntimeError(self.error)
            await asyncio.sleep(.05)
    async def clock(self,p):
        while line:=await p.stdout.readline():
            try:
                msg=json.loads(line); t=msg['sim'];value=float(t.get('sec',0))+float(t.get('nsec',0))*1e-9
                if value<self.sim:self.error='Gazebo clock reset'
                self.sim=value;self.clock_wall=time.monotonic()
            except (ValueError,KeyError):pass
    async def position(self,i):
        async for msg in self.drones[i].mavlink_direct.message('LOCAL_POSITION_NED'):
            if msg.system_id!=i+1:continue
            p=json.loads(msg.fields_json);s=self.states[i];stamp=p['time_boot_ms']
            if stamp<s['bootMs']:self.error='Position timestamp reset';continue
            if stamp==s['bootMs']:continue
            for a,b in [('north','x'),('east','y'),('down','z'),('vn','vx'),('ve','vy'),('vd','vz')]:s[a]=p[b]
            s.update(bootMs=stamp,receivedSim=self.sim,wall=time.monotonic())
    async def heartbeat(self,i):
        async for msg in self.drones[i].mavlink_direct.message('HEARTBEAT'):
            p=json.loads(msg.fields_json)
            if msg.component_id==1 and p.get('autopilot')==12:
                if msg.system_id!=i+1:self.error='Unexpected PX4 system ID'
                self.states[i]['sysId']=msg.system_id
    async def health(self,i):
        async for h in self.drones[i].telemetry.health():
            self.states[i]['healthy']=h.is_local_position_ok and h.is_global_position_ok and h.is_home_position_ok and h.is_armable
            self.states[i]['healthDetail']=str(h)
    async def armed(self,i):
        async for value in self.drones[i].telemetry.armed():self.states[i]['armed']=value
    async def mode(self,i):
        async for value in self.drones[i].telemetry.flight_mode():self.states[i]['mode']=str(value)
    async def connection(self,i):
        async for value in self.drones[i].core.connection_state():self.states[i]['connected']=value.is_connected
    def frame(self):
        now=time.monotonic()
        return dict(runId=self.run,error=self.error,simTime=self.sim,clockAge=now-self.clock_wall,
            vehicles=[dict(s,ageWall=now-s['wall']) for s in self.states])
    async def start(self):
        if self.started:raise ValueError('Already started')
        async def takeoff(i):
            d=self.drones[i];s=self.states[i];h=5+3*i
            await d.action.arm();await d.action.takeoff()
            await self.wait(lambda: abs(s['down']-(s['origin']['down']-h))<.5 and abs(s['vd'])<.5,60,'takeoff')
            await d.offboard.set_position_ned(PositionNedYaw(s['origin']['north'],s['origin']['east'],s['origin']['down']-h,0))
            await asyncio.sleep(1);await d.offboard.start()
        await asyncio.gather(*(takeoff(i) for i in range(len(self.drones))))
        await self.wait(lambda: all(s['mode']=='OFFBOARD' for s in self.states),5,'Offboard telemetry confirmation')
        self.started=True;self.last_request=time.monotonic();return self.frame()
    async def execute(self,i,task):
        s=self.states[i];s.update(taskId=task['taskId'],taskState='active',waypoint=0)
        try:
            for index,point in enumerate(task['ned']):
                s['waypoint']=index
                await self.drones[i].offboard.set_position_ned(PositionNedYaw(*point,0))
                settled=None;deadline=time.monotonic()+60
                while True:
                    if time.monotonic()>deadline:raise TimeoutError('Waypoint timeout')
                    if self.error:raise RuntimeError(self.error)
                    near=math.hypot(s['north']-point[0],s['east']-point[1])<=1 and abs(s['down']-point[2])<=.5 and math.sqrt(s['vn']**2+s['ve']**2+s['vd']**2)<=.5
                    settled=(self.sim if settled is None else settled) if near else None
                    if settled is not None and self.sim-settled>=.5:break
                    await asyncio.sleep(.05)
            s.update(taskState='completed',waypoint=len(task['ned']))
        except asyncio.CancelledError:raise
        except Exception as e:s['error']=str(e);self.error=str(e)
    def submit(self,tasks):
        reserved=set()
        for t in tasks:
            i=t['vehicleId']
            if i not in range(len(self.drones)) or i in reserved:raise ValueError('Invalid vehicle identity')
            reserved.add(i)
            if t['runId']!=self.run or t['frameId']!='map-enu-v1' or t['missionRevision']!=1:raise ValueError('Invalid mission identity/frame')
            if self.states[i]['taskState']=='active' or t['sequence']<=self.last_sequences.get(i,0):raise ValueError('Busy or stale task')
            if not t['ned'] or len(t['ned'])>10000 or any(len(p)!=3 or not all(math.isfinite(x) for x in p) for p in t['ned']):raise ValueError('Invalid waypoints')
        for t in tasks:
            i=t['vehicleId'];self.last_sequences[i]=t['sequence'];self.states[i]['taskState']='active'
            self.goal_jobs[i]=asyncio.create_task(self.execute(i,t))
    async def land(self):
        self.started=False
        for job in self.goal_jobs.values():job.cancel()
        await asyncio.gather(*self.goal_jobs.values(),return_exceptions=True)
        async def one(i):
            if self.states[i]['armed']:
                await self.drones[i].action.land()
                deadline=time.monotonic()+50
                while self.states[i]['armed'] and time.monotonic()<deadline:await asyncio.sleep(.2)
                if self.states[i]['armed']:raise TimeoutError('Landing did not disarm')
        await asyncio.gather(*(one(i) for i in range(len(self.drones))))
        (self.path/'landing.json').write_text(json.dumps(self.frame(),indent=2))
    async def watchdog(self):
        while True:
            await asyncio.sleep(.25)
            if self.started:
                now=time.monotonic()
                if now-self.last_request>5 or now-self.clock_wall>2 or any(now-s['wall']>2 for s in self.states):
                    self.error='Host/clock/telemetry lease expired';await self.land();self.done.set();return
                if any(not s['armed'] or s['mode']!='OFFBOARD' for s in self.states):
                    self.error='PX4 left armed Offboard flight';await self.land();self.done.set();return
    async def handle(self,reader,writer):
        try:
            while line:=await reader.readline():
                try:
                    request=json.loads(line)
                    if request.get('token')!=self.token:raise ValueError('Authentication failed')
                    self.last_request=time.monotonic();op=request['op']
                    if op=='start':result=await self.start()
                    elif op=='poll':result=self.frame()
                    elif op=='tasks':self.submit(request['tasks']);result={}
                    elif op=='land':await self.land();result={};self.done.set()
                    else:raise ValueError('Unknown operation')
                    response=dict(ok=True,result=result)
                except Exception as e:response=dict(ok=False,error=str(e))
                writer.write((json.dumps(response)+'\n').encode());await writer.drain()
        finally:writer.close();await writer.wait_closed()
    async def smoke(self):
        await self.start();s=self.states[0];o=s['origin'];task=dict(taskId='smoke',ned=[[o['north']+2,o['east']+2,o['down']-5]])
        self.goal_jobs[0]=asyncio.create_task(self.execute(0,task))
        while not self.goal_jobs[0].done():self.last_request=time.monotonic();await asyncio.sleep(.1)
        await self.goal_jobs[0]
        if self.error:raise RuntimeError(self.error)
        (self.path/'smoke.json').write_text(json.dumps(self.frame(),indent=2));await self.land()
    async def close(self):
        with contextlib.suppress(Exception):await self.land()
        for job in self.jobs:job.cancel()
        await asyncio.gather(*self.jobs,return_exceptions=True)
        for sig in (signal.SIGINT,signal.SIGTERM,signal.SIGKILL):
            for p,_,_ in self.children:
                with contextlib.suppress(ProcessLookupError):os.killpg(p.pid,sig)
            await asyncio.sleep(1)
        for p,log,_ in self.children:
            with contextlib.suppress(Exception):await p.wait()
            log.close()
        with contextlib.suppress(FileNotFoundError):
            (pathlib.Path('/tmp')/('llm2swarm-camera-'+self.run)).unlink()
        (self.path/'cleanup.json').write_text(json.dumps([dict(pid=p.pid,name=name,returncode=p.returncode) for p,_,name in self.children],indent=2))

async def main(args):
    lock=open('/opt/llm2swarm/slice.lock','w')
    fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
    b=Bridge(args)
    completed=False
    loop=asyncio.get_running_loop();main_task=asyncio.current_task()
    for sig in (signal.SIGTERM,signal.SIGINT):loop.add_signal_handler(sig,main_task.cancel)
    try:
        await b.launch()
        if args.smoke:await b.smoke()
        else:
            server=await asyncio.start_server(b.handle,'127.0.0.1',args.port)
            (b.path/'session.json').write_text(json.dumps(dict(runId=b.run,port=args.port,token=b.token,pid=os.getpid())))
            async with server:await asyncio.wait_for(b.done.wait(),600)
        completed=True
    finally:
        await b.close()
        (b.path/'supervisor-result.json').write_text(json.dumps(dict(completed=completed,error=b.error)))
        lock.close()

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--vehicles',type=int,choices=[1,3],required=True);p.add_argument('--smoke',action='store_true');p.add_argument('--port',type=int,default=8765);p.add_argument('--output',required=True)
    args=p.parse_args()
    if args.smoke and args.vehicles!=1:p.error('Smoke requires exactly one vehicle')
    asyncio.run(main(args))
