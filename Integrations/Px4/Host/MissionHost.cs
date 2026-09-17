using LLM2Swarm.Experiments;
using Newtonsoft.Json;

namespace LLM2Swarm.Px4;

public sealed class MissionHost
{
    public static Scenario Scenario()=>new Scenario{seed=100000,starts=new[]{new Point(10,0),new Point(30,0),new Point(50,0)}};
    public static MissionIntent Intent()=>new MissionIntent{requiredFindings=12,maxSeconds=120,areas=new[]{"all"}};
    public async Task Run(IExecutionBackend backend,string output)
    {
        var scenario=Scenario();var intent=Intent();var config=new SearchConfig();var ledger=new TelemetryLedger(scenario,intent);
        var strategy=new SystematicStrategy(false);var completed=new bool[24];var assignments=new[]{-1,-1,-1};var active=new WaypointTask[3];
        int sequence=0,snapshotSequence=0,snapshotPublishErrors=0;double last=-1,nextDecision=0;string reason="aborted",failure=null;bool reset=false;
        using var log=new StreamWriter(Path.Combine(output,"events.jsonl")){AutoFlush=true};
        void Record(object value)=>log.WriteLine(JsonConvert.SerializeObject(value));
        void Publish(object value)
        {
            string path=Path.Combine(output,"snapshot.json"),temporary=path+".tmp";
            try{File.WriteAllText(temporary,JsonConvert.SerializeObject(value));File.Move(temporary,path,true);}
            catch(IOException){snapshotPublishErrors++;TryDelete(temporary);}
            catch(UnauthorizedAccessException){snapshotPublishErrors++;TryDelete(temporary);}
        }
        static void TryDelete(string path){try{File.Delete(path);}catch{}}
        try
        {
            var frame=await backend.Start();double start=frame.simTime;string runId=frame.runId;
            Record(new{type="configuration",runId,scenario,intent,config,strategy=strategy.Id,heights=new[]{5,8,11},origins=frame.vehicles.Select(v=>v.origin)});
            while(true)
            {
                if(frame.runId!=runId||frame.error!=null||frame.clockAge>2||frame.vehicles.Length!=3)throw new IOException("Invalid bridge frame: "+frame.error);
                if(File.Exists(Path.Combine(output,"abort.request"))){reason="aborted";Record(new{type="abort-requested",seconds=Math.Max(0,frame.simTime-start)});break;}
                var vehicles=frame.vehicles.OrderBy(v=>v.id).ToArray();
                if(!vehicles.Select(v=>v.id).SequenceEqual(new[]{0,1,2})||vehicles.Any(v=>v.sysId!=v.id+1||v.ageWall>2||!v.connected||v.error!=null))throw new IOException("Stale or invalid vehicle telemetry");
                double time=frame.simTime-start;
                if(time<last)throw new IOException("Simulation clock reset");
                if(time>last+.05)
                {
                    var positions=vehicles.Select(v=>v.origin.ToMap(v.north,v.east)).ToArray();ledger.Update(positions,time);last=time;
                    for(int d=0;d<3;d++)if(active[d]!=null&&vehicles[d].taskId==active[d].taskId&&vehicles[d].taskState=="completed")
                    {completed[assignments[d]]=true;assignments[d]=-1;active[d]=null;}
                    var metrics=ledger.Metrics("");var regional=ledger.Regional(config);
                    var observation=new SearchObservation{width=scenario.width,height=scenario.height,speed=scenario.speed,sensorRadius=scenario.sensorRadius,columns=6,rows=4,
                        requiredFindings=intent.requiredFindings,found=metrics.found,seconds=time,remainingSeconds=Math.Max(0,intent.maxSeconds-time),positions=positions,
                        idle=active.Select(t=>t==null).ToArray(),completed=(bool[])completed.Clone(),assignments=(int[])assignments.Clone(),regionCoverage=regional.Item1,regionFindings=regional.Item2,
                        remainingDistance=new double[3]};
                    for(int d=0;d<3;d++)if(active[d]!=null)
                    {var p=positions[d];foreach(var point in active[d].points.Skip(vehicles[d].waypoint)){observation.remainingDistance[d]+=Point.Distance(p,point);p=point;}}
                    var routes=active.Select((task,vehicleId)=>new{vehicleId,taskId=task?.taskId,regionId=task?.regionId??-1,
                        points=task?.points??Array.Empty<Point>()}).ToArray();
                    var snapshot=new{type="mission-snapshot",schemaVersion=1,sequence=++snapshotSequence,runId,missionRevision=1,
                        seconds=time,scenario,intent,positions,vehicles,metrics,findings=ledger.findings,covered=ledger.covered,
                        routes,assignments,completed,completedRegions=completed.Count(x=>x),totalRegions=completed.Length};
                    Record(snapshot);
                    Publish(snapshot);
                    if(metrics.found>=intent.requiredFindings){reason="success";break;}
                    if(time>=intent.maxSeconds){reason="deadline";break;}
                    if(completed.All(x=>x)){reason="exhausted";break;}
                    if(time>=nextDecision)
                    {
                        if(!reset){strategy.Reset(observation);reset=true;}
                        var actions=strategy.Decide(observation);var reserved=new HashSet<int>(assignments.Where(x=>x>=0));var tasks=new List<WaypointTask>();
                        for(int d=0;d<3;d++)if(actions[d]!=-1)
                        {
                            int r=actions[d];if(active[d]!=null||r<0||r>=24||completed[r]||!reserved.Add(r))throw new InvalidOperationException("Invalid strategy assignment");
                            var points=LocalPlanner.Route(scenario,config,r,positions[d]);
                            tasks.Add(new WaypointTask{runId=runId,taskId=$"{runId}-{++sequence}-{d}",sequence=sequence,vehicleId=d,regionId=r,points=points,ned=points.Select(p=>vehicles[d].origin.ToNed(p.x,p.y,5+3*d)).ToArray()});
                        }
                        if(tasks.Count>0){await backend.Submit(tasks.ToArray());foreach(var t in tasks){active[t.vehicleId]=t;assignments[t.vehicleId]=t.regionId;}Record(new{type="tasks",seconds=time,tasks});}
                        nextDecision=time+config.decisionSeconds;
                    }
                }
                await Task.Delay(50);frame=await backend.Poll();
            }
        }
        catch(Exception e){failure=e.ToString();throw;}
        finally
        {
            try{await backend.Land();}catch(Exception e){failure=(failure??"")+" LAND: "+e.Message;reason="aborted";}
            var report=new{reason,failure,metrics=ledger.Metrics(reason),tasksIssued=sequence,completedRegions=completed.Count(x=>x),snapshotPublishErrors};
            File.WriteAllText(Path.Combine(output,"summary.json"),JsonConvert.SerializeObject(report,Formatting.Indented));Console.WriteLine(JsonConvert.SerializeObject(report));
        }
    }
}
