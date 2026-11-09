# Aclaraciones de evidencia del informe de Fase 2

Revisión del código, historial Git y registros locales, 3 de noviembre de 2026.
Esta nota complementa y limita las afirmaciones del informe principal.

## 1. Procedencia de la intención y recorrido del LLM

La petición que alimentó el lote está en [source-mission.json](source-mission.json).
Contiene la instrucción original, la intención K=6/plazo=120/areas=[all] y la
referencia al directorio de inferencia
`7c44d40ed58c4102840e801c60bb5265`.

Ese directorio existe y conserva respuesta original, stdout, conversación,
instrucción, prompt, esquema y metadatos del modelo. Se han archivado copias en
[llm-evidence](llm-evidence/response.json). El modelo registrado es
`mistralai/Ministral-3-3B-Instruct-2512-GGUF`, cuantización Q4_K_M,
runtime b10993; su revisión y SHA256 figuran en
[installed.json](llm-evidence/installed.json).

La respuesta original es una propuesta con `requiredFindings=6`, `maxSeconds=120`
y `areas=["all"]`, coincidente con la intención del lote. No es una intención
que el ejecutor del lote construyera a partir de texto: `batch.py` lee una petición
ya preparada y solo cambia estrategia y semilla.

En el código, `InterpretInstruction` conserva la respuesta, instrucción y evidencia;
`PrepareProposal` las convierte en misión activa; `SavePx4Request`, llamado por
Start PX4 cuando existe el editor integrado, exporta esos valores al JSON preparado.
La procedencia registrada es consistente con ese recorrido desde la tercera
pestaña y con la prueba manual previa. Sin embargo, **no existe un log de eventos
de UI que registre pestaña activa y clic de confirmación**. Podemos acreditar
inferencia real y correspondencia con la intención ejecutada, pero no certificar
independientemente cada interacción humana ni excluir una edición externa del
JSON solo con estos artefactos. No debe afirmarse una auditoría completa de clics.

El lote de 60 vuelos reutilizó esa interpretación; no ejecutó 60 interpretaciones
ni constituye una evaluación estadística del LLM.

## 2. Selección del checkpoint y elección de seed 47

Hay dos decisiones distintas:

1. **Checkpoint dentro de cada entrenamiento.** El protocolo histórico selecciona
   en 96 escenarios de validación (48 condiciones × semillas 100100/100101),
   priorizando éxito, después menor tiempo restringido y checkpoint anterior en
   empate. Para seed 47 seleccionó el paso 196608: 83,33 % de éxito y 34,40 s.
   Las seeds 11 y 29 también alcanzaron 83,33 %, con 36,87 y 36,64 s,
   respectivamente. Esto está documentado antes de la evaluación PX4 ampliada.
2. **Elegir una de las tres políticas para integrar.** Seed 47 ya estaba fijada
   en el selector Fast2D y su clase C# en el commit `03a4340`, anterior al lote
   PX4 y al commit de integración `5b456cb`. El manifiesto del lote fija el hash
   de esa política; no se eligió entre 11/29/47 observando sus 60 resultados PX4.

No se ha localizado una declaración previa que establezca formalmente el criterio
para escoger la seed 47 entre las tres. Sus menores tiempos en la validación de
selección, y su mejor éxito/tiempo entre las tres en la comparación histórica de
4800 episodios, son razones compatibles con la elección; **no prueban la motivación
original**. No debe presentarse retrospectivamente como un criterio preregistrado.

Tampoco puede afirmarse que se eligiera antes de observar *cualquier* vuelo PX4:
ya existían vuelos de baseline A y el slice aceptado en septiembre, anteriores
al selector PPO (`03a4340`, 18 de septiembre). Lo acreditado es que estaba fijada
antes del lote ampliado. Los registros examinados no contienen una competición
PX4 entre seeds 11, 29 y 47. Los resultados describen una política elegida, no la
media del algoritmo sobre semillas de entrenamiento.

Fuentes: [protocolo PPO](../ppo/README.md),
[informe histórico](../ppo/report/REPORT.md),
[selección seed 47](../ppo/report/full-47/selection.json) y
[manifiesto del lote](batch.json).

## 3. Fast2D frente a PX4: comparación no disponible todavía

No se ha localizado un lote Fast2D con **las mismas 20 semillas y la misma
configuración N=12, K=6, plazo=120, uniforme y salidas dispersas**.
La evaluación histórica de 4800 episodios varía N=6/12/24, K=3/N, plazo,
distribución y salidas. Para N=12 usa K=3 o K=12, no K=6. Por tanto, no basta
con filtrar sus resultados por estas semillas para obtener el control requerido.

