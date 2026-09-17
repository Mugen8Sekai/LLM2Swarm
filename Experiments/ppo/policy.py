"""Centralized observation-only actor/critic and reward. No scenario imports."""
import numpy as np
import torch
from torch import nn
from torch.distributions import Categorical

FIELDS = {'width', 'height', 'speed', 'sensorRadius', 'columns', 'rows',
          'requiredFindings', 'found', 'seconds', 'remainingSeconds', 'positions',
          'idle', 'completed', 'assignments', 'regionFindings', 'remainingDistance', 'regionCoverage'}


def encode(o):
    if set(o) != FIELDS:
        raise ValueError('Policy accepts SearchObservation only')
    n = o['columns'] * o['rows']
    k = o['requiredFindings']
    deadline = o['seconds'] + o['remainingSeconds']
    diagonal = np.hypot(o['width'], o['height'])
    features = [o['width']/60, o['height']/40, o['speed']/4, o['sensorRadius']/3,
                o['columns']/6, o['rows']/4, k/24, o['found']/k,
                deadline/120, o['seconds']/deadline, o['remainingSeconds']/deadline]
    for d in range(3):
        features += [o['positions'][d]['x']/o['width'], o['positions'][d]['y']/o['height'],
                     float(o['idle'][d]), o['remainingDistance'][d]/diagonal]
        features += [float(o['assignments'][d] == r) for r in range(n)]
    for r in range(n):
        features += [float(o['completed'][r]), o['regionCoverage'][r], o['regionFindings'][r]/k]
    return np.asarray(features, dtype=np.float32)


def masks(o):
    n = len(o['completed'])
    mask = np.zeros((3, n+1), dtype=bool)
    mask[:, n] = True  # wait remains legal, also for idle UAVs
    reserved = set(o['assignments'])
    for d in range(3):
        if o['idle'][d]:
            mask[d, :n] = [not done and r not in reserved for r, done in enumerate(o['completed'])]
    return mask


def coverage(o):
    # Same cell-center allocation and edge areas as the frozen environment, using public geometry.
    # Pilot uses the frozen 1 m cell size; cellSize is absent from SearchObservation.
    # Other physical coverage resolutions must not silently use this adapter.
    cols, rows = o['columns'], o['rows']
    areas = np.zeros(cols*rows)
    for y in range(int(np.ceil(o['height']))):
        h = min(1, o['height']-y)
        ry = min(rows-1, int((y+h/2)/o['height']*rows))
        for x in range(int(np.ceil(o['width']))):
            w = min(1, o['width']-x)
            rx = min(cols-1, int((x+w/2)/o['width']*cols))
            areas[ry*cols+rx] += w*h
    return float(np.dot(areas, o['regionCoverage'])/(o['width']*o['height']))


_area_weights = {}
def observed_coverage(o):
    key = (o['width'], o['height'], o['columns'], o['rows'])
    if key not in _area_weights:
        n = len(o['completed'])
        weights = []
        for r in range(n):
            probe = dict(o, regionCoverage=[float(i == r) for i in range(n)])
            weights.append(coverage(probe))
        _area_weights[key] = np.asarray(weights)
    return float(np.dot(_area_weights[key], o['regionCoverage']))


def reward(before, after, coefficient=.01):
    k = before['requiredFindings']
    deadline = before['seconds'] + before['remainingSeconds']
    components = np.array([(min(after['found'], k)-min(before['found'], k))/k,
                           -.1*(after['seconds']-before['seconds'])/deadline,
                           coefficient*(observed_coverage(after)-observed_coverage(before))])
    return float(components.sum()), components


class Policy(nn.Module):
    def __init__(self, inputs, regions, hidden=128):
        super().__init__()
        self.regions = regions
        self.body = nn.Sequential(nn.Linear(inputs, hidden), nn.Tanh(), nn.Linear(hidden, hidden), nn.Tanh())
        self.actor = nn.Linear(hidden, 3*(regions+1))
        self.critic = nn.Linear(hidden, 1)
        for layer in self.modules():
            if isinstance(layer, nn.Linear):
                nn.init.orthogonal_(layer.weight, np.sqrt(2)); nn.init.zeros_(layer.bias)
        nn.init.orthogonal_(self.actor.weight, .01)
        nn.init.orthogonal_(self.critic.weight, 1)

    def forward(self, x):
        h = self.body(x)
        return self.actor(h).reshape(-1, 3, self.regions+1), self.critic(h).squeeze(-1)

    def act(self, x, mask, actions=None, deterministic=False):
        logits, value = self(x)
        available = mask.clone()
        chosen, logs, entropies = [], [], []
        for d in range(3):
            valid = available[:, d].clone()
            dist = Categorical(logits=logits[:, d].masked_fill(~valid, -1e9))
            a = actions[:, d] if actions is not None else (dist.logits.argmax(-1) if deterministic else dist.sample())
            chosen.append(a); logs.append(dist.log_prob(a)); entropies.append(dist.entropy())
            # Exact joint distribution under sequential exclusion, same mask during PPO replay.
            for later in range(d+1, 3):
                available[torch.arange(x.shape[0]), later, a] &= a == self.regions
        return torch.stack(chosen, 1), torch.stack(logs).sum(0), torch.stack(entropies).sum(0), value


class PpoStrategy:
    """Reset/Decide contract. Receives only detached SearchObservation dictionaries."""
    def __init__(self, model):
        self.model = model

    def Reset(self, observation):
        encode(observation)
        if len(observation['completed']) != self.model.regions:
            raise ValueError('Checkpoint grid mismatch; retrain for configured grid')

    @torch.no_grad()
    def Decide(self, observation):
        action, _, _, _ = self.model.act(torch.from_numpy(encode(observation)[None]),
                                        torch.from_numpy(masks(observation)[None]), deterministic=True)
        return [int(a) if a < self.model.regions else -1 for a in action[0]]
