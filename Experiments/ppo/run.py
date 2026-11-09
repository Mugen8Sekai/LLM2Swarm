"""Fixed-budget PPO pilot. Scenario sampler/evaluator are outside the policy boundary."""
import argparse
import hashlib
import itertools
import json
import os
from pathlib import Path
import random
import sys
import time

import numpy as np
import psutil
import torch

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE.parent))
from adapter import SearchAdapter
from policy import Policy, encode, masks, reward, observed_coverage

DOTNET = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/DotNetSdk/dotnet.exe'
DLL = ROOT / 'Tests/SimulationChecks/bin/Release/net8.0/SimulationChecks.dll'
CONFIG = json.loads((HERE/'config.json').read_text(encoding='utf-8-sig'))
OUTPUT = HERE.parent/'results/ppo'


def dump(path, obj):
    Path(path).write_text(json.dumps(obj, indent=2, allow_nan=False), encoding='utf-8')


def frozen():
    hashes = json.loads((HERE/'frozen.json').read_text(encoding='utf-8-sig'))
    for path, expected in hashes.items():
        assert hashlib.sha256((ROOT/path).read_bytes()).hexdigest() == expected, f'Frozen file changed: {path}'


def descriptor(seed, n, distribution, layout, k, deadline):
    assert 0 <= seed <= 79999 or 100000 <= seed <= 179999, 'TRAIN/VALIDATION only; TEST forbidden'
    starts = [30, 30, 30] if layout == 'common' else [10, 30, 50]
    return dict(scenario=dict(seed=int(seed), targetCount=int(n), distribution=distribution,
                             starts=[dict(x=x, y=0) for x in starts]),
                mission=dict(requiredFindings=int(k), maxSeconds=int(deadline), areas=['all']),
                condition=dict(targetCount=int(n), distribution=distribution, layout=layout, k=int(k), deadline=int(deadline)))


def conditions(seeds):
    for n, dist, layout, deadline in itertools.product([6, 12, 24], ['uniform', 'clustered'], ['common', 'dispersed'], [60, 120]):
        for k in [3, n]:
            for seed in seeds:
                yield descriptor(seed, n, dist, layout, k, deadline)


def sample(rng):
    n = rng.choice([6, 12, 24])
    return descriptor(rng.randrange(80000), n, rng.choice(['uniform', 'clustered']),
                      rng.choice(['common', 'dispersed']), rng.choice([3, n]), rng.choice([60, 120]))


def reset(env, d, c):
    return env.reset(d['scenario'], d['mission'], {k: c[k] for k in ['columns', 'rows', 'decisionSeconds']})


def tensors(states):
    obs = [s['observation'] for s in states]
    return torch.from_numpy(np.stack([encode(o) for o in obs])), torch.from_numpy(np.stack([masks(o) for o in obs]))


def external(a, n):
    return [int(v) if v < n else -1 for v in a]


