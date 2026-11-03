import importlib.util
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
from paired_analysis import analyze, interval

ROOT = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("batch", ROOT / "batch.py")
batch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(batch)


class BatchChecks(unittest.TestCase):
    def test_paired_failures_and_seed_resampling(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory=pathlib.Path(temporary)
            batch.write_json(directory / "source-mission.json", dict(intent=dict(maxSeconds=120)))
            rows=[]
            for seed in (100003,100004):
                rows += [dict(seed=seed,repeat=0,strategy="A",valid=True,success=False,kth_finding=None,distance=50,coverage=.4),
                         dict(seed=seed,repeat=0,strategy="PPO",valid=True,success=True,kth_finding=20,distance=40,coverage=.3)]
            rows.append(dict(seed=100005,repeat=0,strategy="PPO",valid=False))
            analyze(directory, rows, {})
            result=json.loads((directory / "paired-analysis.json").read_text())
            comparison=result["comparisons"][0]
            self.assertEqual(comparison["pairedSeeds"],2)
            self.assertEqual(comparison["metrics"]["restrictedSeconds"]["meanDifference"],-100)
            self.assertEqual(interval([-100,-100]),[-100,-100])

    def test_paired_plan_and_missing_results(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = pathlib.Path(temporary)
            source = directory / "mission.json"
            mission = dict(intent=dict(requiredFindings=6, maxSeconds=120, areas=["all"]), instruction="Buscar seis", evidence="reference")
            source.write_text(json.dumps(mission), encoding="utf-8")
            output = directory / "batch"
            subprocess.run([sys.executable, str(ROOT / "batch.py"), "--mission", str(source), "--output", str(output)], check=True)
            manifest = json.loads((output / "batch.json").read_text())
            self.assertEqual(len(manifest["trials"]), 9)
            self.assertEqual([t["strategy"] for t in manifest["trials"]], ["A", "B", "PPO", "B", "PPO", "A", "PPO", "A", "B"])
            for trial in manifest["trials"]:
                request = json.loads((output / (trial["run"] + ".json")).read_text(encoding="utf-8"))
                self.assertEqual(request["intent"], mission["intent"])
                self.assertEqual(request["instruction"], mission["instruction"])
                self.assertEqual(request["seed"], trial["seed"])
            # A summary without successful process cleanup is not a valid flight.
            run = output / manifest["trials"][0]["run"]
            run.mkdir()
            batch.write_json(run / "summary.json", dict(reason="success", failure=None, metrics=dict(seconds=40, coverage=.8, found=6)))
            rows = batch.summarize(output)
            self.assertFalse(rows[0]["valid"])
            batch.write_json(run / "launcher-result.json", dict(exitCode=0))
            rows = batch.summarize(output)
            self.assertTrue(rows[0]["success"])
            self.assertFalse(rows[1]["valid"])
            self.assertTrue((output / "episodes.csv").exists())


if __name__ == "__main__":
    unittest.main()
