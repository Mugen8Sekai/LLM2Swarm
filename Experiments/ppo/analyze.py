"""Paired physical metrics, seed variability, learning curves, and observable trajectories."""
import collections
import csv
import json
import math
from pathlib import Path
import shutil
import os
import numpy as np
os.environ.setdefault('MPLCONFIGDIR',str(Path(__file__).resolve().parents[1]/'results/mpl'))
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import torch
from run import ROOT, HERE, OUTPUT, CONFIG, frozen
from policy import Policy, PpoStrategy, encode

DEST=HERE/'report'
DEST.mkdir(exist_ok=True)
KEYS=['targetCount','distribution','layout','k','deadline']
def key(r):return tuple(r['condition'][k] for k in KEYS)+(r['scenario']['seed'],)
def read(path):
    with Path(path).open() as f:return [json.loads(line) for line in f]
def restricted(r):
    t=r['metrics']['kFindingSeconds']
    return r['condition']['deadline'] if t is None else t
def mean(values):return float(np.mean(values)) if values else None
def metric_summary(rows):
    m=[r['metrics'] for r in rows]
    first=[v['firstFindingSeconds'] for v in m if v['firstFindingSeconds'] is not None]
    kt=[v['kFindingSeconds'] for v in m if v['kFindingSeconds'] is not None]
    return dict(n=len(rows),success=mean([v['success'] for v in m]),firstTimeConditional=mean(first),firstTimeObserved=len(first),
                kTimeConditional=mean(kt),kTimeObserved=len(kt),restrictedTime=mean([restricted(r) for r in rows]),
                coverage=mean([v['coverage'] for v in m]),foundFraction=mean([v['foundFraction'] for v in m]),
                redundancy=mean([v['redundantAreaSeconds'] for v in m]),exposure=mean([v['exposureAreaSeconds'] for v in m]),
                distance=mean([v['totalDistance'] for v in m]),distanceByDrone=np.mean([v['distanceByDrone'] for v in m],axis=0).tolist(),
                newAreaByDrone=np.mean([v['newAreaByDrone'] for v in m],axis=0).tolist(),findingsByDrone=np.mean([v['findingsByDrone'] for v in m],axis=0).tolist(),
                idleSeconds=np.mean([r.get('idleSeconds',[0,0,0]) for r in rows],axis=0).tolist() if 'idleSeconds' in rows[0] else None,
                voluntaryWaitFraction=sum(r.get('voluntaryWaits',0) for r in rows)/max(1,sum(r.get('freeDecisions',0) for r in rows)) if 'idleSeconds' in rows[0] else None,
                rewardComponents=np.mean([r['rewardComponents'] for r in rows],axis=0).tolist() if 'rewardComponents' in rows[0] else None)

baselines=collections.defaultdict(list)
for r in read(ROOT/'Experiments/results/20260917-063519/episodes.jsonl'):
    c={k:r['row'][k] for k in KEYS}
    baselines[r['row']['strategy']].append(dict(condition=c,scenario=r['scenario'],mission=r['mission'],metrics=r['row']['metrics']))
baseline_lookup={key(r):r for r in baselines['A-systematic']}
data=dict(baselines);complete={};selections={};curves={}
for variant in ['full','no-coverage']:
    for seed in CONFIG['trainingSeeds']:
        directory=OUTPUT/variant/str(seed)
        if not (directory/'paired.jsonl').exists():continue
        label=f'{variant}-{seed}'
        data[label]=read(directory/'paired.jsonl')
        complete[label]=json.loads((directory/'complete.json').read_text())
        selections[label]=json.loads((directory/'selection.json').read_text())
        curves[label]=json.loads((directory/'curves.json').read_text())
        archive=DEST/label;archive.mkdir(exist_ok=True)
        for name in ['complete.json','config.json','selection.json','curves.json','paired-summary.json','export-verification.json']:
            if (directory/name).exists():shutil.copyfile(directory/name,archive/name)
        # Compact representative episodes are reviewable without the ignored large files.
        (archive/'trajectories.json').write_text(json.dumps([r for r in data[label] if r['trace']]),encoding='utf-8')
        for r in data[label]:
            expected=baseline_lookup[key(r)]
            for field,value in r['scenario'].items():assert expected['scenario'][field]==value
            assert expected['mission']==r['mission']
