using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LLM2Swarm;

static class Evaluation
{
    class Case { public string text { get; set; } public string status { get; set; } public int count { get; set; } public float seconds { get; set; } public string[] areas { get; set; } }
    public static async Task Run(string caseFile)
    {
        string root = Directory.GetCurrentDirectory();
        var cases = JsonConvert.DeserializeObject<Case[]>(File.ReadAllText(Path.Combine(root,"LocalInference",caseFile)));
        string output = Path.Combine(root,"LocalInference","evaluation-results",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        File.Copy(Path.Combine(root,"LocalInference",caseFile),Path.Combine(output,"cases.json"));
        var rows = new List<object>(); int passed=0;
        foreach (var test in cases)
        {
            try
            {
                var result = await LocalInterpreter.InterpretAsync(Path.Combine(root,"LocalInference"),Path.Combine(root,"Assets","StreamingAssets","Inference"),output,test.text,99,CancellationToken.None);
                var actual=result.response;
                bool ok = actual.status == test.status;
                if (ok && test.status == "proposal")
                {
                    ok = actual.mission.requiredFindings == test.count && Math.Abs(actual.mission.maxSeconds-test.seconds)<.001 &&
                        string.Join(",",actual.mission.areas) == string.Join(",",test.areas);
                    var simulation = new Simulation(MissionPlanner.Plan(new Scenario(),actual.mission));
                    while (!simulation.Finished) simulation.Step();
                }
                if (ok) passed++;
                rows.Add(new { test.text,expected=test.status,actual,passed=ok,result.elapsedSeconds,result.requestDirectory });
                Console.WriteLine($"{rows.Count}/{cases.Length} {(ok ? "PASS" : "FAIL")} {test.text}");
            }
            catch (Exception e) { rows.Add(new { test.text,passed=false,error=e.Message }); Console.WriteLine("ERROR "+test.text+": "+e.Message); }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonConvert.SerializeObject(rows,Formatting.Indented));
        }
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonConvert.SerializeObject(new { passed,total=cases.Length,accuracy=(double)passed/cases.Length },Formatting.Indented));
        Console.WriteLine($"RESULT: {passed}/{cases.Length}. {output}");
    }
}
