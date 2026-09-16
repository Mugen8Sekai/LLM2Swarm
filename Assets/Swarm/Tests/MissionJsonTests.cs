using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace LLM2Swarm.Tests
{
    public class MissionJsonTests
    {
        [Test] public void SupportedMissionRoundTrips()
        {
            var mission = MissionJson.Parse(JsonUtility.ToJson(Mission.Sweep()));
            Assert.That(mission.requiredFindings, Is.EqualTo(12));
            Assert.That(mission.routes.Length, Is.EqualTo(3));
        }
        [Test] public void UnsupportedPriorityIsRejected()
        {
            string json = JsonUtility.ToJson(Mission.Sweep());
            json = json.Insert(1,"\"priorities\": [\"north\"],");
            Assert.Throws<JsonSerializationException>(() => MissionJson.Parse(json));
        }
        [Test] public void DuplicateFieldsAreRejected()
        {
            string json = JsonUtility.ToJson(Mission.Sweep());
            json = json.Insert(1,"\"seed\": 99,");
            Assert.Throws<JsonReaderException>(() => MissionJson.Parse(json));
        }
    }
}
