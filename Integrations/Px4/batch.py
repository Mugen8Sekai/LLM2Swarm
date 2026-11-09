"""Sequential paired PX4 trials. Standard library only; never trains a policy."""
import argparse
import csv
import hashlib
import json
import pathlib
import subprocess
import sys
from datetime import datetime, timezone
from paired_analysis import analyze

ROOT = pathlib.Path(__file__).resolve().parent
STRATEGIES = {1: "A", 2: "B", 3: "PPO"}


def provenance():
    repo = ROOT.parent.parent
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    paths = subprocess.check_output(["git", "ls-files", "Assets/Swarm", "Integrations/Px4", "Experiments/results/ppo/full/47/policy.json"], cwd=repo, text=True).splitlines()
    hashes = {name: hashlib.sha256((repo / name).read_bytes()).hexdigest() for name in paths
              if pathlib.Path(name).suffix in {".cs", ".csproj", ".py", ".ps1", ".cpp", ".json"}}
    return dict(commit=commit, sha256=hashes)


def write_json(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")
    temporary.replace(path)


def summarize(directory):
    manifest = json.loads((directory / "batch.json").read_text(encoding="utf-8"))
    rows = []
    for trial in manifest["trials"]:
        run = directory / trial["run"]
        report_path = run / "summary.json"
        report = json.loads(report_path.read_text(encoding="utf-8-sig")) if report_path.exists() else {}
        launcher_path = run / "launcher-result.json"
        launcher = json.loads(launcher_path.read_text(encoding="utf-8-sig")) if launcher_path.exists() else {}
        metrics = report.get("metrics", {})
        valid = bool(report) and not report.get("failure") and launcher.get("exitCode") == 0
        rows.append(dict(seed=trial["seed"], repeat=trial["repeat"], strategy=trial["strategy"],
                         valid=valid, success=valid and report.get("reason") == "success",
                         reason=report.get("reason", "missing_result"),
                         seconds=metrics.get("seconds"), first_finding=metrics.get("firstFindingSeconds"),
                         kth_finding=metrics.get("kFindingSeconds"), coverage=metrics.get("coverage"),
                         distance=metrics.get("totalDistance"), found=metrics.get("found"),
                         failure=report.get("failure") or trial.get("error", ""), run=trial["run"]))
    with (directory / "episodes.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    lines = ["# Comparativa PX4", "", "Misma intención y escenario por semilla; orden rotado por bloque.",
             "Cobertura medida al finalizar: depende del momento de parada.",
             "Los fallos de infraestructura se conservan y no cuentan como vuelos válidos.", "",
             "| Método | Válidos / previstos | Éxitos | Tiempo medio en éxitos (s) | Cobertura media en válidos |",
             "|---|---:|---:|---:|---:|"]
    for name in STRATEGIES.values():
        group = [r for r in rows if r["strategy"] == name]
        valid = [r for r in group if r["valid"]]
        successful = [r for r in valid if r["success"]]
        mean_time = sum(r["seconds"] for r in successful) / len(successful) if successful else None
        coverage = sum(r["coverage"] for r in valid) / len(valid) if valid else None
        lines.append(f"| {name} | {len(valid)} / {len(group)} | {len(successful)} | "
                     f"{mean_time if mean_time is not None else '—'} | {coverage if coverage is not None else '—'} |")
    lines += ["", "Los tiempos medios solo entre éxitos no bastan para ordenar métodos con distinta tasa de éxito.",
              "Ver episodes.csv para comparar cada par con la misma semilla y repetición."]
    (directory / "REPORT.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    analyze(directory, rows, manifest)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mission", type=pathlib.Path, help="prepared-*.json exportado por Unity; se interpreta una sola vez")
    parser.add_argument("--seeds", type=int, nargs="+", default=[100000, 100001, 100002])
    parser.add_argument("--repeats", type=int, default=1)
    parser.add_argument("--output", type=pathlib.Path)
    parser.add_argument("--execute", action="store_true", help="Sin esta opción solo genera el lote")
    parser.add_argument("--summarize", type=pathlib.Path)
    args = parser.parse_args()
    if args.summarize:
        summarize(args.summarize.resolve())
        return
    if not args.mission:
        candidates = sorted((ROOT / ".runtime").glob("prepared-*.json"), key=lambda p: p.stat().st_mtime)
        if not candidates:
            parser.error("No hay misión preparada exportada por Unity; usa --mission archivo.json")
        args.mission = candidates[-1]
    print(f"Misión reutilizada: {args.mission}", flush=True)
    if args.repeats < 1 or not args.seeds or any(s < 100000 or s >= 180000 for s in args.seeds):
        parser.error("Usa semillas de validación 100000–179999 y repeats >= 1; TEST permanece reservado")
    source = args.mission.read_bytes()
    mission = json.loads(source.decode("utf-8-sig"))
    intent = mission["intent"]
    if intent["areas"] != ["all"] or not 1 <= intent["requiredFindings"] <= 10000 or not 0 < intent["maxSeconds"] <= 86400:
        parser.error("Intención no admitida por PX4")
    if args.execute:
        launch = ROOT / ".runtime" / "mission-control-launch.json"
        if launch.exists():
            pid = json.loads(launch.read_text(encoding="utf-8-sig"))["pid"]
            check = subprocess.run(["powershell.exe", "-NoProfile", "-Command", f"if(Get-Process -Id {int(pid)} -ErrorAction SilentlyContinue){{exit 1}}"], check=False)
            if check.returncode:
                parser.error("Hay una misión de Unity activa; espera a que termine")
    directory = (args.output or ROOT / ".runtime" / ("batch-" + datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S-%f"))).resolve()
    directory.mkdir(parents=True, exist_ok=False)
    (directory / "source-mission.json").write_bytes(source)
    manifest = dict(schemaVersion=1, createdUtc=datetime.now(timezone.utc).isoformat(),
                    sourceSha256=hashlib.sha256(source).hexdigest(), provenance=provenance(), trials=[])
    block = 0
    for seed in args.seeds:
        for repeat in range(args.repeats):
            order = [1, 2, 3]
            shift = block % 3
            order = order[shift:] + order[:shift]
            block += 1
            for strategy in order:
                name = f"slice-{seed}-{repeat}-{STRATEGIES[strategy]}"
                request = dict(mission, seed=seed, strategy=strategy)
                request_path = directory / (name + ".json")
                write_json(request_path, request)
                manifest["trials"].append(dict(seed=seed, repeat=repeat, strategy=STRATEGIES[strategy], run=name))
    write_json(directory / "batch.json", manifest)
    print(f"Lote: {directory} ({len(manifest['trials'])} vuelos)", flush=True)
    if not args.execute:
        print("Plan generado; no se ha iniciado Gazebo. Usa --execute con un output nuevo para ejecutar.")
        return
    try:
        for trial in manifest["trials"]:
            if provenance()!=manifest["provenance"]:
                raise RuntimeError("El código o la política han cambiado; lote detenido para conservar la versión fijada")
            print(f"Ejecutando {trial['run']}", flush=True)
            command = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(ROOT / "Run.ps1"),
                       "-MissionFile", str(directory / (trial["run"] + ".json")), "-OutputDirectory", str(directory / trial["run"])]
            with (directory / (trial["run"] + ".log")).open("w", encoding="utf-8") as log:
                result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=False)
            trial["exitCode"] = result.returncode
            write_json(directory / "batch.json", manifest)
            summarize(directory)
            if result.returncode:
                raise RuntimeError(f"Fallo de infraestructura en {trial['run']}; lote detenido. Consulta su log.")
    finally:
        summarize(directory)


if __name__ == "__main__":
    main()
