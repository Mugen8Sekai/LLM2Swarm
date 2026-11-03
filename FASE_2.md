# Fase 2: sistema integrado de misiones

## Punto de partida

La Fase 1 queda conservada en el commit `093eade` y la etiqueta `fase-1`.
El desarrollo de integración comienza en `codex/fase-2-integracion`.
La etiqueta conserva los archivos versionados; no incluye modelos, entornos,
dependencias ni registros ignorados por Git. No es una copia completa del entorno.
Las fases experimentales anteriores mantienen su numeración y sus resultados.

## Objetivo del TFM

Convertir una instrucción en una misión validada, ejecutarla con una estrategia
de búsqueda y registrar resultados trazables, mediante un único flujo de usuario:

**Instrucción → intención → validación → preparación → ejecución → resultados.**

El LLM interpreta la intención; la estrategia decide las asignaciones; el
planificador produce tareas; el backend ejecuta; Unity presenta y controla
el ciclo de misión. Ninguna estrategia recibe posiciones de objetivos ocultos.

## Diagnóstico inicial

- `LLM2SwarmAppShell` alterna dos componentes con ciclos de vida independientes.
- `SimulationRunner` combina presentación, interpretación, ejecución y exportación;
  conserva caminos diferentes para misiones manuales y estrategias experimentales.
- `SearchExperiment` aporta observaciones y estrategias comunes para búsqueda.
- PX4 dispone de un host y un contrato de ejecución, pero su escenario y estrategia
  están fijados según el alcance de la integración original.
- Las misiones por sectores y el experimento de búsqueda tienen distinta semántica
  de detección. Debe hacerse explícita antes de comparar resultados.
- Los README históricos contienen afirmaciones que han quedado desactualizadas;
  deben revisarse junto con cada funcionalidad integrada.

## Entregas y aceptación

### 1. Contrato y ciclo de misión comunes

Definir una especificación versionada con escenario, intención validada,
estrategia y configuración, backend, política de detección y procedencia.
Separar configuración de ejecución, observación pública y datos privilegiados
de evaluación. Reutilizar los contratos existentes cuando encajen.

Definir estados y transiciones: borrador, preparada, iniciando, ejecutando,
finalizando, completada, interrumpida y fallida. Pausa debe declararse como
capacidad del backend: no asumir que pausar 2D equivale a pausar PX4.

Aceptación: validación de capacidades antes del arranque, rechazo de combinaciones
no admitidas y pruebas de transiciones, invalidación del borrador y aislamiento
de objetivos ocultos. La primera integración admite `areas: ["all"]`;
las peticiones por sectores se rechazan explícitamente hasta implementarlas.

### 2. Primer recorrido completo en Fast 2D

Conectar el intérprete local, el contrato común y baseline A a un coordinador
de misión independiente de la UI. Ejecutar desde una sola pantalla y exportar
configuración, evidencia del intérprete, eventos y métricas bajo un identificador.

Aceptación: una instrucción llega a resultados sin cambiar de laboratorio;
editarla invalida la preparación; el inicio requiere una misión validada;
interrumpir produce un resultado consistente; la repetición conserva los
resultados deterministas de la referencia con la misma configuración.

### 3. Estrategias intercambiables

Incorporar baseline B y la política PPO existente mediante el mismo contrato.
Registrar versión/huella de política y comprobar dimensiones y supuestos de
observación. Las rutas manuales se admiten como una capacidad explícita.

Aceptación: cambiar la estrategia no cambia el escenario ni el evaluador;
una política incompatible se rechaza antes de ejecutar. No entrenar otra
política ni utilizar TEST para ajustar la integración.

### 4. PX4/Gazebo como backend del mismo recorrido

Parametrizar el host a partir de la misión preparada, conservando validación,
telemetría medida, confirmación de tareas y cierre de los procesos propios.
Adaptar los estados y snapshots a la presentación común. Incorporar estrategias
progresivamente; no anunciar paridad de capacidades antes de verificarla.

Aceptación: baseline A ejecuta una misión preparada desde el mismo flujo;
se verifican inicio, finalización, interrupción y pérdida de telemetría.
Las métricas declaran reloj, detección y dinámica del backend: no se exige
igualdad de trayectorias entre movimiento ideal y PX4.

### 5. Evaluación integrada y demostración del TFM

Preparar escenarios reproducibles y comparaciones emparejadas de estrategias,
separando calidad de interpretación, rendimiento de búsqueda y efecto del backend.
Conservar las versiones históricas necesarias para reproducir los pilotos.

Aceptación: documentación actualizada, demostración de extremo a extremo,
artefactos trazables y protocolo de evaluación fijado antes de usar TEST final.

## Próximo incremento

Implementar las entregas 1 y 2 como primer corte vertical. La integración completa
es el objetivo de esta fase; este documento prepara el trabajo y no afirma
que los componentes ya estén unificados.
