"""Differential checks: exported ISearchStrategy vs PyTorch and frozen baselines."""
import json
import subprocess
import sys
import numpy as np
import torch
from run import ROOT, OUTPUT, DOTNET, frozen
from policy import Policy, PpoStrategy, encode

torch.set_num_threads(1)
seed=int(sys.argv[1]) if len(sys.argv)>1 else 11
directory=OUTPUT/'full'/str(seed)
rows=[json.loads(line) for line in (directory/'paired.jsonl').read_text().splitlines()]
traces=[r for r in rows if r['trace']]
model=Policy(len(encode(traces[0]['trace'][0]['observation'])),24)
model.load_state_dict(torch.load(directory/'selected.pt',weights_only=True))
strategy=PpoStrategy(model)
host=ROOT/'Experiments/ppo/PolicyHost/bin/Release/net8.0/PolicyHost.dll'
p=subprocess.Popen([DOTNET,str(host),str(directory/'policy.json')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True,creationflags=subprocess.CREATE_NO_WINDOW)
def request(x):
    p.stdin.write(json.dumps(x)+'\n');p.stdin.flush()
    return json.loads(p.stdout.readline())
checks=0;max_error=0
try:
    for row in traces:
        for step in row['trace']:
            o=step['observation'];got=request(dict(observation=o))
            with torch.no_grad():logits,_=model(torch.from_numpy(encode(o)[None]))
            max_error=max(max_error,float(np.max(np.abs(logits.numpy().flatten()-got['logits']))))
            assert got['action']==strategy.Decide(o)
            checks+=1
        got=request(dict(scenario=row['scenario'],mission=row['mission']))
        assert got==row['metrics'], 'C# ISearchStrategy trajectory metrics diverge'
    baseline_checks=0
    source=ROOT/'Experiments/results/20260917-063519/episodes.jsonl'
    with source.open() as f:
        for line in f:
            row=json.loads(line)
            if row['row']['seed']!=100000:continue
            got=request(dict(scenario=row['scenario'],mission=row['mission'],baseline=row['row']['strategy']=='B-coordinated'))
            assert got==row['row']['metrics'], 'Frozen baseline replay diverges'
            baseline_checks+=1
    frozen()
    report=dict(observationDecisions=checks,fullEpisodeMetrics=len(traces),baselineReplays=baseline_checks,maxLogitAbsoluteError=max_error,frozenSourcesUnchanged=True)
    (directory/'export-verification.json').write_text(json.dumps(report,indent=2))
    print(report)
finally:
    p.stdin.close();p.wait(timeout=10);p.stdout.close()