@torch.no_grad()
def evaluate(model, envs, descriptors, c, coefficient, output=None, traces=False):
    todo = iter(descriptors)
    active = {}
    records = []
    def begin(i):
        while True:
            d = next(todo, None)
            if d is None:
                active.pop(i, None); return
            s = reset(envs[i], d, c)
            trace = traces and d['scenario']['seed'] == 100000 and d['condition']['targetCount'] == 12
            item = dict(d=d, s=s, components=np.zeros(3), idle=np.zeros(3), waits=0, decisions=0,
                        trace=[dict(observation=s['observation'])] if trace else None)
            active[i] = item
            if not s['terminated']:
                return
            finish(i)
    def finish(i):
        item = active[i]
        records.append(dict(**item['d'], metrics=item['s']['terminalMetrics'],
                            rewardComponents=item['components'].tolist(), idleSeconds=item['idle'].tolist(),
                            voluntaryWaits=item['waits'], freeDecisions=item['decisions'], trace=item['trace']))
    for i in range(len(envs)):
        begin(i)
    while active:
        ids = list(active)
        x, m = tensors([active[i]['s'] for i in ids])
        actions, _, _, _ = model.act(x, m, deterministic=True)
        for i, a in zip(ids, actions.numpy()):
            item = active[i]; before = item['s']['observation']
            action = external(a, model.regions)
            joint_reserved = set(before['assignments'])
            for d in range(3):
                if before['idle'][d] and any(not done and r not in joint_reserved for r,done in enumerate(before['completed'])):
                    item['decisions'] += 1
                    item['waits'] += int(action[d] == -1)
                if action[d] >= 0:
                    joint_reserved.add(action[d])
            s = envs[i].step(action)
            after = s['observation']
            _, components = reward(before, after, coefficient)
            item['components'] += components
            dt = after['seconds']-before['seconds']
            item['idle'] += np.array([before['idle'][d] and action[d] == -1 for d in range(3)])*dt
            if item['trace'] is not None:
                item['trace'].append(dict(action=action, observation=after, rewardComponents=components.tolist()))
            item['s'] = s
            if s['terminated']:
                # Metric agreement is evaluator-only, never used for reward or action.
                assert abs(observed_coverage(after)-s['terminalMetrics']['coverage']) < 1e-9
                finish(i); begin(i)
    if output:
        with Path(output).open('w', encoding='utf-8') as f:
            for r in records:
                f.write(json.dumps(r, allow_nan=False)+'\n')
    success = np.mean([r['metrics']['success'] for r in records])
    restricted = np.mean([r['metrics']['kFindingSeconds'] if r['metrics']['kFindingSeconds'] is not None else r['condition']['deadline'] for r in records])
    return dict(success=float(success), restrictedTime=float(restricted), episodes=len(records)), records


def cpu_seconds(process):
    times = process.cpu_times()
    return times.user+times.system


