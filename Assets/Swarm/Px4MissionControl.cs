using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LLM2Swarm
{
    // Read-only projection of MissionHost state. This component has no backend command channel.
    public sealed class Px4MissionControl : MonoBehaviour
    {
        [Serializable] sealed class ScenarioView { public float width,height,cellSize,sensorRadius; public int targetCount; }
        [Serializable] sealed class IntentView { public int requiredFindings; public double maxSeconds; }
        [Serializable] sealed class MetricsView { public double coverage; public int found; public int[] findingsByDrone; }
        [Serializable] sealed class VehicleView
        {
            public int id,sysId,waypoint; public bool armed,healthy,connected;
            public string mode,taskId,taskState,error;
        }
        [Serializable] sealed class FindingView { public int target,drone; public double seconds; public Point position; }
        [Serializable] sealed class RouteView { public int vehicleId,regionId; public string taskId; public Point[] points; }
        [Serializable] sealed class Snapshot
        {
            public string type,runId; public int schemaVersion,sequence,missionRevision,completedRegions,totalRegions;
            public double seconds; public ScenarioView scenario; public IntentView intent; public Point[] positions;
            public VehicleView[] vehicles; public MetricsView metrics; public FindingView[] findings; public bool[] covered;
            public RouteView[] routes; public int[] assignments; public bool[] completed;
        }
        sealed class ReadResult
        {
            public string directory,json,error; public byte[][] jpeg=new byte[3][]; public long[] cameraTicks=new long[3];
        }

        readonly Color[] colors={new Color(.2f,.8f,1),new Color(1,.7f,.2f),new Color(.9f,.4f,.8f)};
        readonly object pendingLock=new object();
        readonly Texture2D[] cameras=new Texture2D[3];
        readonly long[] loadedCameraTicks=new long[3];
        Snapshot snapshot; ReadResult pending; Task readTask; double nextRead; string runtimeRoot,status="Esperando MissionHost…",sourceDirectory;
        GUIStyle titleStyle,sectionStyle,textStyle,smallStyle;

        void Awake()
        {
            runtimeRoot=Environment.GetEnvironmentVariable("LLM2SWARM_PX4_RUNTIME");
            if(string.IsNullOrWhiteSpace(runtimeRoot))
                runtimeRoot=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Integrations","Px4",".runtime");
            if(int.TryParse(Environment.GetEnvironmentVariable("LLM2SWARM_MC_TARGET_FPS"),out int targetFps)&&targetFps>0)
                Application.targetFrameRate=targetFps;
            for(int i=0;i<3;i++) cameras[i]=new Texture2D(2,2,TextureFormat.RGB24,false);
        }

        void Update()
        {
            if(Time.realtimeSinceStartupAsDouble>=nextRead&&readTask==null)
            {
                nextRead=Time.realtimeSinceStartupAsDouble+.2;
                string root=runtimeRoot; long[] ticks=(long[])loadedCameraTicks.Clone();
                readTask=Task.Run(()=>ReadLatest(root,ticks)).ContinueWith(task=>
                {
                    lock(pendingLock) pending=task.IsCompletedSuccessfully?task.Result:new ReadResult{error=task.Exception?.GetBaseException().Message};
                });
            }
            if(readTask!=null&&readTask.IsCompleted) readTask=null;
            ReadResult result=null; lock(pendingLock){if(pending!=null){result=pending;pending=null;}}
            if(result!=null) Apply(result);
        }

        static ReadResult ReadLatest(string root,long[] knownTicks)
        {
            var result=new ReadResult();
            try
            {
                string directory=Directory.Exists(root)?Directory.GetDirectories(root,"slice-*")
                    .Where(d=>File.Exists(Path.Combine(d,"snapshot.json"))).OrderByDescending(d=>File.GetLastWriteTimeUtc(Path.Combine(d,"snapshot.json"))).FirstOrDefault():null;
                if(directory==null){result.error="No hay una ejecución PX4 con snapshots";return result;}
                result.directory=directory;
                result.json=ReadShared(Path.Combine(directory,"snapshot.json"));
                for(int i=0;i<3;i++)
                {
                    string path=Path.Combine(directory,$"camera-{i}.jpg");
                    if(!File.Exists(path)) continue;
                    long tick=File.GetLastWriteTimeUtc(path).Ticks; result.cameraTicks[i]=tick;
                    if(tick!=knownTicks[i]) result.jpeg[i]=ReadSharedBytes(path);
                }
            }
            catch(Exception e){result.error=e.Message;}
            return result;
        }

        static string ReadShared(string path)
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var reader=new StreamReader(stream);return reader.ReadToEnd();
        }
        static byte[] ReadSharedBytes(string path)
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            byte[] bytes=new byte[stream.Length];int offset=0,read;
            while(offset<bytes.Length&&(read=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=read;
            if(offset!=bytes.Length)Array.Resize(ref bytes,offset);return bytes;
        }

        void Apply(ReadResult result)
        {
            if(!string.IsNullOrEmpty(result.error)){status=result.error;return;}
            try
            {
                var next=JsonConvert.DeserializeObject<Snapshot>(result.json);
                if(next==null||next.type!="mission-snapshot"||next.schemaVersion!=1||next.positions?.Length!=3||next.vehicles?.Length!=3)
                    throw new InvalidDataException("MissionSnapshot incompatible o incompleto");
                if(snapshot==null||next.runId!=snapshot.runId||next.sequence>=snapshot.sequence) snapshot=next;
                sourceDirectory=result.directory;
                for(int i=0;i<3;i++) if(result.jpeg[i]!=null&&result.jpeg[i].Length>0)
                {
                    if(cameras[i].LoadImage(result.jpeg[i],false)) loadedCameraTicks[i]=result.cameraTicks[i];
                }
                status="Solo lectura · MissionHost/PX4/Gazebo son la autoridad";
            }
            catch(Exception e){status=e.Message;}
        }

        void OnDestroy(){foreach(var texture in cameras) if(texture!=null) Destroy(texture);}
        static void Box(Rect rect,Color color){GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=Color.white;}
        void EnsureStyles()
        {
            if(titleStyle!=null)return;
            titleStyle=new GUIStyle(GUI.skin.label){fontSize=24,fontStyle=FontStyle.Bold};
            sectionStyle=new GUIStyle(GUI.skin.label){fontSize=16,fontStyle=FontStyle.Bold};
            textStyle=new GUIStyle(GUI.skin.label){fontSize=15,wordWrap=true};
            smallStyle=new GUIStyle(textStyle){fontSize=12};
        }

        void OnGUI()
        {
            EnsureStyles(); float scale=Mathf.Min(1,Mathf.Min(Screen.width/1200f,Screen.height/720f));
            Matrix4x4 old=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            float width=Screen.width/scale,height=Screen.height/scale;
            Box(new Rect(0,0,width,height),new Color(.035f,.05f,.08f));
            GUI.Label(new Rect(22,14,width-44,34),"LLM2Swarm  /  Mission Control PX4",titleStyle);
            GUI.Label(new Rect(22,50,width-44,22),status,smallStyle);
            if(snapshot==null){GUI.Label(new Rect(22,100,width-44,40),"Esperando el primer MissionSnapshot…",sectionStyle);GUI.matrix=old;return;}
            float right=Mathf.Clamp(width*.34f,390,520); var mapPanel=new Rect(20,82,width-right-52,height-104);
            var cameraPanel=new Rect(mapPanel.xMax+12,82,right,height-104);
            Box(mapPanel,new Color(.065f,.085f,.12f));Box(cameraPanel,new Color(.065f,.085f,.12f));
            GUI.Label(new Rect(mapPanel.x+14,mapPanel.y+10,mapPanel.width-28,24),"MINIMAPA · TELEMETRÍA REAL",sectionStyle);
            GUI.Label(new Rect(mapPanel.x+14,mapPanel.y+36,mapPanel.width-28,42),
                $"t {snapshot.seconds:F1}/{snapshot.intent.maxSeconds:F0} s   ·   cobertura {snapshot.metrics.coverage:P1}   ·   hallazgos {snapshot.metrics.found}/{snapshot.intent.requiredFindings}   ·   regiones {snapshot.completedRegions}/{snapshot.totalRegions}",textStyle);
            DrawMap(new Rect(mapPanel.x+18,mapPanel.y+84,mapPanel.width-36,mapPanel.height-154));
            float vehicleWidth=(mapPanel.width-36)/3;
            for(int i=0;i<3;i++)
            {
                var v=snapshot.vehicles[i];int found=snapshot.findings?.Count(f=>f.drone==i)??0;
                GUI.Label(new Rect(mapPanel.x+14+i*vehicleWidth,mapPanel.yMax-62,vehicleWidth-4,52),
                    $"UAV {i+1} · {v.mode}\n({snapshot.positions[i].x:F1}, {snapshot.positions[i].y:F1}) m · {found} hallazgos",smallStyle);
            }
            GUI.Label(new Rect(cameraPanel.x+14,cameraPanel.y+10,cameraPanel.width-28,24),"CÁMARAS GAZEBO · VISUALIZACIÓN",sectionStyle);
            float cameraHeight=(cameraPanel.height-58)/3;
            for(int i=0;i<3;i++) DrawCamera(new Rect(cameraPanel.x+12,cameraPanel.y+42+i*cameraHeight,cameraPanel.width-24,cameraHeight-8),i);
            GUI.matrix=old;
        }

        void DrawCamera(Rect rect,int index)
        {
            Box(rect,new Color(.015f,.02f,.03f));
            if(loadedCameraTicks[index]==0) GUI.Label(new Rect(rect.x+12,rect.y+34,rect.width-24,28),$"UAV {index+1} · sin frame",textStyle);
            else
            {
                float aspect=cameras[index].width/(float)cameras[index].height;Rect image=rect;
                if(rect.width/rect.height>aspect){image.width=rect.height*aspect;image.x=rect.center.x-image.width/2;}
                else{image.height=rect.width/aspect;image.y=rect.center.y-image.height/2;}
                GUI.DrawTexture(image,cameras[index],ScaleMode.ScaleToFit,false);
                GUI.Label(new Rect(rect.x+8,rect.y+6,120,22),$"UAV {index+1}",smallStyle);
            }
        }

        void DrawMap(Rect viewport)
        {
            float mapWidth=snapshot.scenario.width,mapHeight=snapshot.scenario.height;
            float scale=Mathf.Min(viewport.width/mapWidth,viewport.height/mapHeight);
            var area=new Rect(viewport.center.x-mapWidth*scale/2,viewport.center.y-mapHeight*scale/2,mapWidth*scale,mapHeight*scale);
            Box(area,new Color(.02f,.025f,.04f));
            int columns=Mathf.CeilToInt(mapWidth/snapshot.scenario.cellSize);
            for(int i=0;i<(snapshot.covered?.Length??0);i++) if(snapshot.covered[i])
            {
                float x=i%columns*snapshot.scenario.cellSize,y=i/columns*snapshot.scenario.cellSize;
                Box(new Rect(area.x+x*scale,area.yMax-(y+snapshot.scenario.cellSize)*scale,snapshot.scenario.cellSize*scale,snapshot.scenario.cellSize*scale),new Color(.08f,.31f,.18f));
            }
            foreach(var route in snapshot.routes??Array.Empty<RouteView>())
            {
                var points=route.points??Array.Empty<Point>();Color color=colors[Mathf.Clamp(route.vehicleId,0,2)]*.55f;
                for(int i=1;i<points.Length;i++)DrawLine(area,points[i-1],points[i],scale,color);
                foreach(var point in points)DrawPoint(area,point,scale,3,color);
            }
            foreach(var finding in snapshot.findings??Array.Empty<FindingView>())DrawPoint(area,finding.position,scale,7,Color.white);
            for(int i=0;i<3;i++)DrawPoint(area,snapshot.positions[i],scale,11,colors[i]);
        }
        static void DrawPoint(Rect area,Point p,float scale,float size,Color color)=>Box(new Rect(area.x+p.x*scale-size/2,area.yMax-p.y*scale-size/2,size,size),color);
        static void DrawLine(Rect area,Point a,Point b,float scale,Color color)
        {
            Vector2 start=new Vector2(area.x+a.x*scale,area.yMax-a.y*scale),end=new Vector2(area.x+b.x*scale,area.yMax-b.y*scale),delta=end-start;
            Matrix4x4 old=GUI.matrix;GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,start);Box(new Rect(start.x,start.y,delta.magnitude,2),color);GUI.matrix=old;
        }
    }
}
