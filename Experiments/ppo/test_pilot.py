import copy
import json
import unittest
import numpy as np
import torch
from policy import Policy, PpoStrategy, encode, masks, reward, observed_coverage
from run import SearchAdapter, DOTNET, DLL, CONFIG, descriptor, reset, frozen


class PilotTests(unittest.TestCase):
    def setUp(self):
        torch.set_num_threads(1);torch.manual_seed(123)
        self.env=SearchAdapter(DOTNET,DLL)
        self.state=reset(self.env,descriptor(123,12,'uniform','common',12,120),CONFIG)
        self.obs=self.state['observation']

    def tearDown(self):
        self.env.close()
        for stream in [self.env.process.stdin,self.env.process.stdout,self.env.process.stderr]:stream.close()

    def test_frozen_and_hidden(self):
        frozen()
        other=SearchAdapter(DOTNET,DLL)
        try:
            a=self.env.reset(dict(seed=11,targetCount=1,sensorRadius=.01),dict(requiredFindings=12,maxSeconds=120,areas=['all']))['observation']
            b=other.reset(dict(seed=913,targetCount=99,sensorRadius=.01,distribution='clustered'),dict(requiredFindings=12,maxSeconds=120,areas=['all']))['observation']
            np.testing.assert_array_equal(encode(a),encode(b));np.testing.assert_array_equal(masks(a),masks(b))
            with self.assertRaises(ValueError):encode(dict(a,seed=11))
            model=Policy(len(encode(a)),24)
            self.assertEqual(PpoStrategy(model).Decide(a),PpoStrategy(model).Decide(b))
        finally:
            other.close()
            for stream in [other.process.stdin,other.process.stdout,other.process.stderr]:stream.close()

    def test_k_deadline_representable(self):
        self.assertFalse(np.array_equal(encode(self.obs),encode(dict(self.obs,requiredFindings=3))))
        self.assertFalse(np.array_equal(encode(self.obs),encode(dict(self.obs,remainingSeconds=60))))
        with self.assertRaises(AssertionError):descriptor(1000000,12,'uniform','common',12,120)

    def test_joint_masks_and_logprob(self):
        model=Policy(len(encode(self.obs)),24)
        x=torch.from_numpy(np.stack([encode(self.obs)]*256))
        m=torch.from_numpy(np.stack([masks(self.obs)]*256))
        a,lp,_,_=model.act(x,m)
        _,replayed,_,_=model.act(x,m,a)
        torch.testing.assert_close(lp,replayed)
        for action in a.tolist():
            regions=[r for r in action if r<24]
            self.assertEqual(len(regions),len(set(regions)))
        (-replayed.mean()).backward()
        self.assertTrue(all(p.grad is None or torch.isfinite(p.grad).all() for p in model.parameters()))
        busy=dict(self.obs,idle=[False]*3)
        actions,logp,ent,_=model.act(x,torch.from_numpy(np.stack([masks(busy)]*256)))
        self.assertTrue((actions==24).all());self.assertTrue((logp==0).all());self.assertTrue((ent==0).all())

    def test_reward_no_shaping_revisits(self):
        before=self.obs
        after=self.env.step([-1,-1,-1])['observation']
        r,comp=reward(before,after)
        np.testing.assert_allclose(comp,[0,-.1/120,0],atol=1e-10)
        self.assertLess(r,0)
        fake=copy.deepcopy(before);fake['found']=100
        self.assertLessEqual(reward(before,fake)[1][0],1)
        self.assertEqual(reward(before,fake,0)[1][2],0)

    def test_reward_telescopes_and_valid_trajectory(self):
        model=Policy(len(encode(self.obs)),24)
        initial=self.obs;total=np.zeros(3);state=self.state
        for _ in range(130):
            if state['terminated']:break
            before=state['observation']
            x=torch.from_numpy(encode(before)[None]);m=torch.from_numpy(masks(before)[None])
            with torch.no_grad():a,_,_,_=model.act(x,m)
            state=self.env.step([int(v) if v<24 else -1 for v in a[0]])
            total+=reward(before,state['observation'])[1]
        self.assertTrue(state['terminated'])
        final=state['observation']
        np.testing.assert_allclose(total,[(min(final['found'],12)-min(initial['found'],12))/12,-.1*final['seconds']/120,.01*(observed_coverage(final)-observed_coverage(initial))],atol=1e-10)
        self.assertAlmostEqual(observed_coverage(final),state['terminalMetrics']['coverage'])
        self.assertLessEqual(total.sum(),1.01);self.assertGreaterEqual(total.sum(),-.1)

    def test_grid_configurable(self):
        obs=self.env.reset(dict(seed=1),dict(requiredFindings=12,maxSeconds=120,areas=['all']),dict(columns=3,rows=2,decisionSeconds=1))['observation']
        policy=PpoStrategy(Policy(len(encode(obs)),6));policy.Reset(obs)
        self.env.step(policy.Decide(obs))


if __name__=='__main__':unittest.main(verbosity=2)
