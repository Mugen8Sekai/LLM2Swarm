using LLM2Swarm;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Sockets;

namespace LLM2Swarm.Px4;

public sealed class NedOrigin
{
    public double north,east,down,x,y,height;
    public double[] ToNed(double px,double py,double h) => new[]{north+py-y,east+px-x,down-h+height};
    public Point ToMap(double n,double e) => new Point((float)(x+e-east),(float)(y+n-north));
}
public sealed class VehicleState
{
    public int id,sysId,waypoint;
    public double north,east,down,vn,ve,vd,bootMs,receivedSim,ageWall;
    public bool armed,healthy,connected;
    public string mode,taskId,taskState,error;
    public NedOrigin origin;
}
public sealed class BridgeFrame
{
    public string runId,error;
    public double simTime,clockAge;
    public VehicleState[] vehicles;
}
public sealed class WaypointTask
{
    public string runId,taskId,frameId="map-enu-v1";
    public int missionRevision=1,vehicleId,regionId,sequence;
    public Point[] points;
    public double[][] ned;
}
public interface IExecutionBackend : IAsyncDisposable
{
    Task<BridgeFrame> Start();
    Task<BridgeFrame> Poll();
    Task Submit(WaypointTask[] tasks);
    Task Land();
}
public sealed class BridgeBackend : IExecutionBackend
{
    readonly TcpClient client=new();
    StreamReader reader;
    StreamWriter writer;
    readonly string token;
    public BridgeBackend(string token) { this.token=token; }
    public async Task Connect(int port)
    {
        await client.ConnectAsync("127.0.0.1",port).WaitAsync(TimeSpan.FromSeconds(10));
        reader=new StreamReader(client.GetStream());writer=new StreamWriter(client.GetStream()){AutoFlush=true};
    }
    async Task<JToken> Request(object request)
    {
        var doc=JObject.FromObject(request);doc["token"]=token;
        await writer.WriteLineAsync(doc.ToString(Formatting.None));
        string line=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(150));
        if(line==null)throw new IOException("Bridge disconnected");
        var result=JObject.Parse(line);
        if((bool?)result["ok"]!=true)throw new IOException((string)result["error"]??"Bridge failure");
        return result["result"];
    }
    public async Task<BridgeFrame> Start() => (await Request(new{op="start"})).ToObject<BridgeFrame>();
    public async Task<BridgeFrame> Poll() => (await Request(new{op="poll"})).ToObject<BridgeFrame>();
    public async Task Submit(WaypointTask[] tasks) { await Request(new{op="tasks",tasks}); }
    public async Task Land() { await Request(new{op="land"}); }
    public ValueTask DisposeAsync() { writer?.Dispose();reader?.Dispose();client.Dispose();return ValueTask.CompletedTask; }
}
