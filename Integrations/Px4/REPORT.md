# Transferencia de conocimiento: slice PX4

**Estado final: slice aceptado y detenido.** Ejecución de aceptación
`slice-20260917-142401`, sesión `d8ff1a1a6efa2893`. El lanzador devolvió código 0;
el supervisor registró cierre completo sin error. Los tres UAV aterrizaron y se
desarmaron. La inspección final no encontró procesos PX4, Gazebo, MAVSDK o puente
huérfanos. No se inició Unity. No se continúa con otro incremento.

| Resultado final | Medición |
|---|---:|
| Terminación de búsqueda | deadline, sin fallo de ejecución |
| Tiempo de búsqueda | 120,052 s simulados |
| Hallazgos | 11/12 |
| Primer objetivo | 4,064 s |
| Tiempo hasta K=12 | no alcanzado |
| Cobertura | 80,875 % |
| Distancia horizontal total | 599,669 m |
| Distancia por UAV | 188,297 / 203,220 / 208,153 m |
| Nuevos m² por UAV | 488 / 709 / 744 |
| Hallazgos por UAV | 2 / 5 / 4 |
| Exposición / exposición redundante | 9744,600 / 9628,064 m²·s |
| Tareas emitidas por UAV | 6 / 6 / 6 |
| Regiones completadas por UAV | 5 / 5 / 5 |
| Muestras de telemetría auditadas | 1948 |
| Hashes congelados verificados | 25/25 |

La auditoría offline pasó: identidades, modos Offboard durante toda la búsqueda,
transformaciones de todas las tareas, orden de waypoints, llegada medida a sus
destinos, progreso de los tres UAV, terminación y registros de desarmado/cierre.
Evidencia compacta versionable: [acceptance-result.json](acceptance-result.json).
Los registros completos permanecen en `.runtime/slice-20260917-142401/` y la
inspección final en `.runtime/verification-final.txt`; no se incluyen datos grandes
ni tokens efímeros en Git.

## Qué se ha construido

Se ha separado la decisión de búsqueda de su ejecución física sin editar el laboratorio
Fast 2D. El host de integración utiliza el baseline A existente para decidir **qué región
recorre cada UAV**. Un planificador determinista convierte la región en waypoints. PX4
ejecuta esos objetivos de posición mediante Offboard dentro de Gazebo. La telemetría
medida vuelve al host y alimenta las siguientes decisiones y las métricas.

No se ha conectado un LLM nuevo, cargado una política PPO, entrenado, ajustado una
recompensa ni cambiado un baseline. Esta prueba utiliza una misión fija y baseline A.
Los 25 hashes de congelación se comprueban antes y después de las ejecuciones.

## Flujo y responsabilidades

1. `MissionHost` crea el escenario y la intención fijos. Es la autoridad de misión.
2. Construye el contrato **SearchObservation** con posiciones medidas, disponibilidad,
   regiones completadas, cobertura conocida, hallazgos y tiempo restante. El baseline
   no recibe semilla, distribución, posiciones ocultas ni número real de objetivos.
3. `SystematicStrategy(false)`, enlazado desde el archivo original, devuelve tres IDs
   de región; `-1` significa continuar o esperar. El host valida la acción conjunta.
4. `LocalPlanner` produce barridos de la región. Es una copia de compatibilidad de
   `RegionRoute`, comprobada diferencialmente contra la implementación congelada.
5. `BridgeBackend` envía tareas identificadas por sesión, revisión, secuencia y UAV.
   Usa JSON por TCP localhost con un token efímero. El transporte no es parte del
   contrato de estrategia y se puede sustituir sin cambiar baseline A.
6. `bridge.py` mantiene un servidor MAVSDK por UAV y envía posiciones NED. MAVSDK
   sostiene el flujo Offboard; PX4 resuelve el control de vuelo. El puente no manda
   actitudes, motores ni acciones de una red neuronal.
7. Posición NED, velocidad, estado de armado, modo, identidad y progreso retornan
   al host. Llegar al último waypoint completa una región; un ACK no lo hace.
8. `TelemetryLedger` calcula detecciones y cobertura sobre segmentos XY medidos,
   y contabiliza distancia, hallazgos y exposición. No hay un segundo dron ideal
   moviéndose en paralelo ni un `Simulation.Step()` dentro del backend PX4.
9. Se registran eventos y un snapshot. Unity todavía no consume ese snapshot: su
   conexión requiere una vista separada de solo lectura. Unity no se abrió ni modificó.