Los promedios históricos y los del lote PX4 corresponden a problemas diferentes.
La aparente diferencia de clasificación entre entornos no puede atribuirse a PX4
sin un control Fast2D emparejado. Las semillas 100003–100022 ya pertenecían al
conjunto histórico de validación en otras condiciones; son nuevas para el piloto
PX4, **no semillas nunca observadas en la investigación previa**.

El siguiente experimento para aclararlo debe ejecutar A/B/PPO-47 en Fast2D sobre
los 20 descriptores del lote, manteniendo intención, generador, salidas, sensores,
rejilla y checkpoint. Después debe comparar diferencias por escenario y método.
Incluso así, ruido, aceptación de waypoints, muestreo y dinámica son diferencias
del backend, por lo que no se exige igualdad de trayectorias.

## 4. Corte de reloj, distancia y cobertura

El host recibe telemetría y llama a `TelemetryLedger.Update` para los tres drones.
Esa actualización procesa todos los segmentos desde la muestra anterior y asigna
el tiempo de la muestra a los nuevos hallazgos. A continuación publica el snapshot
y comprueba `found >= requiredFindings`; si se cumple, sale del bucle.

**El corte es conjunto y discreto, en la primera muestra procesada que alcanza K**,
no una interrupción interpolada en el instante geométrico exacto del sexto hallazgo.
Se incluye el intervalo completo de esa última muestra para los tres drones. En
general podría haber más de K hallazgos dentro de esa misma muestra; en este lote
los 60 snapshots finales tienen exactamente 6.

Tras salir, el host llama a `backend.Land()` en `finally`, sin volver a actualizar
el ledger. El puente cancela tareas de navegación y solicita aterrizaje; no hay
un regreso explícito al punto de salida en esa rutina. El reloj de Gazebo y el
movimiento físico continúan durante la maniobra, **pero no se incorporan al tiempo,
distancia o cobertura de búsqueda**. El resumen se escribe después del aterrizaje
con las métricas que quedaron congeladas antes de iniciarlo.

Se han contrastado los 60 resúmenes con sus últimos snapshots: coinciden
`seconds`, `coverage`, `totalDistance` y `found` en todos los casos. El máximo
intervalo entre snapshots de búsqueda consecutivos observado fue 0,172 s
simulados. No se interpreta esa cifra como garantía general de resolución.

Fuentes de código: `Integrations/Px4/Host/MissionHost.cs`,
`Integrations/Px4/Host/TelemetryLedger.cs` y `Integrations/Px4/bridge.py` (`land`).

## 5. Nombre y alcance actual de la interfaz

El código de `LLM2SwarmAppShell` define las pestañas **Fast 2D / Lab**,
**PX4 / Mission Control** y **Misión integrada**. No aparece «Fast PX4» como nombre
de la tercera pestaña en la versión revisada.

Desde Misión integrada se puede interpretar, revisar y preparar una propuesta,
seleccionar A/B/PPO, arrancar o interrumpir PX4, ver cámaras y telemetría, consultar
el resumen final y abrir la carpeta de registros. El arranque llama automáticamente
a scripts de PowerShell/WSL; no exige escribir comandos por vuelo una vez instalado
el entorno. La provisión inicial, disponibilidad de modelos/dependencias y ciertos
diagnósticos siguen siendo externos a la UI.

La evaluación masiva se lanzó mediante `batch.py`, fuera de la pestaña. Los IC y
las comparativas agregadas se consultan en los informes, no en un panel estadístico
de Unity. El recorrido individual está integrado; el proceso experimental completo
no está encapsulado en una única pantalla. El nombre y estas capacidades se
acreditan por código y registros; no se añade aquí una nueva prueba visual de UI.

## 6. Localización de artefactos

- [CSV de los 60 vuelos](episodes.csv).
- [Análisis emparejado e IC sin redondeo](paired-analysis.json).
- [Protocolo previo al lote, copia conservada](PROTOCOL.md).
- [Manifiesto: commit, hashes, orden, semillas y códigos de salida](batch.json).
- [Misión fuente del lote](source-mission.json).
- [Respuesta original del LLM](llm-evidence/response.json).
- [Salida original del proceso de inferencia](llm-evidence/stdout.txt).
- [Informe principal](REPORT.md).

Los eventos, snapshots, políticas por vuelo y logs completos están en
`Integrations/Px4/.runtime/evaluation20-20261103-154708/`, ignorado por Git.
El análisis legible generado automáticamente es `PAIRED.md` dentro de ese lote.
