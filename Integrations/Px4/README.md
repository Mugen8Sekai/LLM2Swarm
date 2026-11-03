# PX4 integration slice

## Fase 2: misión integrada

### Comparativa automatizada

Desde la raíz del repositorio, después de haber arrancado una misión preparada
desde Unity (se reutiliza el último `prepared-*.json` exportado):

```powershell
# Generar e inspeccionar el plan, sin arrancar vehículos:
python Integrations/Px4/batch.py
# Ejecutar A/B/PPO secuencialmente para tres semillas de validación (9 vuelos):
python Integrations/Px4/batch.py --execute
# Repetir solo el escenario original (3 vuelos):
python Integrations/Px4/batch.py --execute --seeds 100000
# También admite --mission ruta/al/prepared.json y --repeats 3.
python Integrations/Px4/batch.py --summarize Integrations/Px4/.runtime/batch-<fecha>
python Integrations/Px4/test_batch.py
```

El lote conserva la interpretación del LLM, cambia únicamente estrategia y semilla
de objetivos y rota el orden A/B/PPO por bloque. Las posiciones de salida, sensores
y dinámica siguen siendo los del escenario PX4. No se consume TEST ni se entrena.
Esperar a que termine la ejecución de Unity antes de iniciar el lote; no arrancar
misiones manuales durante la comparación. Gazebo se reinicia en cada vuelo.
Los lotes quedan en `.runtime/batch-*`, con peticiones, logs, `batch.json`,
`episodes.csv` y `REPORT.md`. El resumen solo considera válidos los vuelos con
resumen sin fallo y cierre correcto del supervisor. No hay reintentos silenciosos:
un error de infraestructura detiene el lote y conserva los resultados anteriores.
La cobertura se mide al final de cada vuelo, por lo que su duración afecta a la
comparación; revisar también éxito, tiempo hasta K hallazgos y distancia.

La pestaña Misión integrada puede enviar una intención preparada (`areas: ["all"]`)
y elegir baseline A, baseline B o PPO seed 47. El escenario PX4 sigue siendo el
escenario fijo descrito abajo. La política usa las observaciones de telemetría
medida y el mismo planificador local de tareas que los baselines; no recibe
objetivos ocultos. El host copia el checkpoint utilizado a `policy.json` dentro
de la ejecución. Es inferencia de la política entrenada en 2D, sin entrenamiento
adicional ni garantía de conservar su rendimiento en dinámica PX4.

Los checks del host verifican carga del checkpoint y decisiones admitidas por
el entorno de búsqueda. La validación de vuelo PPO en Gazebo queda pendiente.
La descripción histórica de baseline A a continuación corresponde al slice original.

This directory is independent of the frozen Fast 2D experiment. No policies are trained.
The three design decisions in DESIGN.md were approved: position Offboard, a dedicated
Ubuntu 24.04 WSL distribution, and operational altitude layers 5/8/11 m.

## Architecture implemented

`MissionHost` owns the fixed mission, detected targets, coverage and metrics. It passes
only the existing `SearchObservation` to the unchanged `SystematicStrategy(false)`
(baseline A). `LocalPlanner` reproduces the frozen private region planner; the host
links the existing C# source files without editing them. `IExecutionBackend` exchanges
versioned waypoint tasks and telemetry with a Python MAVSDK bridge over authenticated
JSON lines on localhost port 8765. Each vehicle has its own MAVSDK server and NED origin.
PX4 controls attitude, velocity and position; the host does not simulate motion.

The bridge runs one Gazebo world, x500_mono_cam_0/1/2, MAVLink system IDs 1/2/3, UDP destinations
14540/14541/14542 and MAVSDK gRPC ports 50051/50052/50053. Gazebo transport uses a unique
per-run `GZ_PARTITION`. A lock inside the dedicated distribution prevents concurrent
supervisors. No ROS 2 node or agent is started by this integration. PX4's standard
firmware includes its default uXRCE client; there is no DDS agent or ROS dependency in
the mission path.

Mission Control is a separate read-only Unity component. `MissionHost` atomically
publishes a versioned `MissionSnapshot` containing measured positions, active routes,
coverage, detected findings, vehicle state and progress. Unity polls that file on a
worker thread and never opens the authenticated command channel. The legacy
`SimulationRunner` remains in the repository but no longer bootstraps `SampleScene`;
there is no `Simulation.Step()` or local vehicle motion in Mission Control.

Each `x500_mono_cam` keeps the upstream front-camera geometry and inertial values. Its
visual-only sensor is limited to 640×480 at 5 Hz so rendering cannot starve MAVLink
heartbeats. A small Gazebo Transport/OpenCV subscriber publishes only the latest JPEG
for each UAV by atomic rename. Frames are neither recorded nor consumed by the host,
ledger, observations, strategy or metrics. Missing or stale images affect only the
corresponding Unity panel.

