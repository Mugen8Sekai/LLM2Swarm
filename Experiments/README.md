# Entorno experimental, fases 1–3

Estado: baselines y evaluación implementados. **No hay entrenamiento, recompensa ni dependencia de PPO.** La rejilla 6×4 es configurable, no una decisión metodológica definitiva. Resultados: [informe del piloto](pilot-20260917/REPORT.md).

`Assets/Swarm/SearchExperiment.cs` expone `ISearchStrategy.Reset/Decide` sobre snapshots `SearchObservation`. La acción conjunta contiene tres índices de región; -1 significa continuar/esperar. Solo se asignan drones libres a regiones no completadas ni reservadas. La validación es atómica. Ambos baselines utilizan el mismo ejecutor local de barrido y la misma física de `Simulation.cs`. A reparte un orden global serpenteante en tres bloques; B reparte franjas compactas a lo largo del lado mayor. Ambos minimizan el traslado inicial entre las permutaciones de bloques y sus orientaciones. Son referencias estáticas: no realizan aprendizaje ni redistribución dinámica de trabajo.

Las posiciones iniciales pertenecen al escenario y el desplazamiento hasta la primera ruta consume tiempo/distancia. El sensor experimental detecta durante todo el desplazamiento, incluso fuera de la macrorregión asignada. La primera formulación admite `areas: ["all"]`; las misiones por fases de la interfaz existente siguen siendo un flujo separado. No se ha añadido un selector de estrategias a la UI.

La estrategia recibe geometría, posiciones, disponibilidad, asignaciones, distancia pendiente, cobertura y hallazgos observados por región, K y tiempo restante. No recibe semilla, distribución, total real ni objetivos ocultos. El descriptor del escenario lo usa el evaluador para reset; no debe pasarse a una futura política. `Metrics()` es un canal de evaluación privilegiado, no una barrera de seguridad contra código malicioso. El adaptador entrega métricas privilegiadas únicamente al terminar; tampoco deben incorporarse a las observaciones de entrenamiento.

Métricas: éxito, razón de fin, tiempo al primer/K-ésimo hallazgo (nullable), fracción encontrada, cobertura, distancia total y por dron, hallazgos y área nueva por dron. Cobertura usa centros de celdas ponderados por área. Exposición y redundancia son aproximaciones en m²·s: por celda/dron/paso se cuenta una vez la cápsula recorrida; redundancia significa que la celda ya estaba cubierta. No equivalen a tiempo de solapamiento simultáneo exacto. Área nueva y hallazgos empatados se atribuyen al primer índice de dron. No utilizar esa atribución para concluir que un dron es mejor. Los tiempos de hallazgo tienen resolución del paso físico; el último paso se recorta al plazo.

## Reproducir

Desde la raíz, con SDK .NET 8 y los paquetes de Unity resueltos:

```powershell
dotnet run --project Tests/SimulationChecks -c Release
dotnet run --project Tests/SimulationChecks -c Release -- pilot
dotnet run --project Tests/SimulationChecks -c Release -- bench
python Experiments/adapter.py --dotnet dotnet --dll Tests/SimulationChecks/bin/Release/net8.0/SimulationChecks.dll
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/RunUnityTests.ps1
python Experiments/summarize.py Experiments/results/<carpeta-del-piloto>
```

El SDK incluido con este Unity está en `C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/DotNetSdk/dotnet.exe`. No requiere instalar Python packages. El adaptador JSONL persiste un proceso C#; operaciones reset/step/observe/close. No presupone Gym, PPO, una red ni una recompensa. Es un adaptador local síncrono; aún no tiene timeout de lectura ni gestión de workers paralelos.

El piloto cruza N=6/12/24, K=3/N, plazo=60/120, uniforme/agrupada (3 centros, desviación 4 m, reflexión en bordes), inicios común/disperso y 100 semillas por condición. Cada par A/B comparte descriptor exacto. Registra `episodes.jsonl` con escenarios y métricas, CSV por episodio, configuración con huella del código y resumen emparejado. Tiempo restringido asigna el plazo a los fallos: no se eliminan episodios fallidos. Los IC son exploratorios por condición, sin corrección múltiple. No interpretar el throughput del comando pilot, que incluye escritura de resultados, como benchmark aislado.

Reserva inicial de semillas: entrenamiento 0–79999 (sin ejecutar); throughput 90000–90099; validación 100000–179999 (piloto usa 100000–100099); test final 1000000–1079999 (sin ejecutar). Mantener el test final fuera de ajustes posteriores. Las mismas semillas entre condiciones no convierten esas condiciones en muestras independientes.

## Versión de escenarios

Esta revisión introduce mezcla avalanche de la semilla antes del LCG para evitar la correlación de primeras posiciones entre semillas consecutivas. También separa inicios de waypoints y recorta el plazo. **Los resultados históricos no se reproducen con esta versión del motor**, aunque tengan la misma semilla: necesitan el código original. El piloto guarda SHA256 de sus cuatro fuentes C# y todos sus parámetros. No se garantiza identidad binaria entre plataformas de coma flotante diferentes.
