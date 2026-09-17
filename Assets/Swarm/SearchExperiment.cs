using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace LLM2Swarm.Experiments
{
    [Serializable] public sealed class SearchConfig
    {
        public int columns=6, rows=4;
        public double decisionSeconds=1;
        public void Validate(float tick)
        {
            if(columns<1||rows<1||columns* (long)rows>256||double.IsNaN(decisionSeconds)||double.IsInfinity(decisionSeconds)||decisionSeconds<=0||decisionSeconds>60)
                throw new ArgumentException("Discretización o intervalo inválido.");
            double ticks=decisionSeconds/tick;
            if(ticks<1||Math.Abs(ticks-Math.Round(ticks))>1e-4) throw new ArgumentException("El intervalo debe ser múltiplo del paso físico.");
        }
    }
    // The only information a strategy receives. Every array is a detached snapshot.
    // Deliberately excludes scenario seed, distribution, hidden targets and total target count.
    [Serializable] public sealed class SearchObservation
    {
        public float width,height,speed,sensorRadius;
        public int columns,rows,requiredFindings,found;
        public double seconds,remainingSeconds;
        public Point[] positions;
        public bool[] idle,completed;
        public int[] assignments,regionFindings;
        public double[] remainingDistance,regionCoverage;
    }
    public interface ISearchStrategy
    {
        string Id { get; }
        void Reset(SearchObservation observation);
        int[] Decide(SearchObservation observation); // -1 = continue/wait
    }
    [Serializable] public sealed class SearchMetrics
    {
        public string finishReason;
        public bool success;
        public double seconds,coverage,foundFraction,totalDistance,exposureAreaSeconds,redundantAreaSeconds;
        public double? firstFindingSeconds,kFindingSeconds;
        public int found;
        public double[] distanceByDrone,newAreaByDrone;
        public int[] findingsByDrone;
    }
    public sealed class SearchEnvironment
    {
        readonly Simulation simulation;
        readonly SearchConfig config;
        readonly bool[] completed;
        readonly int[] assignments={-1,-1,-1},cellRegions;
        readonly double[] regionAreas;
        public bool Finished => simulation.Finished;
        public SearchEnvironment(Scenario scenario,MissionIntent intent,SearchConfig configuration=null)
        {
            intent.Validate();
            if(intent.areas.Length!=1||intent.areas[0]!="all") throw new ArgumentException("El experimento inicial admite solo la región completa.");
            // Copy all caller-owned state, including nested starts, before constructing the world.
            var copy=JsonConvert.DeserializeObject<Scenario>(JsonConvert.SerializeObject(scenario));
            config=JsonConvert.DeserializeObject<SearchConfig>(JsonConvert.SerializeObject(configuration??new SearchConfig()));
            config.Validate(copy.stepSeconds);
            var mission=new Mission { seed=copy.seed,targetCount=copy.targetCount,width=copy.width,height=copy.height,speed=copy.speed,
                sensorRadius=copy.sensorRadius,cellSize=copy.cellSize,stepSeconds=copy.stepSeconds,starts=copy.starts,
                distribution=copy.distribution,clusters=copy.clusters,clusterSpread=copy.clusterSpread,
                requiredFindings=intent.requiredFindings,maxSeconds=intent.maxSeconds,routes=new Route[3] };
            for(int d=0;d<3;d++) mission.routes[d]=new Route { waypoints=new[] {new Point(0,0),new Point(0,0)} };
            simulation=new Simulation(mission,true,false);
            completed=new bool[config.columns*config.rows];
            regionAreas=new double[completed.Length]; cellRegions=new int[simulation.Covered.Length];
            for(int i=0;i<cellRegions.Length;i++) { cellRegions[i]=Region(simulation.CellCenter(i)); regionAreas[cellRegions[i]]+=CellArea(i); }
        }
        double CellArea(int i) => Math.Min(simulation.Mission.cellSize,simulation.Mission.width-i%simulation.Columns*simulation.Mission.cellSize)*
            (double)Math.Min(simulation.Mission.cellSize,simulation.Mission.height-i/simulation.Columns*simulation.Mission.cellSize);
        int Region(Point p) => Math.Min(config.rows-1,(int)(p.y/simulation.Mission.height*config.rows))*config.columns+
            Math.Min(config.columns-1,(int)(p.x/simulation.Mission.width*config.columns));
        public SearchObservation Observe()
        {
            var m=simulation.Mission;
            var o=new SearchObservation { width=m.width,height=m.height,speed=m.speed,sensorRadius=m.sensorRadius,columns=config.columns,rows=config.rows,
                requiredFindings=m.requiredFindings,found=simulation.Findings.Count,seconds=simulation.Seconds,remainingSeconds=Math.Max(0,m.maxSeconds-simulation.Seconds),
                positions=(Point[])simulation.Drones.Clone(),completed=(bool[])completed.Clone(),assignments=(int[])assignments.Clone(),
                idle=new bool[3],remainingDistance=new double[3],regionCoverage=new double[completed.Length],regionFindings=new int[completed.Length] };
            for(int d=0;d<3;d++) { o.idle[d]=simulation.Idle(d); o.remainingDistance[d]=simulation.RemainingDistance(d); }
            for(int i=0;i<cellRegions.Length;i++) if(simulation.Covered[i]) o.regionCoverage[cellRegions[i]]+=CellArea(i);
            for(int i=0;i<regionAreas.Length;i++) o.regionCoverage[i]=regionAreas[i]>0 ? o.regionCoverage[i]/regionAreas[i] : 0;
            foreach(var f in simulation.Findings) o.regionFindings[Region(f.position)]++;
            return o;
        }
        public void Step(int[] action)
        {
            if(Finished) return;
            if(action==null||action.Length!=3) throw new ArgumentException("La acción requiere tres asignaciones.");
            var reserved=new HashSet<int>(assignments.Where(x=>x>=0));
            var routes=new Point[3][];
            // Validate the whole joint action before changing state.
            for(int d=0;d<3;d++)
            {
                int region=action[d];
                if(region == -1) continue;
                if(!simulation.Idle(d)||region<0||region>=completed.Length||completed[region]||!reserved.Add(region)) throw new ArgumentException("Asignación no disponible.");
                routes[d]=RegionRoute(region,simulation.Drones[d]);
            }
            for(int d=0;d<3;d++) if(routes[d]!=null) { simulation.Assign(d,routes[d]); assignments[d]=action[d]; }
            int ticks=(int)Math.Round(config.decisionSeconds/simulation.Mission.stepSeconds);
            for(int i=0;i<ticks&&!Finished;i++) simulation.Step();
            for(int d=0;d<3;d++) if(assignments[d]>=0&&simulation.Idle(d)) { completed[assignments[d]]=true; assignments[d]=-1; }
            if(completed.All(x=>x)) simulation.Exhaust();
        }
        Point[] RegionRoute(int region,Point start)
        {
            var m=simulation.Mission;
            float w=m.width/config.columns,h=m.height/config.rows,x=(region%config.columns)*w,y=(region/config.columns)*h;
            int rows=Math.Max(1,(int)Math.Ceiling(h/(m.sensorRadius*1.3)));
            if(rows>10000) throw new ArgumentException("Ruta local demasiado grande.");
            Point[] best=null; double cost=double.PositiveInfinity;
            for(int flipY=0;flipY<2;flipY++) for(int flipX=0;flipX<2;flipX++)
            {
                var points=new List<Point>();
                for(int r=0;r<rows;r++)
                {
                    float yy=y+(flipY==0 ? r+.5f : rows-r-.5f)*h/rows;
                    float margin=Math.Min(m.sensorRadius*.5f,w/2),left=x+margin,right=x+w-margin;
                    bool forward=(r+flipX)%2==0;
                    points.Add(new Point(forward?left:right,yy)); points.Add(new Point(forward?right:left,yy));
                }
                double distance=Point.Distance(start,points[0]);
                if(distance<cost) { best=points.ToArray();cost=distance; }
            }
            return best;
        }
        // Evaluation channel, never passed to ISearchStrategy or emitted by the adapter before done.
        public SearchMetrics Metrics()
        {
            var m=new SearchMetrics { finishReason=simulation.FinishReason,success=simulation.FinishReason=="success",seconds=simulation.Seconds,
                coverage=simulation.Coverage,found=simulation.Findings.Count,foundFraction=(double)simulation.Findings.Count/simulation.TargetCount,
                totalDistance=simulation.DistanceByDrone.Sum(),distanceByDrone=(double[])simulation.DistanceByDrone.Clone(),
                newAreaByDrone=(double[])simulation.NewAreaByDrone.Clone(),findingsByDrone=new int[3],exposureAreaSeconds=simulation.ExposureAreaSeconds,
                redundantAreaSeconds=simulation.RedundantAreaSeconds };
            if(simulation.Findings.Count>0) m.firstFindingSeconds=simulation.Findings[0].seconds;
            if(simulation.Findings.Count>=simulation.Mission.requiredFindings) m.kFindingSeconds=simulation.Findings[simulation.Mission.requiredFindings-1].seconds;
            foreach(var f in simulation.Findings) m.findingsByDrone[f.drone]++;
            return m;
        }
    }
    public sealed class SystematicStrategy : ISearchStrategy
    {
        readonly bool compact;
        List<int>[] queues;
        public string Id => compact ? "B-coordinated" : "A-systematic";
        public SystematicStrategy(bool compactPartition) { compact=compactPartition; }
        public void Reset(SearchObservation o)
        {
            var groups=new[] {new List<int>(),new List<int>(),new List<int>()};
            if(!compact)
            {
                int k=0,total=o.columns*o.rows;
                for(int y=0;y<o.rows;y++) for(int x=0;x<o.columns;x++)
                { int region=y*o.columns+(y%2==0?x:o.columns-1-x); groups[Math.Min(2,k++*3/total)].Add(region); }
            }
            else
            {
                // Compact strips along the longest grid dimension, with serpentine order inside each strip.
                bool vertical=o.width>=o.height;
                if(vertical)
                {
                    for(int y=0;y<o.rows;y++) for(int x=0;x<o.columns;x++)
                    { int xx=y%2==0?x:o.columns-1-x; groups[Math.Min(2,xx*3/o.columns)].Add(y*o.columns+xx); }
                }
                else for(int y=0;y<o.rows;y++) for(int x=0;x<o.columns;x++)
                    groups[Math.Min(2,y*3/o.rows)].Add(y*o.columns+(y%2==0?x:o.columns-1-x));
            }
            int[][] permutations={new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}};
            double best=double.PositiveInfinity; int[] selected=permutations[0];
            foreach(var p in permutations)
            {
                double cost=0;
                for(int d=0;d<3;d++) if(groups[p[d]].Count>0) cost+=Math.Min(Point.Distance(o.positions[d],Center(groups[p[d]][0],o)),Point.Distance(o.positions[d],Center(groups[p[d]][groups[p[d]].Count-1],o)));
                if(cost<best) {best=cost;selected=p;}
            }
            queues=new List<int>[3];
            for(int d=0;d<3;d++)
            {
                queues[d]=new List<int>(groups[selected[d]]);
                if(queues[d].Count>0&&Point.Distance(o.positions[d],Center(queues[d][0],o))>Point.Distance(o.positions[d],Center(queues[d][queues[d].Count-1],o))) queues[d].Reverse();
            }
        }
        static Point Center(int i,SearchObservation o) => new Point((i%o.columns+.5f)*o.width/o.columns,(i/o.columns+.5f)*o.height/o.rows);
        public int[] Decide(SearchObservation o)
        {
            var result=new[]{-1,-1,-1};
            for(int d=0;d<3;d++) if(o.idle[d]) foreach(int region in queues[d])
                if(!o.completed[region]&&!o.assignments.Contains(region)) { result[d]=region;break; }
            return result;
        }
    }
}