Map coordinates are metres, x east and y north. For each ground calibration
`(n0,e0,d0,x0,y0,h0)`, the transform is `N=n0+y-y0, E=e0+x-x0, D=d0-h+h0`.
Measured N/E are transformed back before sensing. The fixed scenario has seed 100000,
12 uniform targets in 60×40 m, sensor radius 3 m, coverage cells 1 m, K=12,
deadline 120 simulated seconds, grid 6×4 and decision interval 1 s. Starts are
(10,0), (30,0), (50,0). The heights are operational settings of this backend only.

The search clock starts after all three takeoffs and Offboard entry. Time comes from
Gazebo `/world/default/clock`; watchdogs use wall time. Waypoint completion requires
horizontal error ≤1 m, vertical error ≤0.5 m and speed ≤0.5 m/s sustained for 0.5
simulated seconds. Acceptance is not completion. Commands for occupied UAVs, duplicate
vehicle IDs, stale sequences, invalid frames or revisions are rejected atomically.
No network retry/reconnection protocol is claimed in this slice.

## Reproduction from the repository root (PowerShell)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Integrations/Px4/Provision.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Integrations/Px4/Verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Integrations/Px4/Run.ps1 -Mode smoke
powershell -NoProfile -ExecutionPolicy Bypass -File Integrations/Px4/Run.ps1 -Mode slice
```

Audit the recorded mission without launching vehicles (replace the final directory
with the one printed by your run):

```powershell
wsl -d LLM2Swarm-PX4 -u root -- /opt/llm2swarm/venv/bin/python /mnt/c/Users/mugen/LLM2Swarm/Integrations/Px4/acceptance.py /mnt/c/Users/mugen/LLM2Swarm/Integrations/Px4/.runtime/slice-20260917-142401
```

Provision imports `LLM2Swarm-PX4` using the SHA256-checked official Ubuntu 24.04 WSL
rootfs. Its VHDX and download are under ignored `.runtime`. It installs PX4 v1.17.0,
Gazebo Harmonic and MAVSDK Python 3.17.2, with its own `/opt/llm2swarm/venv`.
It does not edit WSL/Windows global settings, firewall, Ubuntu 26.04,
OpenClawGateway or `Experiments/.venv`. C# uses the already installed Unity .NET 8 SDK
and existing Newtonsoft assembly. No additional NuGet package is installed.

Both launch commands check the 25 frozen hashes before and after. The smoke command
starts exactly one UAV, takes off to 5 m, executes a +2 m north/+2 m east position
goal, waits for measured completion and lands. The slice command starts exactly
three UAVs and runs baseline A until success, deadline, exhaustion or explicit abort.
Both close their owned process groups. An interrupted wrapper sends TERM only to its
recorded supervisor; it never calls global `pkill`, `wsl --shutdown` or kills Unity.

Artifacts appear under `.runtime/smoke-*` or `.runtime/slice-*`: PX4 ULog and console
logs, MAVSDK logs, supervisor/cleanup records; the slice additionally writes
`events.jsonl`, `snapshot.json`, `camera-0.jpg` through `camera-2.jpg`, and `summary.json`. Session tokens in `session.json`
are local runtime data and must not be committed. `events.jsonl` records tasks,
measured positions, progress, findings and metrics. Raw flight logs are not RL data.

## Verification and limits

The C# checks compare 90 routes against the original planner, replay A on common
observations, round-trip three NED origins, reject invalid timestamps, and compare
60 samples of coverage/detections/distance/exposure/redundancy with the frozen sensor.
Python tests check atomic command validation and that ACK cannot complete a waypoint.
These tests do not establish exhaustive fault tolerance or physical trajectory parity.

Fast 2D integrates ideal movement deterministically. PX4 has estimator noise, flight
dynamics, stopping at waypoints, takeoff/landing and runtime-dependent sampling.
Coverage is computed from measured XY segments; distance is horizontal and excludes
takeoff/landing. Small sampling overshoot at the deadline is recorded, not hidden.
The bridge rejects timestamp resets and the host rejects telemetry gaps above 1 s;
full EKF-origin reset handling, packet replay/reconnection and fault-injection tests
remain technical debt. Frozen metrics are not redefined and PX4 runs must not be
pooled with the paired RL evaluation.

Unity Mission Control consumes the snapshot boundary and latest camera frames only.
The snapshot includes detected targets only, never undetected target coordinates.
The accepted decoupling test ran the Unity player at 1 FPS, suspended one camera
capturer for five seconds, resumed it, and closed Unity while snapshots continued.
See `MISSION_CONTROL_REPORT.md` and `acceptance-result.json`. This integration still
supports the fixed uniform scenario only.

See REPORT.md for measured outcomes and the exact installed dependency versions.
