"""Contract checks without starting a vehicle or opening sockets."""
import asyncio, json, pathlib, tempfile, types, unittest
from bridge import Bridge

class ContractTests(unittest.IsolatedAsyncioTestCase):
    async def test_joint_validation_is_atomic(self):
        with tempfile.TemporaryDirectory() as folder:
            b=Bridge(types.SimpleNamespace(output=folder));b.drones=[None]*3
            b.states=[dict(taskState='idle') for _ in range(3)]
            def task(i,seq=1):return dict(vehicleId=i,runId=b.run,frameId='map-enu-v1',missionRevision=1,sequence=seq,taskId=str(seq),ned=[[0,0,-5]])
            for tasks in ([task(0),task(0)],[task(3)],[dict(task(0),missionRevision=2)],[dict(task(0),ned=[[float('nan'),0,0]])]):
                with self.assertRaises(ValueError):b.submit(tasks)
                self.assertFalse(b.goal_jobs);self.assertFalse(b.last_sequences)
            b.states[0]['taskState']='active'
            with self.assertRaises(ValueError):b.submit([task(0)])
            b.states[0]['taskState']='idle';b.last_sequences[0]=2
            with self.assertRaises(ValueError):b.submit([task(0)])
    async def test_ack_is_not_arrival(self):
        with tempfile.TemporaryDirectory() as folder:
            b=Bridge(types.SimpleNamespace(output=folder))
            class Offboard:
                async def set_position_ned(self,p):pass
            b.drones=[types.SimpleNamespace(offboard=Offboard())]
            s=dict(north=0,east=0,down=-5,vn=0,ve=0,vd=0);b.states=[s]
            job=asyncio.create_task(b.execute(0,dict(taskId='a',ned=[[10,0,-5]])))
            await asyncio.sleep(.06);self.assertEqual(s['taskState'],'active');self.assertEqual(s['waypoint'],0)
            s['north']=10;b.sim=1;await asyncio.sleep(.06);b.sim=1.6
            await asyncio.wait_for(job,.3);self.assertEqual(s['taskState'],'completed');self.assertEqual(s['waypoint'],1)

if __name__=='__main__':unittest.main()
