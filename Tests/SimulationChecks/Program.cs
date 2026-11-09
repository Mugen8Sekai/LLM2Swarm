using System;
using LLM2Swarm;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main(string[] args)
    {
        if(args.Length>0 && args[0]=="pilot") { SearchPilot.Run(); return; }
        if(args.Length>0 && args[0]=="serve") { SearchPilot.Serve(); return; }
        if(args.Length>0 && args[0]=="bench") { SearchPilot.Benchmark(); return; }
        SearchPilot.Checks();
        if (args.Length > 0 && args[0] == "evaluate") { Evaluation.Run(
            args.Length > 1 ? args[1] : "evaluation-cases.json",
            args.Length > 2 ? args[2] : LocalInterpreter.ModelFile,
            args.Length > 3 ? args[3] : "installed.json",
            args.Length > 4 ? args[4] : null).GetAwaiter().GetResult(); return; }
        var scenario = new Scenario();
        var phased = new Simulation(MissionPlanner.Plan(scenario,new MissionIntent { requiredFindings=20,maxSeconds=600,areas=new[] {"north","center","south"} }));
        while (phased.ActivePhase == 0 && !phased.Finished)
        {
            for (int i=0;i<phased.Covered.Length;i++)
                Check(!phased.Covered[i] || phased.CellCenter(i).y >= scenario.height*2/3,"No coverage outside first phase");
            phased.Step();
        }
        while (!phased.Finished) phased.Step();
        Check(phased.TargetCount == 12 && phased.Findings.Count == 12,"Requested count does not change hidden world");
        Check(phased.PhaseEvents.Count == 3 && phased.FinishReason == "search_exhausted","Ordered phases and exhaustion");
        Check(phased.Coverage > .99,"Phased region coverage");
        var onlyNorth = new Simulation(MissionPlanner.Plan(scenario,new MissionIntent { requiredFindings=20,maxSeconds=600,areas=new[] {"north"} }));
        while (!onlyNorth.Finished) onlyNorth.Step();
        Check(onlyNorth.Coverage < .35,"North only restriction");
        foreach (var f in onlyNorth.Findings) Check(f.position.y >= scenario.height*2/3,"No detection outside north");
        bool missingIntentRejected=false;
        try { Interpretation.Parse("{\"status\":\"proposal\",\"message\":\"ok\",\"mission\":{\"areas\":[\"all\"]}}"); }
        catch (Newtonsoft.Json.JsonSerializationException) { missingIntentRejected=true; }
        Check(missingIntentRejected,"Missing fields rejected");
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(Mission.Sweep());
        Check(MissionJson.Parse(json).routes.Length == 3,"JSON round trip");
        bool unknownRejected = false, duplicateRejected = false;
        try { MissionJson.Parse(json.Insert(1,"\"priorities\":[\"north\"],")); }
        catch (Newtonsoft.Json.JsonSerializationException) { unknownRejected = true; }
        try { MissionJson.Parse(json.Insert(1,"\"seed\":99,")); }
        catch (Newtonsoft.Json.JsonReaderException) { duplicateRejected = true; }
        Check(unknownRejected && duplicateRejected,"Unsupported and duplicate JSON fields rejected");
        var a = new Simulation(Mission.Sweep()); var b = new Simulation(Mission.Sweep());
        double previous = 0;
        while (!a.Finished)
        {
            a.Step(); b.Step();
            Check(a.Coverage >= previous && a.Coverage <= 1.000001,"Coverage bounds/monotonicity"); previous = a.Coverage;
            Check(a.Coverage == b.Coverage && a.Findings.Count == b.Findings.Count,"Deterministic replay");
            foreach (var p in a.Drones) Check(p.x >= 0 && p.x <= 60 && p.y >= 0 && p.y <= 40,"Region bounds");
        }
        Check(a.FinishReason == "success" && a.Coverage > 0 && a.Findings.Count == 12,"Sweep completion");
        for (int i = 0; i < a.Findings.Count; i++) Check(a.Findings[i].tick == b.Findings[i].tick && a.Findings[i].target == b.Findings[i].target,"Finding replay");
        int tick = a.Tick; a.Step(); Check(tick == a.Tick,"Finished run immutable");
        var different = new Simulation(Mission.Sweep(43)); while (!different.Finished) different.Step();
        Check(Point.Distance(a.Findings[0].position,different.Findings[0].position) > 0,"Seed changes scenario");
        var limited = Mission.Sweep(); limited.maxSeconds = 1;
        var timeout = new Simulation(limited); while (!timeout.Finished) timeout.Step();
        Check(timeout.FinishReason == "time_limit" && timeout.Tick == 10,"Time limit");
        var invalid = Mission.Sweep(); invalid.routes[0].waypoints[0] = new Point(-1,0);
        bool rejected = false; try { new Simulation(invalid); } catch (ArgumentException) { rejected = true; }
        Check(rejected,"Invalid route rejected");
        var fast = Mission.Sweep(); fast.speed = 10000;
        var swept = new Simulation(fast); swept.Step();
        Check(swept.Coverage > .99 && swept.Findings.Count == 12,"Continuous sensor over long movement");
        var exhaustedMission = Mission.Sweep(); exhaustedMission.sensorRadius = .0001f;
        exhaustedMission.starts=new[]{new Point(0,0),new Point(0,0),new Point(0,0)};
        for (int d = 0; d < 3; d++) exhaustedMission.routes[d].waypoints = new[] { new Point(0,0),new Point(0,0) };
        var exhausted = new Simulation(exhaustedMission); exhausted.Step(); Check(exhausted.FinishReason == "search_exhausted","Exhaustion is not success");
        var interrupted = new Simulation(Mission.Sweep()); interrupted.Interrupt(); interrupted.Step();
        Check(interrupted.FinishReason == "interrupted" && interrupted.Tick == 0,"Interruption");
        Console.WriteLine($"PASS: deterministic replay, bounds, coverage, discoveries, timeout, validation, swept sensing. Default: {a.Seconds:F1}s, {a.Coverage:P2}, {a.Findings.Count} findings.");
    }
}
