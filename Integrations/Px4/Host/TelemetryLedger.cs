using LLM2Swarm.Experiments;

namespace LLM2Swarm.Px4;

// Integration-only accounting from measured XY segments. No ideal motion is integrated.
// The slice is uniform only; target generation and sensing match the frozen uniform case.
public sealed class TelemetryLedger
{
    readonly Scenario s;
    readonly MissionIntent intent;
    readonly Point[] targets;
    readonly bool[] detected;
    readonly int columns,rows;
    public readonly bool[] covered;
    public readonly List<Finding> findings=new();
    readonly double[] distance=new double[3],newArea=new double[3];
    Point[] previous;
    double seconds,area,exposure,redundancy;
    int tick;
    public TelemetryLedger(Scenario scenario,MissionIntent mission)
    {
        s=scenario;intent=mission;
        if(s.distribution!="uniform")throw new ArgumentException("Slice supports its fixed uniform scenario only");
        columns=(int)Math.Ceiling(s.width/s.cellSize);rows=(int)Math.Ceiling(s.height/s.cellSize);
        covered=new bool[columns*rows];targets=new Point[s.targetCount];detected=new bool[s.targetCount];
        uint state=Mix(unchecked((uint)s.seed));
        for(int i=0;i<targets.Length;i++)targets[i]=new Point(Random(ref state)*s.width,Random(ref state)*s.height);
    }
    static uint Mix(uint x){unchecked{x^=x>>16;x*=0x85ebca6bu;x^=x>>13;x*=0xc2b2ae35u;x^=x>>16;return x;}}
    static float Random(ref uint state){state=unchecked(1664525u*state+1013904223u);return(state>>8)/16777216f;}
    public Point Center(int i)
    {
        float x=i%columns*s.cellSize,y=i/columns*s.cellSize;
        return new Point((x+Math.Min(x+s.cellSize,s.width))/2,(y+Math.Min(y+s.cellSize,s.height))/2);
    }
    double CellArea(int i)=>Math.Min(s.cellSize,s.width-i%columns*s.cellSize)*(double)Math.Min(s.cellSize,s.height-i/columns*s.cellSize);
    static double Segment(Point p,Point a,Point b)
    {
        double dx=b.x-a.x,dy=b.y-a.y,length=dx*dx+dy*dy;
        double t=length==0?0:Math.Clamp(((p.x-a.x)*dx+(p.y-a.y)*dy)/length,0,1);
        return Point.Distance(p,new Point((float)(a.x+t*dx),(float)(a.y+t*dy)));
    }
    public void Update(Point[] points,double time)
    {
        if(!double.IsFinite(time)||time<0)throw new ArgumentException("Invalid timestamp");
        if(points.Length!=3||points.Any(p=>!float.IsFinite(p.x)||!float.IsFinite(p.y)))throw new ArgumentException("Invalid positions");
        if(previous!=null&&time<=seconds)throw new ArgumentException("Duplicate or out-of-order telemetry");
        double dt=previous==null?0:time-seconds;
        if(dt>1)throw new InvalidOperationException("Telemetry gap; do not invent coverage");
        seconds=time;
        for(int d=0;d<3;d++)
        {
            Point a=previous==null?points[d]:previous[d],b=points[d];
            distance[d]+=Point.Distance(a,b);
            for(int i=0;i<covered.Length;i++)if(Segment(Center(i),a,b)<=s.sensorRadius)
            {
                double cell=CellArea(i);exposure+=cell*dt;
                if(covered[i])redundancy+=cell*dt;
                else{covered[i]=true;area+=cell;newArea[d]+=cell;}
            }
            for(int i=0;i<targets.Length;i++)if(!detected[i]&&Segment(targets[i],a,b)<=s.sensorRadius)
            {detected[i]=true;findings.Add(new Finding{target=i,drone=d,tick=tick,seconds=time,position=targets[i]});}
        }
        previous=(Point[])points.Clone();tick++;
    }
    int Region(Point p,SearchConfig c)=>Math.Clamp((int)(p.y/s.height*c.rows),0,c.rows-1)*c.columns+Math.Clamp((int)(p.x/s.width*c.columns),0,c.columns-1);
    public (double[],int[]) Regional(SearchConfig c)
    {
        int n=c.columns*c.rows;var total=new double[n];var known=new double[n];var counts=new int[n];
        for(int i=0;i<covered.Length;i++){int r=Region(Center(i),c);total[r]+=CellArea(i);if(covered[i])known[r]+=CellArea(i);}
        for(int r=0;r<n;r++)known[r]=total[r]>0?known[r]/total[r]:0;
        foreach(var f in findings)counts[Region(f.position,c)]++;
        return(known,counts);
    }
    public SearchMetrics Metrics(string reason)
    {
        var m=new SearchMetrics{finishReason=reason,success=reason=="success",seconds=seconds,coverage=area/(s.width*(double)s.height),found=findings.Count,
            foundFraction=findings.Count/(double)s.targetCount,totalDistance=distance.Sum(),distanceByDrone=(double[])distance.Clone(),newAreaByDrone=(double[])newArea.Clone(),
            findingsByDrone=new int[3],exposureAreaSeconds=exposure,redundantAreaSeconds=redundancy};
        if(findings.Count>0)m.firstFindingSeconds=findings[0].seconds;
        if(findings.Count>=intent.requiredFindings)m.kFindingSeconds=findings[intent.requiredFindings-1].seconds;
        foreach(var f in findings)m.findingsByDrone[f.drone]++;
        return m;
    }
}