overall={name:metric_summary(rows) for name,rows in data.items()}
by_condition=[];paired=[];by_distribution=[]
for name,rows in data.items():
    groups=collections.defaultdict(list)
    for r in rows:groups[tuple(r['condition'][k] for k in KEYS)].append(r)
    for condition,group in groups.items():by_condition.append(dict(strategy=name,condition=dict(zip(KEYS,condition)),**metric_summary(group)))
    for distribution in ['uniform','clustered']:
        by_distribution.append(dict(strategy=name,distribution=distribution,**metric_summary([r for r in rows if r['condition']['distribution']==distribution])))
    if name.startswith('A-') or name.startswith('B-'):continue
    for baseline,base_rows in baselines.items():
        lookup={key(r):r for r in base_rows}
        for condition,group in groups.items():
            differences=[restricted(r)-restricted(lookup[key(r)]) for r in group]
            delta=mean(differences);se=float(np.std(differences,ddof=1)/np.sqrt(len(differences)))
            paired.append(dict(strategy=name,baseline=baseline,condition=dict(zip(KEYS,condition)),n=len(group),restrictedDelta=delta,ci95=[delta-1.984*se,delta+1.984*se],
                               successDelta=mean([int(r['metrics']['success'])-int(lookup[key(r)]['metrics']['success']) for r in group])))

diagnostics={}
torch.set_num_threads(1)
for label,rows in data.items():
    if not label.startswith('full-'):continue
    seed=int(label.split('-')[1]);directory=OUTPUT/'full'/str(seed)
    traces=[r for r in rows if r.get('trace')]
    model=Policy(len(encode(traces[0]['trace'][0]['observation'])),24)
    model.load_state_dict(torch.load(directory/'selected.pt',weights_only=True));strategy=PpoStrategy(model)
    eligible=changed=0;assignments=[]
    for r in traces:
        assigned=[[] for _ in range(3)]
        for step in r['trace']:
            if 'action' in step:
                for d,a in enumerate(step['action']):
                    if a>=0:assigned[d].append(a)
            o=step['observation']
            covered=[i for i,v in enumerate(o['regionCoverage']) if v>0]
            actionable=any(not done and i not in o['assignments'] for i,done in enumerate(o['completed']))
            if step is not r['trace'][-1] and actionable and any(o['idle']) and len(covered)>1 and o['found']>0:
                new=list(o['regionFindings']);values=[new[i] for i in covered][::-1]
                for i,v in zip(covered,values):new[i]=v
                if new!=o['regionFindings']:
                    eligible+=1;changed+=int(strategy.Decide(o)!=strategy.Decide(dict(o,regionFindings=new)))
        assignments.append(dict(condition=r['condition'],seed=r['scenario']['seed'],regionsByDrone=assigned,
                                duplicateAssignments=sum(len(a)-len(set(a)) for a in assigned),metrics=r['metrics'],idleSeconds=r['idleSeconds']))
    diagnostics[label]=dict(spatialFindingPermutationEligible=eligible,spatialFindingPermutationChanged=changed,
                             changedFraction=changed/eligible if eligible else None,representativeAssignments=assignments)

with (DEST/'metrics.csv').open('w',newline='',encoding='utf-8') as f:
    writer=csv.DictWriter(f,fieldnames=['strategy']+list(next(iter(overall.values())).keys()));writer.writeheader()
    for name,values in overall.items():writer.writerow(dict(strategy=name,**values))
