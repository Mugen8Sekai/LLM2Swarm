using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LLM2Swarm;
using LLM2Swarm.Experiments;

var strategy=new ExportedPolicyStrategy(File.ReadAllText(args[0]));
string line;
while((line=Console.ReadLine())!=null)
{
    var request=JObject.Parse(line);
    if(request["observation"]!=null)
    {
        var observation=request["observation"].ToObject<SearchObservation>();
        Console.WriteLine(JsonConvert.SerializeObject(new{action=strategy.Decide(observation),logits=strategy.Logits(observation)}));
    }
    else
    {
        var env=new SearchEnvironment(request["scenario"].ToObject<Scenario>(),request["mission"].ToObject<MissionIntent>(),request["config"]?.ToObject<SearchConfig>());
        ISearchStrategy selected=request["baseline"]==null ? strategy : new SystematicStrategy((bool)request["baseline"]);
        selected.Reset(env.Observe());
        while(!env.Finished)env.Step(selected.Decide(env.Observe()));
        Console.WriteLine(JsonConvert.SerializeObject(env.Metrics()));
    }
}