def train(seed, coefficient, c):
    frozen()
    directory = OUTPUT/('full' if coefficient else 'no-coverage')/str(seed)
    directory.mkdir(parents=True, exist_ok=True)
    if (directory/'complete.json').exists():
        print(f'SKIP completed {directory}', flush=True); return
    torch.set_num_threads(c['torchThreads']); torch.use_deterministic_algorithms(True)
    torch.manual_seed(seed); np.random.seed(seed)
    rng = random.Random(seed)
    envs = [SearchAdapter(DOTNET, DLL) for _ in range(c['environments'])]
    process = psutil.Process()
    children = [psutil.Process(e.process.pid) for e in envs]
    wall_start = time.perf_counter(); parent_cpu_start = cpu_seconds(process)
    rollout_seconds = update_seconds = validation_seconds = 0.
    peak_rss = 0
    try:
        first = reset(envs[0], sample(rng), c)
        model = Policy(len(encode(first['observation'])), c['columns']*c['rows'], c['hidden'])
        optimizer = torch.optim.Adam(model.parameters(), lr=c['learningRate'], eps=1e-5)
        dump(directory/'config.json', dict(c, seed=seed, coverageCoefficient=coefficient, torch=torch.__version__, numpy=np.__version__, python=sys.version,
                                           frozen=json.loads((HERE/'frozen.json').read_text(encoding='utf-8-sig'))))
        selection = list(conditions(c['selectionSeeds']))
        start = time.perf_counter()
        initial, _ = evaluate(model, envs, selection, c, coefficient, directory/'validation-0.jsonl')
        validation_seconds += time.perf_counter()-start
        best = (initial['success'], -initial['restrictedTime'])
        best_step = 0
        torch.save(model.state_dict(), directory/'initial.pt'); torch.save(model.state_dict(), directory/'selected.pt')
        selection_log = [dict(step=0, **initial)]
        print(f'{seed}/{coefficient} initial {initial}', flush=True)
        states, descriptions = [], []
        accum = [np.zeros(3) for _ in envs]
        episode_count = 0
        episode_file = (directory/'train-episodes.jsonl').open('w', encoding='utf-8')
        def new_episode(i):
            nonlocal episode_count
            while True:
                d = sample(rng); s = reset(envs[i], d, c)
                if not s['terminated']:
                    return d, s
                episode_count += 1
                episode_file.write(json.dumps(dict(**d, metrics=s['terminalMetrics'], rewardComponents=[0, 0, 0], step=step))+'\n')
        step = 0
        for i in range(len(envs)):
            d, s = new_episode(i); descriptions.append(d); states.append(s)
        updates = c['stepsPerSeed']//(len(envs)*c['rolloutSteps'])
        logs = []
        for update in range(1, updates+1):
            start = time.perf_counter()
            xs=[]; ms=[]; acts=[]; oldlogs=[]; vals=[]; rewards=[]; dones=[]
            ended = []
            for _ in range(c['rolloutSteps']):
                x, m = tensors(states)
                with torch.no_grad():
                    a, lp, _, v = model.act(x, m)
                rs=[]; ds=[]
                for i, action in enumerate(a.numpy()):
                    before = states[i]['observation']
                    next_state = envs[i].step(external(action, model.regions))
                    r, components = reward(before, next_state['observation'], coefficient)
                    accum[i] += components; rs.append(r); ds.append(float(next_state['terminated']))
                    if next_state['terminated']:
                        metric = next_state['terminalMetrics']
                        assert abs(observed_coverage(next_state['observation'])-metric['coverage']) < 1e-9
                        record = dict(**descriptions[i], metrics=metric, rewardComponents=accum[i].tolist(), step=step+len(envs))
                        episode_file.write(json.dumps(record)+'\n'); ended.append(record); episode_count += 1
                        descriptions[i], next_state = new_episode(i); accum[i] = np.zeros(3)
                    states[i] = next_state
                xs.append(x); ms.append(m); acts.append(a); oldlogs.append(lp); vals.append(v)
                rewards.append(torch.tensor(rs, dtype=torch.float32)); dones.append(torch.tensor(ds))
                step += len(envs)
            rollout_seconds += time.perf_counter()-start
            start = time.perf_counter()
            with torch.no_grad():
                _, bootstrap = model(tensors(states)[0])
                advantage = torch.zeros((c['rolloutSteps'], len(envs)))
                last = torch.zeros(len(envs))
                for t in reversed(range(c['rolloutSteps'])):
                    next_value = bootstrap if t == c['rolloutSteps']-1 else vals[t+1]
                    delta = rewards[t]+c['gamma']*next_value*(1-dones[t])-vals[t]
                    last = delta+c['gamma']*c['gaeLambda']*(1-dones[t])*last
                    advantage[t] = last
                returns = advantage+torch.stack(vals)
            bx=torch.cat(xs); bm=torch.cat(ms); ba=torch.cat(acts); bl=torch.cat(oldlogs)
            adv=advantage.flatten(); ret=returns.flatten()
            adv=(adv-adv.mean())/(adv.std()+1e-8)
            diagnostics=[]
            for epoch in range(c['epochs']):
                permutation=torch.randperm(len(bx))
                stop=False
                for begin in range(0,len(bx),c['minibatch']):
                    idx=permutation[begin:begin+c['minibatch']]
                    _, logp, entropy, value=model.act(bx[idx],bm[idx],ba[idx])
                    logratio=logp-bl[idx]; ratio=logratio.exp()
                    kl=((ratio-1)-logratio).mean()
                    if kl.item()>c['targetKL']:
                        stop=True; break
                    policy_loss=-torch.minimum(ratio*adv[idx],ratio.clamp(1-c['clip'],1+c['clip'])*adv[idx]).mean()
                    value_loss=.5*(value-ret[idx]).square().mean()
                    loss=policy_loss+c['valueCoefficient']*value_loss-c['entropyCoefficient']*entropy.mean()
                    optimizer.zero_grad(); loss.backward()
                    grad=torch.nn.utils.clip_grad_norm_(model.parameters(),c['maxGradNorm'])
                    assert torch.isfinite(grad), 'Nonfinite gradient'
                    optimizer.step()
                    diagnostics.append([policy_loss.item(),value_loss.item(),entropy.mean().item(),kl.item(),((ratio-1).abs()>c['clip']).float().mean().item(),grad.item()])
                if stop: break
            update_seconds += time.perf_counter()-start
            diag=np.mean(diagnostics,axis=0)
            peak_rss=max(peak_rss,process.memory_info().rss+sum(p.memory_info().rss for p in children))
            log=dict(step=step,update=update,episodes=episode_count,trainReturn=float(np.mean([sum(e['rewardComponents']) for e in ended])) if ended else None,
                     trainSuccess=float(np.mean([e['metrics']['success'] for e in ended])) if ended else None,
                     components=np.mean([e['rewardComponents'] for e in ended],axis=0).tolist() if ended else None,
                     policyLoss=float(diag[0]),valueLoss=float(diag[1]),entropy=float(diag[2]),approxKL=float(diag[3]),clipFraction=float(diag[4]),gradNorm=float(diag[5]),
                     rolloutSeconds=rollout_seconds,updateSeconds=update_seconds,validationSeconds=validation_seconds)
            logs.append(log)
            if update % c['validationEveryUpdates']==0 or update==updates:
                start=time.perf_counter()
                val,_=evaluate(model,envs,selection,c,coefficient,directory/f'validation-{step}.jsonl')
                validation_seconds+=time.perf_counter()-start
                selection_log.append(dict(step=step,**val))
                score=(val['success'],-val['restrictedTime'])
                torch.save(model.state_dict(),directory/f'checkpoint-{step}.pt')
                if score>best:
                    best=score;best_step=step;torch.save(model.state_dict(),directory/'selected.pt')
                # Validation uses same processes, so restart TRAIN episodes. Never stitch GAE across validation resets.
                for i in range(len(envs)):
                    descriptions[i],states[i]=new_episode(i);accum[i]=np.zeros(3)
                dump(directory/'selection.json',selection_log)
                print(f'{seed}/{coefficient} step {step}: validation {val}; train return {log["trainReturn"]:.3f}; {step/(rollout_seconds+update_seconds):.0f} steps/s',flush=True)
            if update%16==0:
                dump(directory/'curves.json',logs)
        episode_file.close()
        torch.save(model.state_dict(),directory/'last.pt')
        dump(directory/'curves.json',logs);dump(directory/'selection.json',selection_log)
        dump(directory/'complete.json',dict(seed=seed,steps=step,episodes=episode_count,selectedStep=best_step,initial=initial,
                                          selectedSuccess=best[0],selectedRestrictedTime=-best[1],wallSeconds=time.perf_counter()-wall_start,
                                          rolloutSeconds=rollout_seconds,updateSeconds=update_seconds,validationSeconds=validation_seconds,
                                          trainingTransitionsPerSecond=step/(rollout_seconds+update_seconds),parentCpuSeconds=cpu_seconds(process)-parent_cpu_start,
                                          environmentCpuSeconds=sum(cpu_seconds(p) for p in children),peakCombinedRssBytes=peak_rss,gpuUsed=False))
        frozen()
    finally:
        for e in envs:e.close()


