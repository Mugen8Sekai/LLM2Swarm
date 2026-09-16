using System;
using System.Collections.Generic;

namespace LLM2Swarm
{
    [Serializable] public struct Point
    {
        public float x, y;
        public Point(float x, float y) { this.x = x; this.y = y; }
        public static double Distance(Point a, Point b) => Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y));
    }
    [Serializable] public class Route { public Point[] waypoints; }
    [Serializable] public class Mission
    {
        public int seed = 42, targetCount = 12, requiredFindings = 12;
        public float width = 60, height = 40, speed = 4, sensorRadius = 3, cellSize = 1;
        public float stepSeconds = 0.1f, maxSeconds = 600;
        public Route[] routes;
        public SearchPhase[] phases;
        public static Mission Sweep(int seed = 42)
        {
            var m = new Mission { seed = seed, routes = new Route[3] };
            for (int d = 0; d < 3; d++)
            {
                var points = new List<Point>();
                for (int row = 0; row < 10; row++)
                {
                    float left = d * 20 + 2, right = d * 20 + 18, y = 2 + row * 4;
                    points.Add(new Point(row % 2 == 0 ? left : right, y));
                    points.Add(new Point(row % 2 == 0 ? right : left, y));
                }
                m.routes[d] = new Route { waypoints = points.ToArray() };
            }
            return m;
        }
        public void Validate()
        {
            // Unity serializes a null array as []; both mean a legacy mission without phases.
            if (phases != null && phases.Length == 0) phases = null;
            foreach (float v in new[] { width, height, speed, sensorRadius, cellSize, stepSeconds, maxSeconds })
                if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0) throw new ArgumentException("Los parámetros deben ser finitos y positivos.");
            if (targetCount < 1 || targetCount > 10000 || requiredFindings < 1 || requiredFindings > 10000 || Math.Ceiling(width/cellSize)*Math.Ceiling(height/cellSize) > 250000)
                throw new ArgumentException("Número de objetivos o resolución fuera de límites.");
            if (routes == null || routes.Length != 3) throw new ArgumentException("La misión requiere tres rutas.");
            foreach (var route in routes)
            {
                if (route?.waypoints == null || route.waypoints.Length < 2) throw new ArgumentException("Cada ruta requiere al menos dos puntos.");
                foreach (var p in route.waypoints)
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || p.x < 0 || p.x > width || p.y < 0 || p.y > height)
                        throw new ArgumentException("Waypoint fuera de la región.");
            }
            if (phases != null)
            {
                if (phases.Length < 1 || phases.Length > 3) throw new ArgumentException("Número de fases inválido.");
                foreach (var phase in phases)
                {
                    if (phase == null || float.IsNaN(phase.minY) || float.IsNaN(phase.maxY) || phase.minY < 0 || phase.maxY > height || phase.minY >= phase.maxY)
                        throw new ArgumentException("Límites de fase inválidos.");
                    var check = new Mission { width=width,height=height,routes=phase.routes };
                    check.Validate();
                    if (phase.area != "all" && phase.area != "north" && phase.area != "center" && phase.area != "south")
                        throw new ArgumentException("Nombre de fase no soportado.");
                    foreach (var route in phase.routes)
                        foreach (var p in route.waypoints)
                            if (p.y < phase.minY || p.y > phase.maxY) throw new ArgumentException("Ruta fuera de su fase.");
                }
            }
        }
    }
    [Serializable] public class Finding { public int target, drone, tick; public double seconds; public Point position; }
    [Serializable] public class Sample { public int tick, found; public double seconds, coverage; }
    public sealed class Simulation
    {
        public readonly Mission Mission;
        public readonly Point[] Drones = new Point[3];
        readonly Point[] Targets;
        public int TargetCount => Targets.Length;
        public readonly bool[] Covered, Found;
        public readonly List<Finding> Findings = new List<Finding>();
        public readonly List<Sample> Samples = new List<Sample>();
        public readonly List<PhaseEvent> PhaseEvents = new List<PhaseEvent>();
        public int ActivePhase { get; private set; }
        public Route[] ActiveRoutes => Mission.phases == null ? Mission.routes : Mission.phases[ActivePhase].routes;
        public string ActiveArea => Mission.phases == null ? "all" : Mission.phases[ActivePhase].area;
        public readonly int Columns, Rows;
        readonly int[] next = { 1, 1, 1 };
        double coveredArea;
        public int Tick { get; private set; }
        public double Seconds => Tick * (double)Mission.stepSeconds;
        public double Coverage => coveredArea / (Mission.width * (double)Mission.height);
        public string FinishReason { get; private set; }
        public bool Finished => FinishReason != null;
        public Simulation(Mission mission)
        {
            mission.Validate(); Mission = mission;
            Columns = (int)Math.Ceiling(mission.width/mission.cellSize); Rows = (int)Math.Ceiling(mission.height/mission.cellSize);
            Covered = new bool[Columns*Rows]; Targets = new Point[mission.targetCount]; Found = new bool[Targets.Length];
            // Explicit PRNG: scenario generation does not depend on Unity's global random state.
            uint state = unchecked((uint)mission.seed);
            for (int i = 0; i < Targets.Length; i++) Targets[i] = new Point(Random(ref state)*mission.width, Random(ref state)*mission.height);
            PhaseEvents.Add(new PhaseEvent { phase=0,area=ActiveArea,tick=0,seconds=0 });
            for (int d = 0; d < 3; d++) { Drones[d] = ActiveRoutes[d].waypoints[0]; Scan(d, Drones[d], Drones[d]); }
            if (Findings.Count >= Mission.requiredFindings) FinishReason = "success";
            Record();
        }
        static float Random(ref uint state) { state = unchecked(1664525u*state+1013904223u); return (state >> 8)/16777216f; }
        public Point CellCenter(int index)
        {
            float x = index%Columns*Mission.cellSize, y = index/Columns*Mission.cellSize;
            return new Point((x+Math.Min(x+Mission.cellSize,Mission.width))/2, (y+Math.Min(y+Mission.cellSize,Mission.height))/2);
        }
        static double SegmentDistance(Point p, Point a, Point b)
        {
            double dx = b.x-a.x, dy = b.y-a.y, length = dx*dx+dy*dy;
            double t = length == 0 ? 0 : Math.Max(0,Math.Min(1,((p.x-a.x)*dx+(p.y-a.y)*dy)/length));
            return Point.Distance(p,new Point((float)(a.x+t*dx),(float)(a.y+t*dy)));
        }
        void Scan(int drone, Point a, Point b)
        {
            for (int i = 0; i < Covered.Length; i++)
                if (!Covered[i] && InActiveArea(CellCenter(i)) && SegmentDistance(CellCenter(i),a,b) <= Mission.sensorRadius)
                {
                    Covered[i] = true;
                    coveredArea += Math.Min(Mission.cellSize,Mission.width-i%Columns*Mission.cellSize)*
                        (double)Math.Min(Mission.cellSize,Mission.height-i/Columns*Mission.cellSize);
                }
            for (int i = 0; i < Targets.Length; i++)
                if (!Found[i] && InActiveArea(Targets[i]) && SegmentDistance(Targets[i],a,b) <= Mission.sensorRadius)
                { Found[i] = true; Findings.Add(new Finding { target=i, drone=drone, tick=Tick, seconds=Seconds, position=Targets[i] }); }
        }
        bool InActiveArea(Point p) => Mission.phases == null || (p.y >= Mission.phases[ActivePhase].minY &&
            (p.y < Mission.phases[ActivePhase].maxY || Mission.phases[ActivePhase].maxY == Mission.height));
        public void Step()
        {
            if (Finished) return;
            Tick++;
            bool complete = true;
            for (int d = 0; d < 3; d++)
            {
                double budget = Mission.speed*(double)Mission.stepSeconds;
                var route = ActiveRoutes[d].waypoints;
                while (next[d] < route.Length && budget > 0)
                {
                    Point a = Drones[d], goal = route[next[d]];
                    double distance = Point.Distance(a,goal);
                    if (distance <= budget) { Drones[d] = goal; budget -= distance; next[d]++; }
                    else { Drones[d] = new Point((float)(a.x+(goal.x-a.x)*budget/distance),(float)(a.y+(goal.y-a.y)*budget/distance)); budget = 0; }
                    Scan(d,a,Drones[d]);
                }
                complete &= next[d] == route.Length;
            }
            if (Findings.Count >= Mission.requiredFindings) FinishReason = "success";
            else if (Seconds >= Mission.maxSeconds) FinishReason = "time_limit";
            else if (complete)
            {
                if (Mission.phases != null && ActivePhase+1 < Mission.phases.Length)
                {
                    ActivePhase++;
                    for (int d=0;d<3;d++) next[d]=0;
                    PhaseEvents.Add(new PhaseEvent { phase=ActivePhase,area=ActiveArea,tick=Tick,seconds=Seconds });
                }
                else FinishReason = "search_exhausted";
            }
            Record();
        }
        void Record() => Samples.Add(new Sample { tick=Tick, seconds=Seconds, coverage=Coverage, found=Findings.Count });
        public void Interrupt() { if (!Finished) FinishReason = "interrupted"; }
    }
}
