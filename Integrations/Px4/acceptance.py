"""Read-only acceptance audit of recorded fixed-mission telemetry; no simulator launch."""
import argparse, hashlib, json, math, pathlib

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
            elif e['type']=='mission-snapshot':
                assert e['schemaVersion']==1 and e['runId']==run and e['missionRevision']==1
                assert e['sequence']==frames+1 and len(e['positions'])==len(e['vehicles'])==3
                assert len(e['routes'])==3 and len(e['covered'])==2400
                assert e['scenario']['width']==60 and e['scenario']['height']==40
                assert e['intent']['requiredFindings']==12 and e['totalRegions']==24
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
    assert frames>0
    snapshot=json.loads((path/'snapshot.json').read_text())
    assert snapshot['type']=='mission-snapshot' and snapshot['runId']==run and snapshot['sequence']<=frames
    assert summary.get('snapshotPublishErrors')==0
    for i in range(3):
        jpeg=(path/f'camera-{i}.jpg').read_bytes()
        assert len(jpeg)>1000 and jpeg[:2]==b'\xff\xd8' and jpeg[-2:]==b'\xff\xd9'
    decoupling=json.loads((path/'mission-control-decoupling.json').read_text())
    assert decoupling['unityTargetFps']==1 and decoupling['unityClosed']
    assert decoupling['frameUnchangedWhileSuspended'] and decoupling['frameResumed']
    assert decoupling['snapshotSequenceDuring']>decoupling['snapshotSequenceBefore']
    assert decoupling['missionSecondsDuring']>decoupling['missionSecondsBefore']
    assert decoupling['snapshotSequenceAfter']>decoupling['snapshotSequenceDuring']
    cleanup=json.loads((path/'cleanup.json').read_text())
    assert len([p for p in cleanup if p['name'].startswith('px4-')])==3
    assert len([p for p in cleanup if p['name'].startswith('camera-')])==3
    assert all(p['returncode'] is not None for p in cleanup)
    for i in range(3):assert 'Disarmed by landing' in (path/f'px4-{i}.log').read_text(errors='replace')
    if (path/'landing.json').exists():assert all(not v['armed'] for v in json.loads((path/'landing.json').read_text())['vehicles'])
    if (path/'supervisor-result.json').exists():
        result=json.loads((path/'supervisor-result.json').read_text());assert result['completed'] and result['error'] is None
    launcher=json.loads((path/'launcher-result.json').read_text())
    supervisor=json.loads((path/'supervisor-result.json').read_text())
    repo=pathlib.Path(__file__).resolve().parents[2]
    sources=['Assets/Swarm/Px4MissionControl.cs','Assets/Swarm/SimulationRunner.cs','Integrations/Px4/camera_capture.cpp',
        'Integrations/Px4/bridge.py','Integrations/Px4/Run.ps1','Integrations/Px4/Host/Contracts.cs','Integrations/Px4/Host/MissionHost.cs',
        'Integrations/Px4/models/mono_cam/model.sdf','Integrations/Px4/worlds/default.sdf',
        'Integrations/Px4/acceptance.py',
        'Integrations/Px4/README.md','Integrations/Px4/MISSION_CONTROL_REPORT.md']
    hashes={name:hashlib.sha256((repo/name).read_bytes()).hexdigest() for name in sources}
    frozen=0
    for manifest in ('Experiments/ppo/frozen.json','Experiments/ppo/report/artifact-hashes.json'):
        frozen+=len(json.loads((repo/manifest).read_text()))
    result=dict(accepted=True,runId=run,frames=frames,tasksByVehicle=counts,completedByVehicle=list(map(len,finished)),
        reason=summary['reason'],metrics=summary['metrics'],snapshotPublishErrors=summary['snapshotPublishErrors'],
        cameras=dict(count=3,format='JPEG',resolution=[640,480],maxFps=5),decoupling=decoupling,
        artifactDirectory=str(path.relative_to(repo)),launcher=launcher,supervisor=supervisor,frozenHashesVerified=frozen,sourceHashesAtAcceptance=hashes)
    text=json.dumps(result,indent=2)
    if args.output:pathlib.Path(args.output).write_text(text+'\n')
    print(text)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('run_directory');p.add_argument('--output');args=p.parse_args();audit(args.run_directory)
