"""Read-only acceptance audit of recorded fixed-mission telemetry; no simulator launch."""
import argparse, json, math, pathlib

def audit(folder):
    path=pathlib.Path(folder)
    summary=json.loads((path/'summary.json').read_text())
    assert summary['reason'] in ('deadline','success','exhausted') and summary['failure'] is None
    tasks={};counts=[0]*3;finished=[set() for _ in range(3)];last_time=-1;progress={};frames=0
    with (path/'events.jsonl').open() as stream:
        for line in stream:
            e=json.loads(line)
            if e['type']=='configuration':
                origins=e['origins'];run=e['runId']
                assert e['strategy']=='A-systematic' and e['heights']==[5,8,11]
                assert e['scenario']['seed']==100000 and e['intent']['requiredFindings']==12
            elif e['type']=='tasks':
                ids=set()
                for t in e['tasks']:
                    i=t['vehicleId'];assert i in range(3) and i not in ids;ids.add(i)
                    assert t['runId']==run and t['taskId'] not in tasks and t['missionRevision']==1
                    assert len(t['points'])==len(t['ned'])>0
                    o=origins[i]
                    for p,n in zip(t['points'],t['ned']):
                        expected=[o['north']+p['y']-o['y'],o['east']+p['x']-o['x'],o['down']-5-3*i+o['height']]
                        assert max(abs(a-b) for a,b in zip(n,expected))<1e-5
                    tasks[t['taskId']]=t;counts[i]+=1
            elif e['type']=='telemetry':
                assert e['seconds']>last_time;last_time=e['seconds'];frames+=1
                assert [v['id'] for v in e['vehicles']]==[0,1,2]
                for v in e['vehicles']:
                    i=v['id'];assert v['sysId']==i+1 and v['mode']=='OFFBOARD' and v['connected'] and v['armed']
                    assert v['error'] is None and v['ageWall']<2
                    if not v['taskId']:continue
                    t=tasks[v['taskId']];assert t['vehicleId']==i
                    assert v['waypoint']>=progress.get(t['taskId'],0);progress[t['taskId']]=v['waypoint']
                    if v['taskState']=='completed' and t['taskId'] not in finished[i]:
                        assert v['waypoint']==len(t['ned'])
                        n=t['ned'][-1]
                        assert math.hypot(v['north']-n[0],v['east']-n[1])<=1.05
                        assert abs(v['down']-n[2])<=.55
                        finished[i].add(t['taskId'])
    assert all(x>0 for x in counts) and all(len(x)>0 for x in finished)
    assert len(tasks)==summary['tasksIssued'] and sum(map(len,finished))==summary['completedRegions']
    cleanup=json.loads((path/'cleanup.json').read_text())
    assert len([p for p in cleanup if p['name'].startswith('px4-')])==3
    assert all(p['returncode'] is not None for p in cleanup)
    for i in range(3):assert 'Disarmed by landing' in (path/f'px4-{i}.log').read_text(errors='replace')
    if (path/'landing.json').exists():assert all(not v['armed'] for v in json.loads((path/'landing.json').read_text())['vehicles'])
    if (path/'supervisor-result.json').exists():
        result=json.loads((path/'supervisor-result.json').read_text());assert result['completed'] and result['error'] is None
    print(json.dumps(dict(accepted=True,runId=run,frames=frames,tasksByVehicle=counts,completedByVehicle=list(map(len,finished)),reason=summary['reason'],metrics=summary['metrics']),indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('run_directory');audit(p.parse_args().run_directory)
