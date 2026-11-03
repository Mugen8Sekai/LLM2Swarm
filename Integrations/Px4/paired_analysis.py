"""Exploratory paired bootstrap over scenarios, not individual drones or ticks."""
import json
import random


def interval(values, samples=20000):
    rng = random.Random(20261103)
    n = len(values)
    means = sorted(sum(values[rng.randrange(n)] for _ in range(n)) / n for _ in range(samples))
    return [means[int(.025 * samples)], means[int(.975 * samples)]]


def analyze(directory, rows, manifest):
    source = json.loads((directory / "source-mission.json").read_text(encoding="utf-8-sig"))
    deadline = source["intent"]["maxSeconds"]
    comparisons = []
    for baseline in ("A", "B"):
        differences = {"restrictedSeconds": [], "distance": [], "coverage": [], "success": []}
        # Repeats, when present, are averaged within seed before resampling seeds.
        grouped = {}
        for row in rows:
            grouped.setdefault((row["seed"], row["repeat"]), {})[row["strategy"]] = row
        by_seed = {}
        for (seed, repeat), pair in grouped.items():
            if any(name not in pair or not pair[name]["valid"] for name in (baseline, "PPO")):
                continue
            a, p = pair[baseline], pair["PPO"]
            values = dict(restrictedSeconds=(p["kth_finding"] if p["success"] else deadline) - (a["kth_finding"] if a["success"] else deadline),
                          distance=p["distance"] - a["distance"], coverage=p["coverage"] - a["coverage"],
                          success=int(p["success"]) - int(a["success"]))
            by_seed.setdefault(seed, []).append(values)
        for repeats in by_seed.values():
            for metric in differences:
                differences[metric].append(sum(v[metric] for v in repeats) / len(repeats))
        metrics = {metric: dict(meanDifference=sum(values) / len(values), bootstrap95=interval(values))
                   for metric, values in differences.items() if values}
        comparisons.append(dict(comparison=f"PPO - {baseline}", pairedSeeds=len(by_seed), metrics=metrics))
    result = dict(method="paired percentile bootstrap by seed; 20000 draws; rng 20261103", comparisons=comparisons)
    (directory / "paired-analysis.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    lines = ["# Análisis emparejado", "", "Diferencias PPO menos baseline. Valores negativos de tiempo/distancia favorecen a PPO.",
             "Tiempo restringido: tiempo al K-ésimo hallazgo si hay éxito, plazo completo si falla la búsqueda.",
             "Solo entran pares con ambos vuelos técnicamente válidos; no se eliminan fallos de búsqueda.",
             "IC exploratorios del 95 % por bootstrap de semillas (20 000 muestras), sin corrección múltiple.",
             "Tres o pocas semillas no permiten una inferencia robusta; consultar siempre tamaño y resultados por escenario.", "",
             "| Comparación | Semillas emparejadas | Métrica | Diferencia media | IC 95 % |", "|---|---:|---|---:|---|"]
    for comparison in comparisons:
        for metric, values in comparison["metrics"].items():
            lo, hi = values["bootstrap95"]
            lines.append(f"| {comparison['comparison']} | {comparison['pairedSeeds']} | {metric} | {values['meanDifference']:.4f} | [{lo:.4f}, {hi:.4f}] |")
    (directory / "PAIRED.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
