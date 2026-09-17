# Fase 4: piloto PPO centralizado

Alcance autorizado: comprobar viabilidad, estabilidad, adaptación y comportamiento de la recompensa. No ajustar A/B ni optimizar contra TEST. Configuración previa a la primera ejecución en `config.json`. El resultado final se publica en `report/REPORT.md`.

El archivo `frozen.json` verifica byte a byte las fuentes del entorno, baselines, métricas, adaptador original y resultados anteriores. Esta fase añade código en `Experiments/ppo`; no modifica esas fuentes. El actor C# exportado implementa `ISearchStrategy` en `PolicyHost/ExportedPolicyStrategy.cs`, sin depender de PPO para la definición del contrato. Python replica Reset/Decide para entrenamiento y se comprueba contra el actor C#.

## Formulación fijada

- Un coordinador, actor y crítico compartiendo MLP 128–128 con tanh, salida de 3×25 logits y valor escalar. Entrada: 167 características (11 globales, 3×28 por UAV y 24×3 por región), exclusivamente derivadas de `SearchObservation`. Normalización fija, sin estadísticas de escenarios ocultos. K y plazo están representados explícitamente; no se recortan sus valores. El crítico tampoco recibe información privilegiada.
- Cada UAV elige 24 regiones o espera. Busy fuerza espera; regiones completadas/reservadas se enmascaran. Se muestrean UAV 0/1/2 en orden, excluyendo elecciones anteriores; la log-probabilidad conjunta suma las tres probabilidades condicionadas por esas máscaras. Al optimizar se reconstruye exactamente la misma exclusión. Esperar permanece permitido para un UAV libre. Orden fijo y máscaras son parte de la formulación, no evidencia de cooperación aprendida.
- Rejilla configurable por ejecución. La dimensión de la red depende de ella: cambiar 6×4 exige crear otro checkpoint; no se afirma generalización a otra rejilla. Prueba del contrato con 3×2. Física congelada: 60×40 m, 3 UAV a 4 m/s, sensor 3 m, celdas de cobertura 1 m, paso 0,1 s, decisión 1 s. El adaptador de recompensa presupone esa resolución congelada de 1 m porque `cellSize` no pertenece a `SearchObservation`; no sirve sin revisión para otros tamaños de celda.
- Recompensa exacta: Δmin(F,K)/K − 0,1 Δt/T + 0,01 ΔA/A. Se registra cada sumando por episodio; se muestran curvas por update. Cobertura global reconstruida a partir de cobertura regional y áreas de celdas públicas, con coincidencia verificada con la métrica terminal. El motor no proporciona recompensa. No hay bonus terminal, penalización de distancia, castigo a esperas ni otros términos.
- PPO clip 0,2, Adam lr 3e-4 y eps 1e-5, gamma 0,99, GAE λ 0,95, 4 épocas, minibatch 256, entropía 0,01, coeficiente value 0,5 aplicado a `0,5*MSE`, grad norm máximo 0,5, parada de épocas por KL aproximada >0,03. Ventajas normalizadas por rollout, sin normalizar retorno. No hay scheduler. Deadline es terminal de horizonte finito representado en la observación: no se bootstrappea tras éxito/plazo/agotamiento; sí al cortar un rollout no terminal.
- Ocho procesos C# persistentes, 128 pasos por rollout = 1.024 transiciones/update. 256 updates = 262.144 transiciones por semilla, seeds de inicialización 11/29/47. CPU, un hilo Torch, algoritmos deterministas. Sin CUDA, Gym, SB3, MARL ni recurrentes. Implementación de [PPO clipped](https://arxiv.org/abs/1707.06347) con [PyTorch CPU](https://pytorch.org/get-started/locally/).

## Escenarios y selección

TRAIN: semillas muestreadas uniformemente de 0–79999, N de 6/12/24, uniforme/clustered, inicio común/disperso, K de 3/N y plazo de 60/120; cada factor equiprobable. Solo el driver de muestreo ve el descriptor. El actor recibe únicamente el snapshot. La correlación K=N en algunas condiciones pertenece al diseño aprobado; no equivale a exponer el campo de total real.

