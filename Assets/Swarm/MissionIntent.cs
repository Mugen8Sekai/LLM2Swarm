using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LLM2Swarm
{
    [Serializable] public class Scenario
    {
        public int seed = 42, targetCount = 12;
        public float width = 60, height = 40, speed = 4, sensorRadius = 3, cellSize = 1, stepSeconds = .1f;
        public Point[] starts;
        public string distribution = "uniform";
        public int clusters = 3;
        public float clusterSpread = 4;
        public static Scenario From(Mission m) => new Scenario { seed=m.seed,targetCount=m.targetCount,width=m.width,height=m.height,
            speed=m.speed,sensorRadius=m.sensorRadius,cellSize=m.cellSize,stepSeconds=m.stepSeconds,starts=m.starts == null ? null : (Point[])m.starts.Clone(),distribution=m.distribution,clusters=m.clusters,clusterSpread=m.clusterSpread };
    }
    [Serializable] public class MissionIntent
    {
        [JsonProperty(Required=Required.Always)] public int requiredFindings;
        [JsonProperty(Required=Required.Always)] public float maxSeconds;
        [JsonProperty(Required=Required.Always)] public string[] areas;
        public void Validate()
        {
            if (requiredFindings < 1 || requiredFindings > 10000 || float.IsNaN(maxSeconds) || float.IsInfinity(maxSeconds) || maxSeconds <= 0 || maxSeconds > 86400)
                throw new ArgumentException("Objetivos entre 1 y 10000; tiempo entre 0 y 86400 s.");
            if (areas == null || areas.Length == 0 || areas.Length > 3) throw new ArgumentException("Se requieren entre una y tres zonas.");
            var seen = new HashSet<string>();
            foreach (var area in areas)
                if (!(area == "all" || area == "north" || area == "center" || area == "south") || !seen.Add(area))
                    throw new ArgumentException("Zona desconocida o repetida.");
            if (seen.Contains("all") && areas.Length != 1) throw new ArgumentException("La zona completa no se combina con otras zonas.");
        }
        public string Summary() => $"Encontrar {requiredFindings} objetivos distintos en un máximo de {maxSeconds} s. Zonas, por orden: {string.Join(" → ", Array.ConvertAll(areas,AreaName))}. Cada fase acaba al completar las rutas de sus tres drones.";
        public static string AreaName(string a) => a == "north" ? "norte" : a == "center" ? "centro" : a == "south" ? "sur" : "región completa";
    }
    [Serializable] public class Interpretation
    {
        [JsonProperty(Required=Required.Always)] public string status;
        [JsonProperty(Required=Required.Always)] public string message;
        [JsonProperty(Required=Required.AllowNull)] public MissionIntent mission;
        public static Interpretation Parse(string json)
        {
            var doc = JObject.Parse(json,new JsonLoadSettings { DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error });
            var result = doc.ToObject<Interpretation>(JsonSerializer.Create(new JsonSerializerSettings {
                MissingMemberHandling=MissingMemberHandling.Error, TypeNameHandling=TypeNameHandling.None, MetadataPropertyHandling=MetadataPropertyHandling.Ignore }));
            if (result == null || string.IsNullOrWhiteSpace(result.message) || result.message.Length > 2000) throw new ArgumentException("Respuesta incompleta.");
            if (result.status == "proposal") { if (result.mission == null) throw new ArgumentException("Falta la misión."); result.mission.Validate(); }
            else if (result.status != "clarification" && result.status != "unsupported") throw new ArgumentException("Estado desconocido.");
            else if (result.mission != null) throw new ArgumentException("Una aclaración o rechazo no puede contener una misión ejecutable.");
            return result;
        }
    }
    [Serializable] public class SearchPhase { public string area; public float minY,maxY; public Route[] routes; }
    [Serializable] public class PhaseEvent { public int phase,tick; public double seconds; public string area; }
    public static class MissionPlanner
    {
        public static Mission Plan(Scenario s, MissionIntent intent)
        {
            intent.Validate();
            var m = new Mission { seed=s.seed,targetCount=s.targetCount,requiredFindings=intent.requiredFindings,width=s.width,height=s.height,
                speed=s.speed,sensorRadius=s.sensorRadius,cellSize=s.cellSize,stepSeconds=s.stepSeconds,maxSeconds=intent.maxSeconds,
                starts=s.starts == null ? null : (Point[])s.starts.Clone(),distribution=s.distribution,clusters=s.clusters,clusterSpread=s.clusterSpread };
            // Validate scenario bounds before allocating route arrays.
            m.routes = new Route[3];
            for (int d=0;d<3;d++) m.routes[d] = new Route { waypoints=new[] { new Point(0,0),new Point(0,0) } };
            m.Validate();
            m.phases = new SearchPhase[intent.areas.Length];
            for (int i=0;i<intent.areas.Length;i++)
            {
                string area = intent.areas[i];
                float low = area == "north" ? s.height*2/3 : area == "center" ? s.height/3 : 0;
                float high = area == "south" ? s.height/3 : area == "center" ? s.height*2/3 : s.height;
                var phase = new SearchPhase { area=area,minY=low,maxY=high,routes=new Route[3] };
                int rows = (int)Math.Ceiling((high-low)/(s.sensorRadius*1.3));
                int columns = (int)Math.Ceiling((s.width/3)/(s.sensorRadius*1.3));
                if ((long)rows*columns > 100000) throw new ArgumentException("Resolución de ruta demasiado alta.");
                for (int d=0;d<3;d++)
                {
                    var points = new List<Point>();
                    // Sample parallel lanes; endpoints stay within radius of sector edges.
                    for (int row=0;row<rows;row++)
                    {
                        float y = high-(row+.5f)*(high-low)/rows;
                        float margin = Math.Min(s.sensorRadius*.5f,s.width/6);
                        float left = d*s.width/3+margin, right = (d+1)*s.width/3-margin;
                        points.Add(new Point(row%2 == 0 ? left : right,y));
                        points.Add(new Point(row%2 == 0 ? right : left,y));
                    }
                    phase.routes[d] = new Route { waypoints=points.ToArray() };
                }
                m.phases[i] = phase;
            }
            m.routes = m.phases[0].routes;
            m.Validate();
            return m;
        }
    }
}
