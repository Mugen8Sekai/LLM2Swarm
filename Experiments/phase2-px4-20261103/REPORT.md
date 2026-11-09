# Informe de la Fase 2: integración y evaluación en PX4/Gazebo

**Proyecto:** LLM2Swarm · TFM  
**Fecha:** 3 de noviembre de 2026  
**Versión evaluada:** `5b456cbe4075e7978191e3f050d7eaa33b8eaf8d`  
**Estado:** integración funcional para búsqueda en toda la región; evaluación ampliada completada.

**Revisión de evidencia:** las aclaraciones de procedencia, selección de política,
comparabilidad entre entornos y corte de métricas se detallan en
[CLARIFICATIONS.md](CLARIFICATIONS.md). No se dispone de una comparación Fast2D
con las mismas 20 semillas y K=6; los resultados PX4 no aíslan el efecto del backend.

## 1. Resultado principal

La Fase 2 conecta la interpretación local de instrucciones con la preparación de
una misión y su ejecución mediante PX4/Gazebo. La interfaz conserva los laboratorios
de componentes y añade una tercera pestaña para el recorrido integrado. Se han
incorporado las estrategias clásicas A y B y la política PPO existente al mismo
host de misión, compartiendo observaciones, planificación local y evaluación.

La evaluación ampliada incluye **60 vuelos: 20 escenarios emparejados por tres
estrategias**. Los tres métodos alcanzaron los seis objetivos requeridos en todos
sus vuelos. PPO obtuvo un tiempo medio de **34,06 s**, frente a **57,71 s** de A y
**72,57 s** de B: reducciones de las medias del **40,98 %** y **53,06 %**, respectivamente.
Son cocientes entre medias, no medias de porcentajes de mejora por escenario.

Los intervalos exploratorios emparejados del 95 % para las diferencias de tiempo
PPO−A y PPO−B excluyen cero. Los resultados apoyan una ventaja de tiempo en esta
configuración. La cobertura media al finalizar es semejante y no demuestra una
ventaja de cobertura de PPO. El alcance es un piloto de validación con 20 semillas,
una sola intención y una única configuración física; TEST final permanece reservado.

## 2. Punto de partida y trabajo realizado

La Fase 1 quedó conservada en el commit `093eade`, etiqueta `fase-1`. Reunía tres
líneas de trabajo: interpretación LLM hacia misión, comparación de estrategias
en 2D e integración con PX4/Gazebo. La Fase 2 se desarrolla en la rama
`codex/fase-2-integracion` para conectarlas mediante un recorrido de usuario común.

Se han realizado las siguientes modificaciones:

- **Interfaz:** tercera pestaña «Misión integrada», basada en la vista PX4 con
  minimapa y tres cámaras, más la columna de interpretación y preparación.
- **Preparación:** conservación de la intención validada, instrucción y referencia
  de evidencia; rechazo explícito de misiones por sectores aún no soportadas.
- **Ejecución:** envío de la intención preparada y de la estrategia elegida al
  lanzador local y al host PX4 mediante una petición JSON conservada por ejecución.
- **Estrategias:** selección A/B/PPO en el host; carga de la política seed 47 para
  inferencia y conservación de una copia del checkpoint en cada vuelo PPO.
- **Seguimiento:** lectura de snapshots y resumen final, presentación de resultados,
  acceso a registros y bloqueo de arranques duplicados desde el controlador.
- **Automatización:** lotes secuenciales, rotación del orden de estrategias,
  escenarios emparejados, exportación CSV e informes y análisis bootstrap.
- **Trazabilidad:** commit y hashes SHA256 en el manifiesto; comprobación de que
  código y política no cambien antes de cada vuelo; auditoría final de registros.

Este avance constituye una integración funcional, aunque la separación completa
entre coordinador de misión y presentación prevista en el plan inicial sigue
siendo trabajo pendiente. La tercera pestaña reutiliza componentes existentes.