La separación más importante para explicarlo es: **la estrategia elige trabajo;
el planificador lo convierte en geometría; PX4 controla el vuelo; el host interpreta
la telemetría y mantiene el estado de búsqueda**.

## Configuración operacional

| Elemento | Valor |
|---|---|
| Distribución dedicada | WSL2 `LLM2Swarm-PX4`, Ubuntu 24.04 LTS |
| PX4 | v1.17.0, commit `d6f12ad1c4f70ad3230afd7d86e971421e02fef4` |
| Gazebo | Harmonic, `gz-sim8` 8.15.0; transport13 13.6.0 |
| MAVSDK Python/servidor | 3.17.2 |
| Dependencias Python observadas | grpcio 1.84.0, protobuf 7.36.1, typing_extensions 4.16.0 |
| Mundo y modelos | un mundo `default`, tres x500 |
| Identidades MAVLink | 1, 2, 3; UAV del host 0, 1, 2 |
| Puertos UDP de recepción | 14540, 14541, 14542 |
| Servidores MAVSDK gRPC | 50051, 50052, 50053 |
| Transporte host/puente | TCP localhost 8765 |
| Mapa | 60 × 40 m; x este, y norte |
| Inicios nominales XY | (10,0), (30,0), (50,0) |
| Capas operacionales | 5, 8 y 11 m |
| Misión fija | seed 100000, uniforme, 12 objetivos, K=12, deadline 120 s |
| Búsqueda | baseline A original, rejilla 6 × 4, decisión cada 1 s |
| Sensor abstracto | radio XY 3 m, celdas de cobertura de 1 m |
| Llegada a waypoint | XY ≤1 m, altura ≤0,5 m, velocidad ≤0,5 m/s durante 0,5 s |

Cada UAV tiene su propio origen local del estimador. Con la referencia terrestre
`(n0,e0,d0)` y su posición conocida `(x0,y0,h0)`, la transformación es:
`N=n0+y-y0`, `E=e0+x-x0`, `D=d0-h+h0`. El signo vertical es negativo al subir.
No se supone que los tres ceros NED sean un único origen mundial.

El reloj de misión procede de Gazebo y empieza después del despegue y de confirmar
Offboard en los tres UAV. El watchdog usa tiempo real para detectar pérdida de host,
reloj o telemetría. Despegue y aterrizaje quedan fuera de las métricas de búsqueda.

La distribución Ubuntu 26.04, OpenClawGateway y `Experiments/.venv` no se modificaron.
No se alteraron configuración global de WSL, firewall ni red. El runtime se instaló
en `/opt/llm2swarm` dentro de la distribución dedicada. Los archivos grandes y su
VHDX quedan bajo `.runtime`, excluido de Git.

## Archivos y cómo leerlos

Todos los cambios de este incremento están en `Integrations/Px4/`:

| Archivo | Responsabilidad |
|---|---|
| `DESIGN.md`, `inspection.json` | Inspección y decisiones previas; conservan el contexto histórico |
| `Provision.ps1`, `provision.sh` | Importación Ubuntu verificada por SHA256 e instalación/compilación aislada |
| `Host/Host.csproj` | Ejecutable .NET 8; enlaza fuentes existentes y Newtonsoft ya instalado |
| `Host/Contracts.cs` | Contratos de tarea/telemetría, NED e interfaz de ejecución |
| `Host/LocalPlanner.cs` | Barrido determinista compatible con el planificador congelado |
| `Host/TelemetryLedger.cs` | Sensor y métricas a partir de posiciones medidas |
| `Host/MissionHost.cs` | Bucle de coordinación, terminación y registro |
| `Host/Program.cs` | CLI y pruebas diferenciales del host |
| `bridge.py` | Supervisor, conexión MAVSDK, Offboard, progreso, watchdog y aterrizaje |
| `Run.ps1` | Lanzamiento smoke/slice, comprobación de congelado y cierre |
| `Verify.ps1`, `test_bridge.py` | Tests y lectura de versiones/procesos |
| `acceptance.py` | Auditoría de las trazas reales, sin lanzar simulaciones |
| `acceptance-result.json` | Resultado final y hashes de las fuentes de integración inspeccionadas al aceptar |
| `.gitignore`, `README.md`, `REPORT.md` | Exclusión del runtime, reproducción e informe |

El árbol Git ya contenía cambios de fases anteriores. No se agruparon en un commit
nuevo ni se revirtieron. Que Git muestre cambios en fuentes RL frente a HEAD no
significa que este incremento las haya editado: la referencia de congelación son
los manifiestos de hashes, que se verifican independientemente.

## Pruebas y problemas encontrados

