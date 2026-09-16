# LLM2Swarm — primera simulación 2D

Proyecto Unity **6000.6.0f1**, plantilla URP 2D. Abrir `Assets/Scenes/SampleScene.unity` y pulsar Play. Se puede iniciar la misión manual o escribir una instrucción y pulsar **Interpretar instrucción → Validar y preparar → Iniciar**. El modelo local Ministral 3B está integrado mediante un proceso auxiliar de llama.cpp, sin servicios externos ni claves. Consultar [instalación y evaluación local](LocalInference/README.md). `Assets/Swarm/ManualSweep.json` conserva el ejemplo manual inicial.

## Misión manual

La interfaz separa el mapa (izquierda) del panel de misión (derecha). Bajo el mapa están **Iniciar / Pausar / Continuar** en verde, **Reiniciar** en naranja e **Interrumpir** en rojo. Arriba a la derecha hay una instrucción multilínea. La interpretación local es asíncrona y se puede cancelar; tiene un límite de 120 segundos. Debajo se muestran la respuesta del modelo, el resumen calculado por código y la misión activa en tiempo real. Preparar y ejecutar siguen siendo acciones explícitas del usuario.

**Detalles técnicos · JSON** está plegado por defecto. Permite revisar tanto respuestas estructuradas como misiones manuales con rutas. **Validar y preparar** sustituye la misión activa sin iniciarla y se deshabilita mientras está en marcha. Editar la instrucción invalida su propuesta del modelo; editar el JSON invalida cualquier propuesta revisada. **Guardar activa** guarda el plan preparado, no el borrador. La lectura estricta con Newtonsoft JSON rechaza campos desconocidos, duplicados y valores no admitidos. Validar una misión estructurada no certifica que cumpla una instrucción en lenguaje natural. Se recomienda una vista Game de al menos 1060 × 700.

El ejemplo usa tres drones homogéneos: región de 60 × 40 m, velocidad de 4 m/s y sensor ideal de 3 m. No se modelan obstáculos, colisiones, batería ni ruido. `Scenario` conserva la configuración del mundo; `MissionIntent` describe la intención; `MissionPlanner` genera el plan ejecutable `Mission` (nombre conservado por compatibilidad con el prototipo). El LLM no genera rutas, no cambia los objetivos existentes y no recibe sus posiciones.

La búsqueda puede abarcar `all` o una secuencia sin repeticiones de `north`, `center` y `south`, que son tercios horizontales. Cada fase reparte su área entre los tres drones con barridos. El gestor espera a que todos terminen sus rutas antes de avanzar; los drones no se teletransportan entre fases. El sensor solo registra hallazgos y cobertura dentro de la zona activa, incluso si su radio alcanza una zona posterior: es una política de búsqueda dirigida, no un sensor que observa todo simultáneamente. Las restricciones de tránsito y los umbrales de cobertura aún no se admiten. El planificador es una referencia provisional y no óptima.

**Guardar activa** escribe `manual-mission.json` en `Application.persistentDataPath` (la ruta aparece en pantalla). Editar allí dimensiones, semilla, objetivos, velocidad, sensor, resolución, límite temporal y las tres listas `routes[].waypoints` de puntos `{ "x": 2, "y": 2 }`. **Cargar borrador**, **Revisar propuesta JSON** y **Validar y preparar** permiten cargarla sin iniciarla automáticamente. El primer waypoint es la posición inicial. Las rutas son independientes de los objetivos ocultos; no hay reasignación automática.

**Proponer barrido** genera el ejemplo con la semilla indicada para prepararlo después. **Reiniciar** reproduce la misión actual desde cero; pausar no consume tiempo simulado. Verde indica celdas exploradas, puntos de color indican waypoints y drones, y blanco indica objetivos encontrados. Los objetivos no detectados no se dibujan.

## Reproducibilidad y registros

El generador LCG tiene algoritmo explícito y utiliza únicamente la semilla. El motor avanza en pasos de 0,1 s por defecto, independientemente del framerate. Una misma configuración reproduce objetivos, trayectorias y hallazgos en el mismo entorno de ejecución. No se garantiza igualdad binaria entre plataformas de coma flotante distintas.

La cobertura es una aproximación por celdas: cuenta el área de cada celda cuyo centro ha entrado en el radio de algún sensor, sin duplicados. Las celdas de borde se ponderan por su área real. Tanto cobertura como detección usan la distancia al segmento recorrido para no perder objetivos entre pasos. El hallazgo se registra al final del paso correspondiente (resolución temporal: `stepSeconds`); los detectados en la posición inicial tienen tiempo cero. Empates se asignan al dron de menor índice.

La condición explícita de éxito es encontrar `requiredFindings` objetivos distintos (12 por defecto). Se distinguen `success`, `search_exhausted` (rutas agotadas sin cumplirla), `time_limit` e `interrupted`. El límite temporal se redondea al siguiente paso. Al terminar se exporta automáticamente; **Exportar registro** guarda también una instantánea parcial. Cada exportación crea una carpeta única en `Application.persistentDataPath/Runs`; su nombre UTC identifica la ejecución, no mide su duración, con:

- `mission.json`: configuración completa y rutas para repetir el experimento.
- `metrics.csv`: tick, segundos simulados, fracción de cobertura y hallazgos acumulados, incluido el estado inicial.
- `findings.csv`: identificadores de objetivo/dron (base cero), tick, tiempo y posición de cada primer hallazgo.
- `summary.json`: estado final (o `snapshot`), tiempo, cobertura y objetivos encontrados/totales.
- `scenario.json`, `intent.json`, `instruction.txt`: escenario, intención confirmada y texto que la originó (vacío/null en misiones manuales).
- `phases.json`: cambios de fase con tick y tiempo simulado.
- `interpretation-evidence.txt`: carpeta de interpretación con respuesta cruda, prompt, esquema, configuración del motor y tiempos reales.

Las métricas de búsqueda usan tiempo simulado. La latencia de interpretación usa tiempo real y se guarda aparte. Pedir más hallazgos que objetivos existentes es válido y permite medir búsqueda agotada. Se conservan las métricas en memoria; para experimentos largos conviene añadir escritura incremental.

## Validación

`dotnet run --project Tests/SimulationChecks` ejecuta comprobaciones sin iniciar Unity (SDK .NET 8; requiere que Unity haya resuelto Newtonsoft en `Library/PackageCache`). Incluye repetición determinista, cobertura monotónica, límites espaciales, hallazgos, finalización, tiempo máximo, validación de rutas, detección durante desplazamientos largos y rechazo de campos JSON desconocidos/duplicados.

El núcleo está en `Assets/Swarm/Simulation.cs`; la presentación, controles y exportación están en `Assets/Swarm/SimulationRunner.cs`.

También hay pruebas EditMode en `Assets/Swarm/Tests`, ejecutables desde Test Runner de Unity. La última ejecución pasó **9/9**, incluyendo la inferencia local y cancelación. Para configurar desde el Inspector, añadir `SimulationRunner` a un GameObject y asignar un TextAsset JSON al campo `Mission Json`; el arranque automático detecta ese componente. El barrido por sectores es una referencia provisional, no óptima ni definitiva para el TFM. El plan se construye sin consultar posiciones de objetivos; estas son privadas del simulador y la vista consume únicamente hallazgos compartidos. La altura se asume constante y no interviene en la geometría 2D.
