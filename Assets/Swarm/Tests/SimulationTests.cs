using NUnit.Framework;

namespace LLM2Swarm.Tests
{
    public class SimulationTests
    {
        [Test] public void PhasesPreserveWorldAndRestrictSearch()
        {
            var intent = new MissionIntent { requiredFindings=20,maxSeconds=600,areas=new[] {"north","center","south"} };
            var sim = new Simulation(MissionPlanner.Plan(new Scenario(),intent));
            while (sim.ActivePhase == 0 && !sim.Finished)
            {
                foreach (var found in sim.Findings) Assert.That(found.position.y,Is.GreaterThanOrEqualTo(40f*2/3));
                sim.Step();
            }
            while (!sim.Finished) sim.Step();
            Assert.That(sim.TargetCount,Is.EqualTo(12));
            Assert.That(sim.Findings.Count,Is.EqualTo(12));
            Assert.That(sim.PhaseEvents.Count,Is.EqualTo(3));
            Assert.That(sim.FinishReason,Is.EqualTo("search_exhausted"));
        }
        [Test] public void SameMissionReplaysEveryMetric()
        {
            var a = new Simulation(Mission.Sweep()); var b = new Simulation(Mission.Sweep());
            while (!a.Finished)
            {
                a.Step(); b.Step();
                Assert.That(a.Coverage, Is.EqualTo(b.Coverage));
                Assert.That(a.Findings.Count, Is.EqualTo(b.Findings.Count));
            }
            Assert.That(a.FinishReason, Is.EqualTo("success"));
            Assert.That(a.Findings.Count, Is.EqualTo(12));
        }
        [Test] public void ExhaustionDoesNotMeanSuccess()
        {
            var m = Mission.Sweep(); m.sensorRadius = .0001f;
            m.starts=new[]{new Point(0,0),new Point(0,0),new Point(0,0)};
            foreach (var route in m.routes) route.waypoints = new[] {new Point(0,0),new Point(0,0)};
            var s = new Simulation(m); s.Step();
            Assert.That(s.FinishReason, Is.EqualTo("search_exhausted"));
        }
        [Test] public void SensorCoversSegmentsAndCountsUniqueTargets()
        {
            var m = Mission.Sweep(); m.speed = 10000;
            var s = new Simulation(m); s.Step();
            Assert.That(s.Coverage, Is.InRange(.99,1.000001));
            Assert.That(s.Findings.Count, Is.EqualTo(12));
            s.Step(); Assert.That(s.Findings.Count, Is.EqualTo(12));
        }
    }
}