Pruebas de contrato: 90 rutas coinciden punto por punto con `RegionRoute` para dos
rejillas y tres inicios; replay del baseline A; tres transformaciones NED de ida y
vuelta; rechazo de timestamps duplicados, desordenados o con huecos; 60 muestras de
una traza sintética comparadas contra detecciones, cobertura, distancia, exposición
y redundancia del sensor congelado. Los tests Python comprueban validación conjunta
atómica, identidad/revisión/secuencia y que aceptar un objetivo no equivale a llegar.

La prueba de un UAV completó despegue a 5 m, objetivo +2 m norte/+2 m este y aterrizaje.
El error horizontal final respecto al objetivo fue aproximadamente 0,16 m. Se
confirmaron identidad 1, estado Offboard y cierre del proceso.

Incidencias resueltas durante integración:

- La variable `PX4_GZ_STANDALONE=0` sigue contando como definida para el script
  upstream. Se eliminó del primer UAV; solamente los otros dos usan `=1`.
- La primera orden de armado llegó antes de `is_armable`. Ahora se espera salud
  completa y autorización de armado; no se desactivaron checks de PX4.
- El tercer UAV despegó a los 2,5 m por defecto cuando el cambio de parámetro y
  el armado se hicieron inmediatamente. Ahora las alturas se preparan antes,
  se leen de vuelta y se permite que Navigator consuma la actualización.
- La primera ejecución completa aún mostraba `HOLD` en la telemetría inicial del
  tercero después del ACK de Offboard. Ahora se espera confirmación del modo de
  los tres antes de iniciar el reloj de búsqueda.
- El lanzador notificó fallo al recoger el cierre aunque el puente no registró
  errores y los vehículos se habían desarmado. Se conserva el handle nativo y
  se registra el código de salida para verificar el cierre explícitamente.

La ejecución `slice-20260917-103252` terminó normalmente por deadline a 120,020 s:
11/12 objetivos, 80,9167 % de cobertura, 600,283 m horizontales, 18 tareas emitidas
y 15 regiones completadas. Aterrizaron y se desarmaron los tres. La inspección
posterior no encontró procesos PX4/Gazebo/MAVSDK/puente ni sus puertos abiertos.
Esta ejecución motivó las comprobaciones finales de barrera y cierre indicadas arriba.

## Límites que deben explicarse

El éxito de integración significa que tres UAV reciben y ejecutan las decisiones
del coordinador y devuelven su estado. No exige encontrar los 12 objetivos antes de
120 s. `metrics.success=false` al agotar el tiempo describe la misión de búsqueda,
no prueba un fallo de la arquitectura. Tampoco permite concluir nada sobre PPO.

El backend Fast 2D es determinista e ideal. PX4 introduce dinámica, ruido del
estimador, aceleraciones, paradas, tolerancias y muestreo. Una misma intención no
garantiza la misma trayectoria ni la misma secuencia de futuras asignaciones:
las observaciones y la disponibilidad cambian. La equivalencia exacta se prueba
sobre observaciones/rutas comunes; en vuelo se exige respetar identidad, tarea,
orden de waypoints y finalización. No mezclar estas métricas con el experimento RL.

Deuda técnica explícita: detección completa de resets del origen EKF, recuperación
tras desconexiones, pruebas exhaustivas de fallos/reloj pausado, validación con
trayectorias curvas para el muestreo del sensor, y vista Unity de solo lectura.
El contador actual admite exclusivamente el escenario uniforme del slice. Los
paquetes del sistema se registran por versión; la instalación apt no es una imagen
hermética. El host requiere el SDK y Newtonsoft existentes en este proyecto.

No hay visión artificial ni detección desde imágenes Gazebo: las detecciones son
el sensor abstracto XY aplicado a telemetría. Las capas de altura son una adaptación
operacional autorizada, no una nueva condición del estudio RL.

## Reproducción y siguiente paso

Los comandos exactos están en README.md. `Provision.ps1` prepara el entorno,
`Verify.ps1` comprueba contratos/hashes, `Run.ps1 -Mode smoke` prueba un UAV y
`Run.ps1 -Mode slice` ejecuta la misión fija con tres. Los logs y resúmenes quedan
en el directorio que imprime el lanzador. `acceptance.py <directorio>` valida las
trazas offline y no usa GPU ni lanza vehículos.

Después de aceptar el slice, el siguiente incremento mínimo propuesto es conectar
el snapshot a una vista Unity independiente de solo lectura. Requiere una nueva
autorización de trabajo; no se inicia automáticamente. No se propone entrenar ni
cambiar el experimento para mejorar los números observados aquí.