def paired(seed, coefficient, c):
    frozen();torch.set_num_threads(c['torchThreads'])
    directory=OUTPUT/('full' if coefficient else 'no-coverage')/str(seed)
    envs=[SearchAdapter(DOTNET,DLL) for _ in range(c['environments'])]
    try:
        first=reset(envs[0],descriptor(100000,12,'uniform','common',12,120),c)
        model=Policy(len(encode(first['observation'])),c['columns']*c['rows'],c['hidden'])
        model.load_state_dict(torch.load(directory/'selected.pt',weights_only=True));model.eval()
        start=time.perf_counter()
        summary,_=evaluate(model,envs,list(conditions(range(100000,100100))),c,coefficient,directory/'paired.jsonl',True)
        dump(directory/'paired-summary.json',dict(**summary,wallSeconds=time.perf_counter()-start))
        export=dict(regions=model.regions,columns=c['columns'],rows=c['rows'],weights={k:v.tolist() for k,v in model.state_dict().items()})
        dump(directory/'policy.json',export)
        print(f'PAIRED {seed}/{coefficient} {summary}',flush=True)
    finally:
        for e in envs:e.close()


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=['train','paired','freeze-check']);parser.add_argument('--seed',type=int,default=11);parser.add_argument('--no-coverage',action='store_true')
    args=parser.parse_args()
    if args.mode=='freeze-check':frozen();print('Frozen sources unchanged')
    elif args.mode=='train':train(args.seed,0 if args.no_coverage else .01,CONFIG)
    else:paired(args.seed,0 if args.no_coverage else .01,CONFIG)
