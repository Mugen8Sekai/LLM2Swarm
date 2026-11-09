# Piloto de baselines — 17 septiembre 2026

9.600 episodios, 48 condiciones × 100 semillas × 2 estrategias. Solo validación; ningún entrenamiento.

A: barrido global repartido en bloques. B: franjas compactas coordinadas. Tiempo restringido = tiempo hasta K o plazo si falla. Δ = B − A; positivo favorece A. IC del 95 % t aproximado, emparejado por semilla, sin corrección por comparaciones múltiples; análisis exploratorio.

| N | Distribución | Inicio | K | Plazo | Éxito A/B | Tiempo A/B (s) | Δ [IC95] (s) |
|---|---|---|---|---|---|---|---|
| 6 | uniform | common | 3 | 60 | 100%/99% | 27.01/31.27 | 4.26 [1.25, 7.28] |
| 6 | uniform | common | 3 | 120 | 100%/100% | 27.01/31.29 | 4.28 [1.26, 7.30] |
| 6 | uniform | common | 6 | 60 | 52%/41% | 54.38/55.97 | 1.59 [-0.36, 3.54] |
| 6 | uniform | common | 6 | 120 | 100%/100% | 56.57/60.48 | 3.91 [1.64, 6.19] |
| 6 | uniform | dispersed | 3 | 60 | 100%/100% | 24.75/29.86 | 5.11 [2.66, 7.57] |
| 6 | uniform | dispersed | 3 | 120 | 100%/100% | 24.75/29.86 | 5.11 [2.66, 7.57] |
| 6 | uniform | dispersed | 6 | 60 | 60%/45% | 52.23/55.36 | 3.12 [0.89, 5.35] |
| 6 | uniform | dispersed | 6 | 120 | 100%/100% | 54.47/58.65 | 4.18 [1.64, 6.71] |
| 6 | clustered | common | 3 | 60 | 98%/95% | 28.13/32.61 | 4.48 [1.17, 7.80] |
| 6 | clustered | common | 3 | 120 | 100%/100% | 28.15/32.75 | 4.61 [1.26, 7.95] |
| 6 | clustered | common | 6 | 60 | 67%/48% | 50.37/52.71 | 2.34 [-0.10, 4.77] |
| 6 | clustered | common | 6 | 120 | 100%/100% | 51.98/56.42 | 4.44 [1.74, 7.14] |
| 6 | clustered | dispersed | 3 | 60 | 100%/96% | 26.43/31.15 | 4.72 [1.69, 7.74] |
| 6 | clustered | dispersed | 3 | 120 | 100%/100% | 26.43/31.19 | 4.76 [1.73, 7.79] |
| 6 | clustered | dispersed | 6 | 60 | 66%/53% | 50.13/52.00 | 1.87 [-0.59, 4.33] |
| 6 | clustered | dispersed | 6 | 120 | 100%/100% | 52.01/54.54 | 2.53 [-0.21, 5.28] |
| 12 | uniform | common | 3 | 60 | 100%/100% | 13.31/14.33 | 1.02 [-1.00, 3.05] |
| 12 | uniform | common | 3 | 120 | 100%/100% | 13.31/14.33 | 1.02 [-1.00, 3.05] |
| 12 | uniform | common | 12 | 60 | 32%/16% | 58.28/59.15 | 0.87 [0.09, 1.66] |
| 12 | uniform | common | 12 | 120 | 100%/100% | 61.77/65.79 | 4.03 [2.87, 5.18] |
| 12 | uniform | dispersed | 3 | 60 | 100%/100% | 10.51/13.63 | 3.12 [1.54, 4.70] |
| 12 | uniform | dispersed | 3 | 120 | 100%/100% | 10.51/13.63 | 3.12 [1.54, 4.70] |
| 12 | uniform | dispersed | 12 | 60 | 33%/27% | 57.73/58.84 | 1.11 [0.10, 2.12] |
| 12 | uniform | dispersed | 12 | 120 | 100%/100% | 61.39/63.25 | 1.86 [0.43, 3.29] |
| 12 | clustered | common | 3 | 60 | 99%/100% | 16.93/18.32 | 1.39 [-1.98, 4.77] |
| 12 | clustered | common | 3 | 120 | 100%/100% | 16.96/18.32 | 1.36 [-2.03, 4.75] |
| 12 | clustered | common | 12 | 60 | 39%/43% | 54.47/55.33 | 0.86 [-1.48, 3.20] |
| 12 | clustered | common | 12 | 120 | 100%/100% | 57.63/60.00 | 2.38 [-0.40, 5.15] |
| 12 | clustered | dispersed | 3 | 60 | 100%/100% | 12.75/16.91 | 4.16 [1.99, 6.33] |
| 12 | clustered | dispersed | 3 | 120 | 100%/100% | 12.75/16.91 | 4.16 [1.99, 6.33] |
| 12 | clustered | dispersed | 12 | 60 | 45%/48% | 54.96/54.53 | -0.43 [-2.17, 1.31] |
| 12 | clustered | dispersed | 12 | 120 | 100%/100% | 58.09/57.83 | -0.26 [-2.48, 1.96] |
| 24 | uniform | common | 3 | 60 | 100%/100% | 6.40/6.89 | 0.49 [-0.45, 1.43] |
| 24 | uniform | common | 3 | 120 | 100%/100% | 6.40/6.89 | 0.49 [-0.45, 1.43] |
| 24 | uniform | common | 24 | 60 | 10%/2% | 59.68/59.97 | 0.29 [0.05, 0.52] |
| 24 | uniform | common | 24 | 120 | 100%/100% | 65.05/69.23 | 4.19 [3.53, 4.84] |
| 24 | uniform | dispersed | 3 | 60 | 100%/100% | 4.15/6.14 | 1.99 [1.26, 2.72] |
| 24 | uniform | dispersed | 3 | 120 | 100%/100% | 4.15/6.14 | 1.99 [1.26, 2.72] |
| 24 | uniform | dispersed | 24 | 60 | 12%/4% | 59.58/59.95 | 0.37 [0.11, 0.64] |
| 24 | uniform | dispersed | 24 | 120 | 100%/100% | 65.28/66.39 | 1.11 [0.35, 1.88] |
| 24 | clustered | common | 3 | 60 | 100%/100% | 10.00/11.46 | 1.47 [-0.93, 3.87] |
| 24 | clustered | common | 3 | 120 | 100%/100% | 10.00/11.46 | 1.47 [-0.93, 3.87] |
| 24 | clustered | common | 24 | 60 | 42%/33% | 56.19/56.19 | 0.00 [-1.90, 1.90] |
| 24 | clustered | common | 24 | 120 | 100%/100% | 59.30/61.53 | 2.23 [-0.24, 4.71] |
| 24 | clustered | dispersed | 3 | 60 | 100%/100% | 7.39/10.11 | 2.72 [0.64, 4.80] |
| 24 | clustered | dispersed | 3 | 120 | 100%/100% | 7.39/10.11 | 2.72 [0.64, 4.80] |
| 24 | clustered | dispersed | 24 | 60 | 37%/35% | 56.82/55.54 | -1.28 [-3.15, 0.59] |
| 24 | clustered | dispersed | 24 | 120 | 100%/100% | 60.25/59.89 | -0.36 [-2.76, 2.04] |