## 3. Responsabilidades de los componentes

El recorrido implementado es:

**Instrucción → interpretación local → validación y preparación → selección de
estrategia → asignaciones de regiones → rutas locales → ejecución PX4 → resultados.**

El LLM interpreta el número de hallazgos requerido, el plazo y las áreas admitidas.
La estrategia se elige en la interfaz. En la integración actual se admite
`areas: ["all"]`; las dimensiones, objetivos totales, salidas y sensores proceden
del escenario PX4 fijo. Una instrucción no configura libremente ese escenario.

A, B y PPO trabajan sobre observaciones de geometría, posición, disponibilidad,
asignaciones, cobertura, hallazgos observados y tiempo restante. No reciben las
posiciones ocultas de los objetivos. **PPO asigna regiones a drones libres**; el
planificador local convierte cada región asignada en un barrido en S, y PX4 sigue
sus waypoints. PPO no ha aprendido ese barrido ni controla actitud o motores.

Las cámaras son visualización. Los hallazgos y la cobertura se calculan mediante
el sensor geométrico sobre segmentos de posiciones XY medidas. Este experimento
no evalúa detección visual de objetivos a partir de las imágenes.

## 4. Protocolo de evaluación

La instrucción reutilizada, sin volver a invocar el LLM entre métodos, fue:

> Encuentra 6 objetivos en toda la región en un máximo de 120 segundos.

| Parámetro | Valor |
|---|---|
| Semillas del lote ampliado | 100003–100022, inclusive |
| Escenarios | 20, cada uno compartido por A, B y PPO |
| Vuelos | 60; una ejecución por método y semilla |
| Mapa | 60 × 40 m |
| Objetivos totales | 12, distribución uniforme |
| Objetivo de misión | 6 hallazgos distintos |
| Plazo de búsqueda | 120 s simulados |
| Drones | 3 |
| Salidas XY | (10,0), (30,0), (50,0) m |
| Alturas operativas | 5, 8 y 11 m |
| Velocidad horizontal configurada | 4 m/s |
| Radio del sensor | 3 m |
| Celda de cobertura | 1 m |
| Regiones de decisión | Rejilla 6 × 4 |
| Intervalo de decisión | 1 s simulado |
| PPO | Actor exportado `PPO-centralized-pilot-full-47`, sin reentrenamiento |

El orden se rota por escenario: A/B/PPO, B/PPO/A, PPO/A/B. Gazebo y los vehículos
se reinician en cada vuelo. Se conserva la misma configuración lógica dentro de
cada terna; las estimaciones, orígenes NED medidos y trayectorias físicas pueden
variar entre reinicios. El emparejamiento no implica igualdad de ruido físico.

El tiempo de búsqueda usa el reloj simulado de Gazebo y comienza tras el despegue
y la entrada de los tres drones en Offboard. No incluye el arranque del simulador,
el despegue ni el aterrizaje. La distancia es horizontal, acumulada entre drones;
no equivale a consumo energético. La cobertura es área única aproximada mediante
celdas, no distancia recorrida ni detección visual.

Se fijó como métrica principal el tiempo restringido hasta el sexto hallazgo:
tiempo observado en éxitos y plazo completo en fallos de búsqueda. También se
registraron éxito, primer hallazgo, distancia y cobertura. Los errores de
infraestructura detienen el lote y no se sustituyen silenciosamente. En este lote
no hubo errores de infraestructura ni fallos de búsqueda.

La inferencia usa diferencias emparejadas PPO−baseline por semilla y bootstrap
percentil de 20 000 muestras, RNG 20261103. Los IC son exploratorios del 95 %,
sin corrección por comparaciones múltiples. La unidad de remuestreo es el escenario,
no los drones ni los ticks. El [protocolo fijado](../../Integrations/Px4/EVALUATION_PHASE2.md)
se guardó antes de lanzar el lote.

## 5. Resultados de los 60 vuelos

