using System;
using System.Linq;
using LLM2Swarm.Experiments;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LLM2Swarm.Tests
{
    public class SearchExperimentTests
    {
        MissionIntent Mission() => new MissionIntent { requiredFindings=20,maxSeconds=600,areas=new[]{"all"} };
        [Test] public void HiddenWorldAndSnapshotsDoNotLeak()
        {
            var a=new SearchEnvironment(new Scenario {seed=11,targetCount=1,sensorRadius=.01f},Mission());
            var b=new SearchEnvironment(new Scenario {seed=913,targetCount=99,sensorRadius=.01f,distribution="clustered"},Mission());
            Assert.That(JsonConvert.SerializeObject(a.Observe()),Is.EqualTo(JsonConvert.SerializeObject(b.Observe())));
            var snapshot=a.Observe();snapshot.positions[0]=new Point(999,999);snapshot.completed[0]=true;
            Assert.That(JsonConvert.SerializeObject(a.Observe()),Is.EqualTo(JsonConvert.SerializeObject(b.Observe())));
            a.Step(new[]{0,1,2});b.Step(new[]{0,1,2});
            Assert.That(JsonConvert.SerializeObject(a.Observe()),Is.EqualTo(JsonConvert.SerializeObject(b.Observe())));
        }
        [TestCase(false)] [TestCase(true)] public void BaselinesShareCompleteWorld(bool compact)
        {
            var e=new SearchEnvironment(new Scenario(),Mission());var strategy=new SystematicStrategy(compact);strategy.Reset(e.Observe());
            while(!e.Finished)e.Step(strategy.Decide(e.Observe()));
            var m=e.Metrics();
            Assert.That(m.found,Is.EqualTo(12));Assert.That(m.finishReason,Is.EqualTo("search_exhausted"));
            Assert.That(m.coverage,Is.GreaterThan(.99));Assert.That(m.totalDistance,Is.EqualTo(m.distanceByDrone.Sum()).Within(1e-6));
        }
        [Test] public void InvalidJointActionIsAtomic()
        {
            var e=new SearchEnvironment(new Scenario(),Mission());string before=JsonConvert.SerializeObject(e.Observe());
            Assert.Throws<ArgumentException>(()=>e.Step(new[]{0,0,1}));
            Assert.That(JsonConvert.SerializeObject(e.Observe()),Is.EqualTo(before));
        }
    }
}
