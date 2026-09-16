using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LLM2Swarm
{
    // Keep unsupported instructions visible as errors instead of silently discarding them.
    public static class MissionJson
    {
        public static Mission Parse(string json)
        {
            var document = JObject.Parse(json, new JsonLoadSettings {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            var serializer = JsonSerializer.Create(new JsonSerializerSettings {
                MissingMemberHandling = MissingMemberHandling.Error,
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore
            });
            var mission = document.ToObject<Mission>(serializer);
            if (mission == null) throw new ArgumentException("Introduce un objeto JSON de misión.");
            mission.Validate();
            return mission;
        }
    }
}