report=dict(overall=overall,byCondition=by_condition,byDistribution=by_distribution,paired=paired,training=complete,diagnostics=diagnostics)
(DEST/'results.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
for filename,records in [('conditions.csv',by_condition),('paired.csv',paired)]:
    flat=[dict(**{k:v for k,v in r.items() if k!='condition'},**r['condition']) for r in records]
    with (DEST/filename).open('w',newline='',encoding='utf-8') as f:
        writer=csv.DictWriter(f,fieldnames=list(flat[0]));writer.writeheader();writer.writerows(flat)

fig,axes=plt.subplots(2,3,figsize=(15,8))
for label,logs in curves.items():
    style='--' if label.startswith('no-') else '-'
    xs=np.array([r['step'] for r in logs])/1000
    def smooth(values):return np.convolve(values,np.ones(16)/16,mode='valid')
    axes[0,0].plot(xs[15:],smooth([r['trainReturn'] for r in logs]),style,label=label)
    sel=selections[label];axes[0,1].plot([s['step']/1000 for s in sel],[s['success'] for s in sel],style+'o',label=label)
    axes[0,2].plot(xs[15:],smooth([r['entropy'] for r in logs]),style,label=label)
    axes[1,0].plot(xs[15:],smooth([r['valueLoss'] for r in logs]),style,label=label)
    axes[1,1].plot(xs[15:],smooth([r['approxKL'] for r in logs]),style,label=label)
    axes[1,2].plot(xs[15:],smooth([r['clipFraction'] for r in logs]),style,label=label)
for ax,title in zip(axes.flat,['Retorno TRAIN (media 16 updates)','Éxito VALIDATION selección','Entropía conjunta (incluye acciones forzadas)','Pérdida del crítico','KL aproximada','Fracción clipping']):
    ax.set_title(title);ax.set_xlabel('Miles de transiciones');ax.grid(alpha=.2)
axes[0,0].legend(fontsize=8);fig.tight_layout();fig.savefig(DEST/'learning-curves.png',dpi=160);plt.close(fig)
fig,axes=plt.subplots(1,3,figsize=(14,4))
for label,logs in curves.items():
    for j in range(3):
        axes[j].plot(np.array([r['step'] for r in logs])[15:]/1000,np.convolve([r['components'][j] for r in logs],np.ones(16)/16,mode='valid'),label=label)
for ax,title in zip(axes,['Hallazgos Δmin(F,K)/K','Tiempo −0,1 Δt/T','Nueva cobertura 0,01 ΔA/A']):
    ax.set_title(title);ax.set_xlabel('Miles de transiciones');ax.grid(alpha=.2)
axes[0].legend(fontsize=7);fig.tight_layout();fig.savefig(DEST/'reward-components.png',dpi=160);plt.close(fig)

fig,axes=plt.subplots(4,3,figsize=(14,15))
examples=[('uniform','common'),('uniform','dispersed'),('clustered','common'),('clustered','dispersed')]
for col,seed in enumerate(CONFIG['trainingSeeds']):
    for row,(distribution,layout) in enumerate(examples):
        r=next(r for r in data[f'full-{seed}'] if r.get('trace') and r['condition']==dict(targetCount=12,distribution=distribution,layout=layout,k=12,deadline=60))
        ax=axes[row,col]
        for d,color in enumerate(['#0072B2','#D55E00','#009E73']):
            points=np.array([[s['observation']['positions'][d]['x'],s['observation']['positions'][d]['y']] for s in r['trace']])
            ax.plot(points[:,0],points[:,1],color=color,lw=1.4,label=f'UAV {d}');ax.scatter(*points[0],color=color,marker='o');ax.scatter(*points[-1],color=color,marker='x')
        ax.set_xlim(0,60);ax.set_ylim(0,40);ax.set_xticks(range(0,61,10));ax.set_yticks(range(0,41,10));ax.grid(alpha=.2);ax.set_aspect('equal')
        ax.set_title(f'Seed RL {seed} · {distribution}/{layout}\n{r["metrics"]["found"]}/12, cobertura {r["metrics"]["coverage"]:.0%}',fontsize=10)
axes[0,0].legend(fontsize=7);fig.suptitle('Escenario 100000, K=12, plazo 60 s · círculos=inicio, cruces=fin\nTrayectorias muestreadas cada decisión; no se muestran objetivos ocultos',fontsize=13)
fig.tight_layout(rect=[0,0,1,.96]);fig.savefig(DEST/'trajectories.png',dpi=160);plt.close(fig)
frozen()
print(json.dumps(dict(overall=overall,training=complete,diagnostics={k:{a:b for a,b in v.items() if a!='representativeAssignments'} for k,v in diagnostics.items()}),indent=2))
