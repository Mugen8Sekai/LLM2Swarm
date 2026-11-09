"""Archive a pilot summary and matched throughput measurements without episode logs."""
import json
import shutil
import statistics
import sys
from pathlib import Path

source = Path(sys.argv[1])
destination = Path(__file__).parent / "pilot-20260917"
destination.mkdir(exist_ok=True)
for name in ("summary.json", "config.json"):
    shutil.copyfile(source / name, destination / name)
measurements = {}
for name in ("core", "adapter"):
    path = source.parent / f"{name}-throughput.json"
    measurements[name] = json.loads(path.read_text())
    shutil.copyfile(path, destination / path.name)
assert measurements["core"]["terminalMetricsFirstEpisode"] == measurements["adapter"]["terminalMetricsFirstEpisode"]
summary = json.loads((source / "summary.json").read_text())
rows = ["# Piloto de baselines — 17 septiembre 2026", "",
        "9.600 episodios, 48 condiciones × 100 semillas × 2 estrategias. Solo validación; ningún entrenamiento.", "",
        "A: barrido global repartido en bloques. B: franjas compactas coordinadas. Tiempo restringido = tiempo hasta K o plazo si falla. Δ = B − A; positivo favorece A. IC del 95 % t aproximado, emparejado por semilla, sin corrección por comparaciones múltiples; análisis exploratorio.", "",
        "| N | Distribución | Inicio | K | Plazo | Éxito A/B | Tiempo A/B (s) | Δ [IC95] (s) |",
        "|---|---|---|---|---|---|---|---|"]
for pair in summary["paired"]:
    condition = pair["condition"]
    match = [r for r in summary["summary"] if all(r["condition"][k] == v for k, v in condition.items())]
    a, b = sorted(match, key=lambda r: r["condition"]["strategy"])
    rows.append(f'| {condition["targetCount"]} | {condition["distribution"]} | {condition["layout"]} | {condition["k"]} | {condition["deadline"]} | {a["successRate"]:.0%}/{b["successRate"]:.0%} | {a["meanRestrictedTime"]:.2f}/{b["meanRestrictedTime"]:.2f} | {pair["restrictedTimeBminusA"]:.2f} [{pair["approximate95Lower"]:.2f}, {pair["approximate95Upper"]:.2f}] |')
rows += ["", "## Rendimiento", "", "Windows, AMD Ryzen 7 9800X3D, .NET SDK 8.0.318 Release, Python 3.12.14. Cinco episodios de calentamiento, cinco repeticiones de 100 episodios. Cada repetición: 11.800 transiciones; una transición equivale a 1 s simulado / 10 pasos físicos. Proceso único, sin render, inferencia ni aprendizaje. Incluye reset, observaciones, despachador y métricas; Python añade serialización e IPC. Variación por JIT y carga del equipo; no es una predicción de velocidad de entrenamiento.", ""]
for name, result in measurements.items():
    rates = [r["transitionsPerSecond"] for r in result["repeats"]]
    rows.append(f'- {name}: mediana {statistics.median(rates):,.0f} transiciones/s; rango {min(rates):,.0f}–{max(rates):,.0f}.')
rows += ["", "Métricas terminales del primer episodio idénticas en ambos canales (comparación exacta del JSON decodificado).", "", "## Verificación y lectura", "", "13/13 pruebas EditMode de Unity aprobadas, más comprobaciones del ejecutable C#: mundos ocultos contrafactuales antes de detectar, observaciones sin semilla/total/distribución/posiciones ocultas, aislamiento de snapshots y escenario, acciones inválidas atómicas, inicios emparejados, velocidad sin teletransporte, sensores fuera de la región asignada, repetición determinista, contabilidad de métricas, búsqueda exhaustiva de ambos baselines, plazo exacto y rejilla alternativa 3×2.", "", "B no demuestra superioridad general. Por ejemplo, N=12, uniforme, inicio común, K=12 y plazo 120: A tarda 61,77 s frente a 65,79 s de B; ambos alcanzan 100 % de éxito. No ajustar los baselines retrospectivamente para favorecer una conclusión. El throughput permite plantear un piloto RL, pero no determina su algoritmo, recompensa ni presupuesto. La fase 4 sigue pendiente de autorización.", "", f"Datos de episodios locales: `{source.as_posix()}`. Configuración, huella SHA256 del código y resumen completo archivados junto a este informe. Los logs grandes se regeneran con `pilot`. La primera ejecución anterior a la mezcla de semillas se descartó por correlación espacial del LCG."]
(destination / "REPORT.md").write_text("\n".join(rows) + "\n", encoding="utf-8")
print(destination / "REPORT.md")