## Rendimiento

Windows, AMD Ryzen 7 9800X3D, .NET SDK 8.0.318 Release, Python 3.12.14. Cinco episodios de calentamiento, cinco repeticiones de 100 episodios. Cada repetición: 11.800 transiciones; una transición equivale a 1 s simulado / 10 pasos físicos. Proceso único, sin render, inferencia ni aprendizaje. Incluye reset, observaciones, despachador y métricas; Python añade serialización e IPC. Variación por JIT y carga del equipo; no es una predicción de velocidad de entrenamiento.

- core: mediana 47,502 transiciones/s; rango 20,139–53,482.
- adapter: mediana 10,270 transiciones/s; rango 6,795–16,266.

Métricas terminales del primer episodio idénticas en ambos canales (comparación exacta del JSON decodificado).

## Verificación y lectura

13/13 pruebas EditMode de Unity aprobadas, más comprobaciones del ejecutable C#: mundos ocultos contrafactuales antes de detectar, observaciones sin semilla/total/distribución/posiciones ocultas, aislamiento de snapshots y escenario, acciones inválidas atómicas, inicios emparejados, velocidad sin teletransporte, sensores fuera de la región asignada, repetición determinista, contabilidad de métricas, búsqueda exhaustiva de ambos baselines, plazo exacto y rejilla alternativa 3×2.

B no demuestra superioridad general. Por ejemplo, N=12, uniforme, inicio común, K=12 y plazo 120: A tarda 61,77 s frente a 65,79 s de B; ambos alcanzan 100 % de éxito. No ajustar los baselines retrospectivamente para favorecer una conclusión. El throughput permite plantear un piloto RL, pero no determina su algoritmo, recompensa ni presupuesto. La fase 4 sigue pendiente de autorización.

Datos de episodios locales: `Experiments/results/20260917-063519`. Configuración, huella SHA256 del código y resumen completo archivados junto a este informe. Los logs grandes se regeneran con `pilot`. La primera ejecución anterior a la mezcla de semillas se descartó por correlación espacial del LCG.
