using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LLM2Swarm;
using LLM2Swarm.Experiments;

static class SearchPilot
{
    static string Root => Directory.GetCurrentDirectory();
    static MissionIntent Intent(int k=12,int deadline=120) => new MissionIntent {requiredFindings=k,maxSeconds=deadline,areas=new[]{"all"}};
    static void Check(bool b,string name) { if(!b) throw new Exception(name); }
    public static void Checks()
    {
        var s1=new Scenario { seed=11,targetCount=1,sensorRadius=.01f };
        var s2=new Scenario { seed=913,targetCount=99,sensorRadius=.01f,distribution="clustered" };
        var a=new SearchEnvironment(s1,Intent());var b=new SearchEnvironment(s2,Intent());
        Check(JsonConvert.SerializeObject(a.Observe())==JsonConvert.SerializeObject(b.Observe()),"Hidden worlds distinguishable before detection");
        string snapshot=JsonConvert.SerializeObject(a.Observe());
        var observation=a.Observe(); observation.positions[0]=new Point(99,99);observation.completed[0]=true;observation.regionCoverage[1]=9;
        Check(JsonConvert.SerializeObject(a.Observe())==snapshot,"Observation aliases simulator");
        s1.width=1;s1.targetCount=999;
        Check(JsonConvert.SerializeObject(a.Observe())==snapshot,"Scenario aliases simulator");
        string[] banned={"seed","targets","targetCount","distribution","clusters","clusterSpread","scenario","simulation","reward"};
        foreach(var f in typeof(SearchObservation).GetFields()) Check(!banned.Contains(f.Name),"Privileged observation field: "+f.Name);
        bool invalid=false; try {a.Step(new[]{0,0,1});}catch(ArgumentException){invalid=true;}
        Check(invalid && JsonConvert.SerializeObject(a.Observe())==snapshot,"Invalid action mutated state");
        a.Step(new[]{0,1,2});b.Step(new[]{0,1,2});
        Check(JsonConvert.SerializeObject(a.Observe())==JsonConvert.SerializeObject(b.Observe()),"Hidden metadata changes behavior before detection");
        var source=new Scenario { starts=new[]{new Point(0,0),new Point(0,0),new Point(0,0)} };
        var left=new SearchEnvironment(source,Intent());var right=new SearchEnvironment(source,Intent());
        Check(JsonConvert.SerializeObject(left.Observe().positions)==JsonConvert.SerializeObject(right.Observe().positions),"Unpaired starts");
        left.Step(new[]{23,22,21});
        foreach(var p in left.Observe().positions) Check(Point.Distance(p,new Point(0,0))<=4.00001,"Teleport or velocity violation");
        Check(left.Observe().regionCoverage[0]>0,"Sensor censored outside assigned regions");
        foreach(bool compact in new[]{false,true})
        {
            var e=new SearchEnvironment(new Scenario(),Intent(20,600));var strategy=new SystematicStrategy(compact);strategy.Reset(e.Observe());
            while(!e.Finished)e.Step(strategy.Decide(e.Observe()));
            var m=e.Metrics();Check(m.finishReason=="search_exhausted"&&m.found==12&&m.coverage>.99,"Baseline fails full search");
            Check(Math.Abs(m.totalDistance-m.distanceByDrone.Sum())<1e-6,"Distance accounting");
            Check(Math.Abs(m.newAreaByDrone.Sum()-m.coverage*2400)<1e-6,"Coverage ownership");
            Check(m.redundantAreaSeconds>=0&&m.redundantAreaSeconds<=m.exposureAreaSeconds,"Redundancy bounds");
            var repeat=new SearchEnvironment(new Scenario(),Intent(20,600));strategy.Reset(repeat.Observe());
            while(!repeat.Finished)repeat.Step(strategy.Decide(repeat.Observe()));
            Check(JsonConvert.SerializeObject(m)==JsonConvert.SerializeObject(repeat.Metrics()),"Replay differs");
        }
        var limit=new SearchEnvironment(new Scenario(),Intent(100,1)); limit.Step(new[]{-1,-1,-1});
        Check(limit.Metrics().seconds==1&&limit.Metrics().finishReason=="time_limit","Exact deadline");
        var alternate=new SearchEnvironment(new Scenario(),Intent(),new SearchConfig {columns=3,rows=2,decisionSeconds=.5});
        Check(alternate.Observe().completed.Length==6,"Grid not configurable");
        Console.WriteLine("PASS: hidden-state counterfactuals, snapshot isolation, atomic actions, paired starts, travel, baseline exhaustion, replay, metrics, exact deadline, alternate grid.");
    }
    public static object Transition(SearchEnvironment env) => new {observation=env.Observe(),terminated=env.Finished,terminalMetrics=env.Finished ? env.Metrics() : null};
    static int[] Dispatch(SearchObservation o)
    {
        var reserved=new HashSet<int>(o.assignments.Where(x=>x>=0));var action=new[]{-1,-1,-1};
        for(int d=0;d<3;d++)if(o.idle[d])for(int r=0;r<o.completed.Length;r++)if(!o.completed[r]&&reserved.Add(r)){action[d]=r;break;}
        return action;
    }
    public static void Benchmark()
    {
        for(int i=0;i<5;i++){var w=new SearchEnvironment(new Scenario{seed=90000},Intent(100));while(!w.Finished)w.Step(Dispatch(w.Observe()));}
        var results=new List<object>();SearchMetrics first=null;
        for(int repeat=0;repeat<5;repeat++)
        {
            int transitions=0;var clock=Stopwatch.StartNew();
            for(int episode=0;episode<100;episode++)
            {
                var e=new SearchEnvironment(new Scenario {seed=90000+episode},Intent(100));
                while(!e.Finished){e.Step(Dispatch(e.Observe()));transitions++;}
                var metrics=e.Metrics();if(episode==0)first=metrics;
            }
            results.Add(new{episodes=100,transitions,seconds=clock.Elapsed.TotalSeconds,transitionsPerSecond=transitions/clock.Elapsed.TotalSeconds});
        }
        Directory.CreateDirectory("Experiments/results");
        string json=JsonConvert.SerializeObject(new{protocol="in-process",repeats=results,terminalMetricsFirstEpisode=first,includes="reset, observations, deterministic dispatcher, metrics; excludes serialization and IPC",training=false},Formatting.Indented);
        File.WriteAllText("Experiments/results/core-throughput.json",json);Console.WriteLine(json);
    }
    public static void Serve()
    {
        SearchEnvironment env=null;string line;
        while((line=Console.ReadLine())!=null)
        {
            try
            {
                var request=JObject.Parse(line);string op=(string)request["op"];
                if(op=="close") {Console.WriteLine("{\"ok\":true}");return;}
                if(op=="reset") env=new SearchEnvironment(request["scenario"].ToObject<Scenario>(),request["mission"].ToObject<MissionIntent>(),request["config"]?.ToObject<SearchConfig>());
                else if(op=="step") {if(env==null)throw new ArgumentException("Reset first");env.Step(request["action"].ToObject<int[]>());}
                else if(op!="observe")throw new ArgumentException("Unknown operation");
                if(env==null)throw new ArgumentException("Reset first");
                Console.WriteLine(JsonConvert.SerializeObject(new {ok=true,result=Transition(env)}));
            }
            catch(Exception e){Console.WriteLine(JsonConvert.SerializeObject(new{ok=false,error=e.Message}));}
        }
    }
    public static void Run()
    {
        Checks();
        string output=Path.Combine(Root,"Experiments","results",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
        string fingerprint;
        using(var sha=SHA256.Create())fingerprint=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",new[]{"Assets/Swarm/Simulation.cs","Assets/Swarm/MissionIntent.cs","Assets/Swarm/SearchExperiment.cs","Tests/SimulationChecks/SearchPilot.cs"}.Select(File.ReadAllText))))).Replace("-","").ToLowerInvariant();
        File.WriteAllText(Path.Combine(output,"config.json"),JsonConvert.SerializeObject(new { split="pilot-validation",seedRange="100000..100099",sourceSha256=fingerprint,config=new SearchConfig(),targetCounts=new[]{6,12,24},kValues="3 and targetCount",deadlines=new[]{60,120},distributions=new[]{"uniform","clustered"},starts=new[]{"common","dispersed"},trainingPerformed=false },Formatting.Indented));
        var records=new List<Row>();long transitions=0;var clock=Stopwatch.StartNew();
        using(var details=new StreamWriter(Path.Combine(output,"episodes.jsonl")))
        using(var csv=new StreamWriter(Path.Combine(output,"episodes.csv")))
        {
            csv.WriteLine("seed,distribution,start_layout,k,deadline,strategy,success,seconds,first,k_time,found,found_fraction,coverage,distance,d0,d1,d2,redundant_area_seconds,exposure_area_seconds,new_area0,new_area1,new_area2,findings0,findings1,findings2,target_count");
            foreach(int targetCount in new[]{6,12,24})foreach(string distribution in new[]{"uniform","clustered"})foreach(string layout in new[]{"common","dispersed"})foreach(int k in new[]{3,targetCount})foreach(int deadline in new[]{60,120})
            {
                var timer=Stopwatch.StartNew();
                for(int seed=100000;seed<100100;seed++)
                {
                    var scenario=new Scenario {seed=seed,targetCount=targetCount,distribution=distribution,starts=layout=="common" ? new[]{new Point(30,0),new Point(30,0),new Point(30,0)} : new[]{new Point(10,0),new Point(30,0),new Point(50,0)}};
                    foreach(bool compact in new[]{false,true})
                    {
                        var env=new SearchEnvironment(scenario,Intent(k,deadline));var policy=new SystematicStrategy(compact);policy.Reset(env.Observe());
                        while(!env.Finished){env.Step(policy.Decide(env.Observe()));transitions++;}
                        var m=env.Metrics();var row=new Row {seed=seed,targetCount=targetCount,distribution=distribution,layout=layout,k=k,deadline=deadline,strategy=policy.Id,metrics=m};records.Add(row);
                        details.WriteLine(JsonConvert.SerializeObject(new {row,scenario,mission=Intent(k,deadline)}));
                        object[] values={seed,distribution,layout,k,deadline,policy.Id,m.success,m.seconds,m.firstFindingSeconds,m.kFindingSeconds,m.found,m.foundFraction,m.coverage,m.totalDistance,m.distanceByDrone[0],m.distanceByDrone[1],m.distanceByDrone[2],m.redundantAreaSeconds,m.exposureAreaSeconds,m.newAreaByDrone[0],m.newAreaByDrone[1],m.newAreaByDrone[2],m.findingsByDrone[0],m.findingsByDrone[1],m.findingsByDrone[2],targetCount};
                        csv.WriteLine(string.Join(",",values.Select(v=>Convert.ToString(v,CultureInfo.InvariantCulture))));
                    }
                }
                Console.WriteLine($"N{targetCount}/{distribution}/{layout}/K{k}/T{deadline}: 200 episodes in {timer.Elapsed.TotalSeconds:F2}s");
            }
        }
        var summary=records.GroupBy(r=>new {r.targetCount,r.distribution,r.layout,r.k,r.deadline,r.strategy}).Select(g=>new {condition=g.Key,n=g.Count(),successRate=g.Average(x=>x.metrics.success?1d:0),meanFound=g.Average(x=>(double)x.metrics.found),meanCoverage=g.Average(x=>x.metrics.coverage),meanRestrictedTime=g.Average(x=>x.metrics.kFindingSeconds??x.deadline),meanDistance=g.Average(x=>x.metrics.totalDistance)}).ToArray();
        var paired=records.GroupBy(r=>new {r.targetCount,r.distribution,r.layout,r.k,r.deadline}).Select(g=>{
            var differences=g.GroupBy(r=>r.seed).Select(pair=>{var a=pair.Single(r=>r.strategy=="A-systematic");var b=pair.Single(r=>r.strategy=="B-coordinated");return (b.metrics.kFindingSeconds??b.deadline)-(a.metrics.kFindingSeconds??a.deadline);}).ToArray();
            double mean=differences.Average(),se=Math.Sqrt(differences.Sum(x=>(x-mean)*(x-mean))/(differences.Length-1)/differences.Length);
            return new {condition=g.Key,pairedSeeds=differences.Length,restrictedTimeBminusA=mean,approximate95Lower=mean-1.984*se,approximate95Upper=mean+1.984*se};}).ToArray();
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonConvert.SerializeObject(new {episodes=records.Count,transitions,seconds=clock.Elapsed.TotalSeconds,episodesPerSecond=records.Count/clock.Elapsed.TotalSeconds,transitionsPerSecond=transitions/clock.Elapsed.TotalSeconds,summary,paired},Formatting.Indented));
        Console.WriteLine("RESULT "+output);
    }
    sealed class Row {public int seed,k,deadline,targetCount;public string distribution,layout,strategy;public SearchMetrics metrics;}
}
