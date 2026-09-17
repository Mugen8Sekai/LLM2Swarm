"""Persistent JSON-lines adapter for the C# environment. No algorithm or training dependency."""
import json
import subprocess
import time
from pathlib import Path


class SearchAdapter:
    def __init__(self, dotnet, dll):
        self.process = subprocess.Popen(
            [str(dotnet), str(dll), "serve"], stdin=subprocess.PIPE,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8",
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )

    def _request(self, payload):
        self.process.stdin.write(json.dumps(payload, allow_nan=False) + "\n")
        self.process.stdin.flush()
        line = self.process.stdout.readline()
        if not line:
            raise RuntimeError("C# environment exited unexpectedly")
        reply = json.loads(line)
        if not reply["ok"]:
            raise ValueError(reply["error"])
        return reply.get("result")

    def reset(self, scenario, mission, config=None):
        return self._request(dict(op="reset", scenario=scenario, mission=mission,
                                  config=config or dict(columns=6, rows=4, decisionSeconds=1)))

    def step(self, action):
        return self._request(dict(op="step", action=action))

    def close(self):
        if self.process.poll() is None:
            self._request(dict(op="close"))
            self.process.wait(timeout=5)


def probe(dotnet, dll, output):
    env = SearchAdapter(dotnet, dll)
    mission = dict(requiredFindings=100, maxSeconds=120, areas=["all"])
    config = dict(columns=6, rows=4, decisionSeconds=1)
    def run(episodes):
        transitions = 0
        first = None
        for episode in range(episodes):
            state = env.reset(dict(seed=90000+episode), mission, config)
            while not state["terminated"]:
                assert state["terminalMetrics"] is None  # no hidden total count during an episode
                obs = state["observation"]
                banned = {"seed", "targets", "targetCount", "distribution", "clusters", "scenario"}
                assert not banned.intersection(obs)
                reserved = {x for x in obs["assignments"] if x >= 0}
                action = [-1, -1, -1]
                for drone in range(3):
                    if obs["idle"][drone]:
                        candidate = next((i for i, done in enumerate(obs["completed"])
                                          if not done and i not in reserved), None)
                        if candidate is not None:
                            action[drone] = candidate
                            reserved.add(candidate)
                state = env.step(action)
                transitions += 1
            if episode == 0:
                first = state["terminalMetrics"]
        return transitions, first
    try:
        for _ in range(5):
            run(1)
        repeats = []
        for _ in range(5):
            start = time.perf_counter()
            transitions, first = run(100)
            seconds = time.perf_counter() - start
            repeats.append(dict(episodes=100, transitions=transitions, seconds=seconds,
                                transitionsPerSecond=transitions/seconds))
        result = dict(repeats=repeats, terminalMetricsFirstEpisode=first,
                      protocol="jsonl-persistent", includes="reset, IPC, observations, deterministic dispatcher, metrics",
                      training=False, seedRange="90000..90099", split="adapter-probe")
        Path(output).parent.mkdir(parents=True, exist_ok=True)
        Path(output).write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(json.dumps(result, indent=2))
    finally:
        env.close()


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--dll", required=True)
    parser.add_argument("--output", default="Experiments/results/adapter-throughput.json")
    args = parser.parse_args()
    probe(args.dotnet, args.dll, args.output)