Se evalúa el checkpoint inicial y cada 65.536 transiciones en 96 escenarios fijos: 48 condiciones × semillas VALIDATION 100100 y 100101. Selección lexicográfica: mayor éxito, menor tiempo restringido, checkpoint anterior en empate. Se reinician los episodios TRAIN después de cada evaluación; los incompletos se descartan del resumen de episodios, pero sus transiciones ya utilizadas cuentan en el presupuesto. No se unen ventajas a través de resets.

El checkpoint seleccionado se compara con los registros originales de A/B sobre sus 4.800 escenarios exactos: 48×semillas 100000–100099. Las misiones y los campos del descriptor coinciden; se reproducen 96 episodios A/B mediante el código congelado. No se selecciona otro checkpoint con esa comparación. Toda esta evaluación sigue siendo VALIDATION, no un test final independiente. Nunca se generan ni leen semillas TEST.

Puerta fijada para la ablación: las tres semillas seleccionadas mejoran el éxito sobre sus inicializaciones y la mejora media alcanza 5 puntos porcentuales, sin NaN/acciones inválidas. Cumplida: +3,125/+4,167/+18,75 pp (media +8,681 pp). Se repite el mismo presupuesto/configuración/seeds con coeficiente de cobertura 0, sin otro cambio. No se amplía el presupuesto por perder contra A/B.

## Reproducción

Desde la raíz, Python 3.12 y .NET 8 (Unity debe haber resuelto Newtonsoft):

```powershell
python -m venv Experiments/.venv
Experiments/.venv/Scripts/python.exe -m pip install torch==2.6.0 --index-url https://download.pytorch.org/whl/cpu
Experiments/.venv/Scripts/python.exe -m pip install numpy==2.2.6 matplotlib==3.10.3 psutil==7.0.0
dotnet build Tests/SimulationChecks -c Release
dotnet build Experiments/ppo/PolicyHost -c Release
Experiments/.venv/Scripts/python.exe Experiments/ppo/test_pilot.py
Experiments/.venv/Scripts/python.exe Experiments/ppo/run.py train --seed 11
Experiments/.venv/Scripts/python.exe Experiments/ppo/run.py paired --seed 11
Experiments/.venv/Scripts/python.exe Experiments/ppo/verify_export.py 11
# Repetir train/paired/verify_export para 29 y 47.
# Solo tras comprobar la puerta de ablación:
Experiments/.venv/Scripts/python.exe Experiments/ppo/run.py train --seed 11 --no-coverage
Experiments/.venv/Scripts/python.exe Experiments/ppo/run.py paired --seed 11 --no-coverage
# Repetir la ablación para 29 y 47.
Experiments/.venv/Scripts/python.exe Experiments/ppo/analyze.py
Experiments/.venv/Scripts/python.exe Experiments/ppo/write_report.py
```

Las rutas locales a dotnet/DLL están al principio de `run.py`; adaptarlas al ejecutar en otro equipo. Un `complete.json` existente evita reentrenar accidentalmente. Para una réplica conservar resultados originales y usar otro directorio `OUTPUT`. Dependencias exactas instaladas en `requirements-lock.txt`.

Checkpoints, logs por episodio y políticas JSON se conservan en `Experiments/results/ppo/{full,no-coverage}/{11,29,47}` (ignorados por Git); curvas, métricas resumidas, configuraciones y trayectorias representativas se archivan en `report`. `analyze.py` necesita los registros originales del piloto anterior; si faltan, regenerarlos con el código congelado y actualizar su ruta, comprobando la huella.

El tiempo ocioso diagnóstico cuenta intervalos completos de decisión con un UAV libre que espera; no mide las fracciones de segundo entre el fin de un barrido y la siguiente decisión. La fracción de espera voluntaria usa la disponibilidad después de las elecciones de los UAV anteriores. Se corrigió ese contador diagnóstico tras detectar que podía incluir esperas forzadas por exclusión conjunta; se repitió la evaluación, sin modificar aprendizaje, selección ni métricas físicas.

Las trayectorias guardan posiciones cada decisión, no cada paso físico. No se dibujan objetivos ocultos. Permutar hallazgos entre regiones con cobertura positiva es una intervención diagnóstica sobre la entrada, no un episodio físico ni una prueba causal definitiva de adaptación.