| Métrica | A | B | PPO |
|---|---:|---:|---:|
| Vuelos técnicamente válidos | 20/20 | 20/20 | 20/20 |
| Éxitos de búsqueda | 20/20 | 20/20 | 20/20 |
| Tiempo medio hasta 6 hallazgos (s) | 57,71 | 72,57 | **34,06** |
| Mediana del tiempo (s) | 54,63 | 67,67 | **34,14** |
| Tiempo medio al primer hallazgo (s) | 3,48 | 8,97 | **2,59** |
| Distancia media total (m) | 304,40 | 368,13 | **243,00** |
| Cobertura media al finalizar | 46,18 % | 47,19 % | 46,14 % |

PPO fue más rápido que A en **16 de 20** escenarios y más rápido que B en
**18 de 20**. Por tanto, la mejora media no implica que gane en todas las semillas.

### Diferencias emparejadas

Los valores negativos de tiempo y distancia favorecen a PPO. La cobertura se
expresa aquí en **puntos porcentuales**, no como mejora relativa.

| Comparación | Métrica | Diferencia media | IC exploratorio 95 % |
|---|---|---:|---|
| PPO−A | Tiempo (s) | −23,65 | [−34,53; −12,16] |
| PPO−B | Tiempo (s) | −38,51 | [−48,24; −28,56] |
| PPO−A | Distancia (m) | −61,40 | [−116,59; −2,62] |
| PPO−B | Distancia (m) | −125,13 | [−176,03; −73,55] |
| PPO−A | Cobertura (pp) | −0,03 | [−6,49; 6,88] |
| PPO−B | Cobertura (pp) | −1,04 | [−7,50; 5,41] |

Como todos los vuelos alcanzaron el objetivo, en esta muestra el tiempo restringido
coincide con el tiempo al sexto hallazgo. No hay evidencia de una diferencia de
éxito entre métodos. Que todos tengan 20/20 no permite afirmar fiabilidad perfecta
fuera de esta muestra; el bootstrap de diferencias de éxito degenerado en cero
no cuantifica la incertidumbre de la tasa poblacional de éxito.

## 6. Relación con el piloto inicial

Antes de fijar el lote ampliado se realizaron nueve vuelos exploratorios con
semillas 100000–100002. Sus tiempos medios fueron A 33,81 s, B 69,83 s y PPO 27,81 s.
PPO ganó dos de las tres ternas, mientras A ganó la restante. En la primera semilla
PPO también mostró más cobertura, lo que motivó ampliar la evaluación.

Esos nueve vuelos **no se han agregado** a las medias ni a los intervalos del lote
ampliado. Las 20 semillas ampliadas son nuevas respecto a ese piloto PX4, pero
pertenecen al conjunto de validación; no se presentan como un TEST final intacto.

## 7. Interpretación y límites

Los resultados son compatibles con una asignación y secuenciación de regiones
más eficiente por PPO en el problema evaluado. Al compartir planificador local,
sensor y ejecutor PX4, la comparación se centra en la estrategia de asignación.
No identifica por sí sola el mecanismo causal exacto de la mejora; para ello haría
falta estudiar trayectorias, tránsito, asignaciones y tiempos por dron.

La menor distancia media apoya una mejora de eficiencia espacial, con IC de PPO−A
cercano a cero en su extremo superior. No se ha medido energía, por lo que no debe
traducirse directamente a ahorro de batería.

La cobertura final no mejora de forma clara: ambos IC incluyen cero. Además, cada
vuelo se detiene al sexto hallazgo, de modo que las coberturas corresponden a
tiempos distintos. Para comparar capacidad de exploración convendría evaluar
cobertura a un horizonte común en un experimento específico.

El alcance sigue limitado por:

- Una intención fija: K=6, plazo=120 s, búsqueda global.
- Veinte semillas de validación y una sola repetición física por combinación.
- Un único mapa, distribución uniforme, salidas y configuración de sensores.
- Uso de una sola política seleccionada previamente; no se comparan semillas
  de entrenamiento ni se evalúa aquí su procedimiento de selección.
