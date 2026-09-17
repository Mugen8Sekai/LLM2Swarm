using LLM2Swarm.Experiments;

namespace LLM2Swarm.Px4;

// Compatibility copy of the frozen RegionRoute algorithm. Differentially tested.
public static class LocalPlanner
{
    public static Point[] Route(Scenario m,SearchConfig config,int region,Point start)
    {
        if(region<0||region>=config.columns*config.rows)throw new ArgumentException("Invalid region");
        float w=m.width/config.columns,h=m.height/config.rows,x=(region%config.columns)*w,y=(region/config.columns)*h;
        int rows=Math.Max(1,(int)Math.Ceiling(h/(m.sensorRadius*1.3)));
        if(rows>10000)throw new ArgumentException("Route too large");
        Point[] best=null;double cost=double.PositiveInfinity;
        for(int flipY=0;flipY<2;flipY++)for(int flipX=0;flipX<2;flipX++)
        {
            var points=new List<Point>();
            for(int r=0;r<rows;r++)
            {
                float yy=y+(flipY==0?r+.5f:rows-r-.5f)*h/rows;
                float margin=Math.Min(m.sensorRadius*.5f,w/2),left=x+margin,right=x+w-margin;
                bool forward=(r+flipX)%2==0;
                points.Add(new Point(forward?left:right,yy));points.Add(new Point(forward?right:left,yy));
            }
            double distance=Point.Distance(start,points[0]);
            if(distance<cost){best=points.ToArray();cost=distance;}
        }
        return best;
    }
}
