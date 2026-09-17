# Transferencia de conocimiento: Mission Control PX4

**Estado final: Mission Control completado, aceptado y detenido.** La referencia del
slice anterior se preservó antes de trabajar como
`snapshot/px4-accepted-20260917` (`4f8c585`). La aceptación final corresponde a
`slice-20260917-152857`, sesión `4a6d45dc6b35a997`.

## Resultado

| Comprobación | Resultado |
|---|---:|
| Terminación de misión | deadline normal, sin fallo |
| Tiempo de búsqueda | 120,056 s simulados |
| MissionSnapshots auditados | 1776 |
| Hallazgos | 11/12 |
| Cobertura | 80,5833 % |
| Distancia horizontal | 595,452 m |
| Tareas / regiones completadas | 18 / 15 |
| Publicaciones de snapshot fallidas | 0 |
| Cámaras | 3 × JPEG, 640×480, máximo 5 Hz |
| Hashes congelados | 25/25 |
| Lanzador / supervisor | código 0 / cierre completo |

Los tres UAV aterrizaron y se desarmaron. La inspección final no encontró PX4,
Gazebo, MAVSDK, bridge ni capturadores de la ejecución aceptada. La auditoría compacta
y los hashes de las fuentes están en `acceptance-result.json`; los logs completos
permanecen en `.runtime/slice-20260917-152857/`.

## Frontera de autoridad

`MissionHost` sigue siendo autoridad de misión, asignaciones, detecciones, cobertura
y métricas. PX4/Gazebo siguen siendo autoridad de ejecución y estado físico. Unity
solo abre archivos de observación con acceso compartido:

1. `MissionHost` produce `snapshot.json` mediante reemplazo atómico y best-effort.
   Un bloqueo del consumidor se contabiliza pero jamás aborta la misión.
2. `Px4MissionControl` valida esquema, sesión, secuencia y tres identidades; aplica
   snapshots y JPEG en el hilo principal después de leerlos en segundo plano.
3. El minimapa pinta posiciones medidas, rutas activas emitidas por el host, celdas
   cubiertas, hallazgos detectados y progreso. No instancia `Simulation`.
4. Los capturadores se suscriben directamente a los tópicos `gz.msgs.Image`, codifican
   con OpenCV y reemplazan `camera-N.jpg`. Ningún dato de cámara retorna al host.

`SimulationRunner` no fue eliminado para conservar Fast 2D, pero se retiró su
bootstrap automático de `SampleScene`. Mission Control es el único bootstrap de esa
escena y no expone iniciar, pausar, reiniciar, interrumpir ni control manual.

## Prueba explícita de desacoplamiento

Durante la misión aceptada se inició el player Unity con `targetFrameRate=1`. Se
suspendió exclusivamente el capturador del UAV 2 durante cinco segundos:

| Momento | Secuencia | Tiempo simulado |
|---|---:|---:|
| Antes de perder frames | 624 | 41,948 s |
| Capturador aún suspendido | 683 | 46,100 s |
| Capturador reanudado | 719 | 48,572 s |

El timestamp del JPEG permaneció idéntico durante la suspensión y volvió a avanzar
después. Unity se cerró a continuación y la misión siguió hasta 120,056 s. Por tanto,
reducir el framerate, perder frames y cerrar Unity no detuvieron ni pausaron
MissionHost, PX4, Gazebo o el reloj de misión.

## Cámaras y rendimiento

El sensor original de `x500_mono_cam` publica 1280×960 a 30 Hz. Tres sensores con esa
configuración redujeron el factor de tiempo real hasta provocar expiraciones de
heartbeat MAVSDK. No se relajaron heartbeats ni watchdogs. Se conservan modelo,
orientación frontal, geometría, masa, FOV y planos de clipping, limitando únicamente
la observación visual a 640×480 y 5 Hz. El capturador publica solo el último frame:
no hay vídeo grabado, replay, RTSP, WebRTC ni visión artificial.

## Alcance preservado

No se modificaron estrategia, recompensa, condiciones experimentales, sensor XY,
objetivos ocultos, PPO ni LLM. Las cámaras no participan en detección, cobertura,
observaciones, decisiones o métricas. La misión fija continúa usando baseline A y
telemetría NED real transformada al mapa. Los artefactos congelados se verificaron
antes y después de cada ejecución.

## Reproducción

Desde la raíz:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Integrations/Px4/Run.ps1 -Mode slice
```

Abrir `SampleScene` en Unity muestra el último run con snapshot disponible. Un player
externo puede señalar el runtime mediante `LLM2SWARM_PX4_RUNTIME`; opcionalmente
`LLM2SWARM_MC_TARGET_FPS` limita solo su refresco visual.

Auditoría offline:

```powershell
wsl -d LLM2Swarm-PX4 -u root -- /opt/llm2swarm/venv/bin/python /mnt/c/Users/mugen/LLM2Swarm/Integrations/Px4/acceptance.py /mnt/c/Users/mugen/LLM2Swarm/Integrations/Px4/.runtime/slice-20260917-152857
```

No se continúa con PPO, LLM ni otro incremento.
