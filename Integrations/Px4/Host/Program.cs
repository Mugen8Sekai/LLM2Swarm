using LLM2Swarm;
using LLM2Swarm.Experiments;
using LLM2Swarm.Px4;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

if(args.Length==0||args[0]=="test") { Checks.Run(); return; }
if(args.Length!=3||args[0]!="run")throw new ArgumentException("test | run session.json output-directory");
var session=JObject.Parse(File.ReadAllText(args[1]));
Directory.CreateDirectory(args[2]);
await using var backend=new BridgeBackend((string)session["token"]);
await backend.Connect((int)session["port"]);
await new MissionHost().Run(backend,args[2]);

static class Checks
{
    static void Assert(bool value,string label){if(!value)throw new Exception(label);}
    public static void Run()
    {
        int routes=0;
        foreach(var c in new[]{new SearchConfig(),new SearchConfig{columns=3,rows=2}})
        {
            var s=MissionHost.Scenario();var env=new SearchEnvironment(s,MissionHost.Intent(),c);
            var method=typeof(SearchEnvironment).GetMethod("RegionRoute",BindingFlags.NonPublic|BindingFlags.Instance);
            foreach(var start in s.starts)for(int r=0;r<c.columns*c.rows;r++)
            {var original=(Point[])method.Invoke(env,new object[]{r,start});Assert(JsonConvert.SerializeObject(original)==JsonConvert.SerializeObject(LocalPlanner.Route(s,c,r,start)),"route parity");routes++;}
            var a=new SystematicStrategy(false);var b=new SystematicStrategy(false);var o=env.Observe();a.Reset(o);b.Reset(o);
            for(int i=0;i<10&&!env.Finished;i++){o=env.Observe();var x=a.Decide(o);Assert(x.SequenceEqual(b.Decide(o)),"strategy replay");env.Step(x);}
        }
        for(int i=0;i<3;i++)
        {var origin=new NedOrigin{north=2+i,east=-4-i,down=.3,x=10+20*i,y=0};var n=origin.ToNed(22,17,5+3*i);var p=origin.ToMap(n[0],n[1]);Assert(Point.Distance(p,new Point(22,17))<1e-5&&Math.Abs(n[2]-(.3-5-3*i))<1e-6,"NED transform");}
        var ledger=new TelemetryLedger(MissionHost.Scenario(),MissionHost.Intent());ledger.Update(MissionHost.Scenario().starts,0);
        foreach(double t in new[]{0d,-1d,2d}){bool rejected=false;try{ledger.Update(MissionHost.Scenario().starts,t);}catch{rejected=true;}Assert(rejected,"timestamp rejection");}
        Assert(ledger.Metrics("").totalDistance==0,"rejected telemetry mutation");
        var scenario=MissionHost.Scenario();var intent=MissionHost.Intent();
        var ppo=MissionHost.CreateStrategy(3);
        var ppoEnvironment=new SearchEnvironment(scenario,intent);
        ppo.Reset(ppoEnvironment.Observe());
        for(int step=0;step<5&&!ppoEnvironment.Finished;step++)
            ppoEnvironment.Step(ppo.Decide(ppoEnvironment.Observe()));
        Assert(ppo.Id=="PPO-centralized-pilot-full-47","PPO checkpoint identity");
        Console.WriteLine("PASS: PX4 PPO factory, checkpoint loading and five validated decisions.");
        var mission=MissionPlanner.Plan(scenario,intent);var sim=new Simulation(mission,true,false);
        var measured=new TelemetryLedger(scenario,intent);measured.Update(sim.Drones,0);
        for(int d=0;d<3;d++)sim.Assign(d,new[]{new Point(scenario.starts[d].x,35)});
        for(int i=0;i<60&&!sim.Finished;i++)
        {
            sim.Step();measured.Update(sim.Drones,sim.Seconds);var metrics=measured.Metrics("");
            Assert(Math.Abs(metrics.coverage-sim.Coverage)<1e-10,"coverage parity");
            Assert(metrics.found==sim.Findings.Count,"detection parity");
            Assert(Math.Abs(metrics.totalDistance-sim.DistanceByDrone.Sum())<1e-8,"distance parity");
            Assert(Math.Abs(metrics.exposureAreaSeconds-sim.ExposureAreaSeconds)<1e-6,"exposure parity");
            Assert(Math.Abs(metrics.redundantAreaSeconds-sim.RedundantAreaSeconds)<1e-6,"redundancy parity");
        }
        Console.WriteLine($"PASS: {routes} differential routes; A replay; 3 NED origins; invalid telemetry; 60 sensor/metrics trace samples.");
    }
}