- Sensor ideal geométrico, ausencia de percepción visual y de vuelos reales.
- Sin evaluación sistemática de latencia, exactitud o aclaraciones del LLM:
  se reutilizó una intención confirmada para aislar las estrategias de búsqueda.
- Sin evaluación exhaustiva de recuperación ante fallos o pérdida de comunicación.

## 8. Verificación y trazabilidad

La compilación C# de Unity se verificó sin errores durante la integración. Las
comprobaciones del host incluyen carga PPO y decisiones admitidas, 90 rutas
diferenciales, replay de A, transformaciones de tres orígenes NED, rechazo de
telemetría inválida y 60 muestras de paridad de sensores/métricas. Las pruebas del
lote comprueban emparejamiento, orden, resultados incompletos y análisis de fallos.
Estas comprobaciones no equivalen a una certificación de tolerancia a fallos.

La auditoría posterior de los 60 vuelos confirmó:

- Semillas e intenciones registradas iguales a las peticiones preparadas.
- Misma configuración de escenario dentro de cada una de las 20 ternas.
- Identidad correcta de A, B y PPO en los eventos de configuración.
- Resúmenes sin fallo y salida correcta de los supervisores en los 60 vuelos.
- Checkpoints PPO coincidentes en los 20 vuelos de esa estrategia.

El lote original se conserva localmente en
`Integrations/Px4/.runtime/evaluation20-20261103-154708`. Incluye eventos por vuelo,
snapshots, cámaras, logs y políticas. `.runtime` está ignorado por Git: para conservar
una entrega del TFM hay que archivar también estos artefactos y las dependencias
externas. Este directorio del informe conserva los datos resumidos fuera de
`.runtime`; no incluye todas las imágenes y trazas de vuelo.

| Artefacto | Contenido |
|---|---|
| [episodes.csv](episodes.csv) | Resultados de los 60 vuelos |
| [paired-analysis.json](paired-analysis.json) | Diferencias e IC sin redondeo |
| [batch.json](batch.json) | Orden, semillas, commit, hashes y códigos de salida |
| [source-mission.json](source-mission.json) | Instrucción e intención reutilizadas |
| [audit.json](audit.json) | Resumen de las verificaciones posteriores |
| [PROTOCOL.md](PROTOCOL.md) | Copia del protocolo previo al lote |
| [llm-evidence/response.json](llm-evidence/response.json) | Respuesta original del modelo |
| [llm-evidence/stdout.txt](llm-evidence/stdout.txt) | Salida original de inferencia |
| [CLARIFICATIONS.md](CLARIFICATIONS.md) | Alcance de la evidencia y aclaraciones metodológicas |

Para recalcular los resúmenes a partir del lote local:

```powershell
python Integrations/Px4/batch.py --summarize Integrations/Px4/.runtime/evaluation20-20261103-154708
```

## 9. Trabajo siguiente

El siguiente incremento experimental debe ampliar condiciones antes de ajustar
la política: diferentes K (por ejemplo, 3 y 12), plazos y repeticiones del mismo
escenario para estimar variabilidad física. Conviene fijar un protocolo separado
para cobertura a tiempo común y mantener TEST fuera de cualquier ajuste.

En producto quedan la parametrización explícita del escenario, la integración de
zonas solicitadas por el usuario y la separación del coordinador respecto a la UI.
La evaluación del intérprete LLM debe constituir otro bloque con instrucciones
correctas, ambiguas y fuera del alcance, midiendo fidelidad y comportamiento de
aclaración/rechazo.

La contribución demostrada en esta fase es un recorrido funcional de misión con
estrategias intercambiables y evidencia reproducible de menor tiempo medio de PPO
en los escenarios PX4 evaluados. No se afirma aprendizaje del control de vuelo,
generación aprendida de trayectorias ni superioridad general fuera de ese alcance.
