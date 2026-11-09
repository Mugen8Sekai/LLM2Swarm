using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using LLM2Swarm.Experiments;

namespace LLM2Swarm
{
    // Inference-only copy of the verified exported actor. It never reads hidden world state.
    public sealed class PpoSeed47Strategy : ISearchStrategy
    {
        readonly int regions,columns,rows; readonly double[][] first,second,actor; readonly double[] firstBias,secondBias,actorBias;
        public string Id => "PPO-centralized-pilot-full-47";
        public PpoSeed47Strategy(string json)
        {
            var doc=JObject.Parse(json);regions=(int)doc["regions"];columns=(int)doc["columns"];rows=(int)doc["rows"];var w=doc["weights"];
            first=w["body.0.weight"].ToObject<double[][]>();firstBias=w["body.0.bias"].ToObject<double[]>();second=w["body.2.weight"].ToObject<double[][]>();secondBias=w["body.2.bias"].ToObject<double[]>();actor=w["actor.weight"].ToObject<double[][]>();actorBias=w["actor.bias"].ToObject<double[]>();
        }
        public void Reset(SearchObservation observation){if(observation.columns!=columns||observation.rows!=rows)throw new ArgumentException("Checkpoint grid mismatch.");}
        static double[] Layer(double[] x,double[][] weights,double[] bias,bool tanh){var y=new double[bias.Length];for(int i=0;i<y.Length;i++){double v=bias[i];for(int j=0;j<x.Length;j++)v+=weights[i][j]*x[j];y[i]=tanh?Math.Tanh(v):v;}return y;}
        double[] Logits(SearchObservation o)
        {
            Reset(o);double deadline=o.seconds+o.remainingSeconds,diagonal=Math.Sqrt(o.width*(double)o.width+o.height*(double)o.height);
            var x=new List<double>{o.width/60d,o.height/40d,o.speed/4d,o.sensorRadius/3d,o.columns/6d,o.rows/4d,o.requiredFindings/24d,o.found/(double)o.requiredFindings,deadline/120d,o.seconds/deadline,o.remainingSeconds/deadline};
            for(int d=0;d<3;d++){x.Add(o.positions[d].x/o.width);x.Add(o.positions[d].y/o.height);x.Add(o.idle[d]?1:0);x.Add(o.remainingDistance[d]/diagonal);for(int r=0;r<regions;r++)x.Add(o.assignments[d]==r?1:0);}
            for(int r=0;r<regions;r++){x.Add(o.completed[r]?1:0);x.Add(o.regionCoverage[r]);x.Add(o.regionFindings[r]/(double)o.requiredFindings);}
            return Layer(Layer(Layer(x.ToArray(),first,firstBias,true),second,secondBias,true),actor,actorBias,false);
        }
        public int[] Decide(SearchObservation o)
        {
            var logits=Logits(o);var reserved=new HashSet<int>(o.assignments);var actions=new[]{-1,-1,-1};
            for(int d=0;d<3;d++)if(o.idle[d]){int best=-1;double value=double.NegativeInfinity;for(int r=0;r<=regions;r++){if(r<regions&&(o.completed[r]||reserved.Contains(r)))continue;if(logits[d*(regions+1)+r]>value){value=logits[d*(regions+1)+r];best=r;}}if(best<regions){actions[d]=best;reserved.Add(best);}}
            return actions;
        }
    }
}
