using System.Collections;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LLM2Swarm.Tests
{
    public class LocalInterpreterTests
    {
        string Root => Path.Combine(Directory.GetParent(Application.dataPath).FullName,"LocalInference");
        string Logs => Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Tests","SimulationChecks","bin","UnityInference");
        [UnityTest] public IEnumerator LocalProposalPlansAndExecutes()
        {
            if (!LocalInterpreter.Available(Root)) Assert.Ignore("Modelo local no instalado.");
            var task = LocalInterpreter.InterpretAsync(Root,Path.Combine(Application.streamingAssetsPath,"Inference"),Logs,
                "Busca primero en el norte y después el resto. Termina al encontrar seis objetivos o pasar dos minutos.",99,CancellationToken.None);
            while (!task.IsCompleted) yield return null;
            var result = task.GetAwaiter().GetResult();
            Assert.That(result.response.status,Is.EqualTo("proposal"));
            Assert.That(result.response.mission.requiredFindings,Is.EqualTo(6));
            Assert.That(result.response.mission.maxSeconds,Is.EqualTo(120));
            Assert.That(result.response.mission.areas,Is.EqualTo(new[] {"north","center","south"}));
            var simulation = new Simulation(MissionPlanner.Plan(new Scenario(),result.response.mission));
            while (!simulation.Finished) simulation.Step();
            Assert.That(simulation.FinishReason,Is.EqualTo("success"));
            Assert.That(simulation.TargetCount,Is.EqualTo(12));
        }
        [UnityTest] public IEnumerator CancellationDoesNotProduceProposal()
        {
            if (!LocalInterpreter.Available(Root)) Assert.Ignore("Modelo local no instalado.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(50);
                var task = LocalInterpreter.InterpretAsync(Root,Path.Combine(Application.streamingAssetsPath,"Inference"),Logs,
                    "Busca seis objetivos en dos minutos.",99,cancellation.Token);
                while (!task.IsCompleted) yield return null;
                Assert.That(task.IsCanceled,Is.True);
            }
        }
    }
}
