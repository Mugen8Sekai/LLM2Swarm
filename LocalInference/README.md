# Inferencia local — primer incremento

## Uso

1. Abrir `SampleScene` en Unity y pulsar Play.
2. Escribir: «Busca primero en el norte y después el resto. Termina al encontrar seis objetivos o pasar dos minutos».
3. Pulsar **Interpretar instrucción**. El motor se ejecuta oculto como proceso hijo, sin abrir servidores ni usar cuentas.
4. Revisar la respuesta y, especialmente, **RESUMEN VALIDADO**, que se genera a partir de los datos y no del texto libre del modelo.
5. Pulsar **Validar y preparar** y después **Iniciar** debajo del mapa.

Las respuestas de aclaración o petición no soportada no habilitan una misión. No hay corrección automática, conversación acumulada ni replanificación durante ejecución: reformular la instrucción completa e interpretar otra vez. La inferencia puede cancelarse y vence a los 120 segundos; la búsqueda no corre a la vez que se interpreta.

## Motor y modelo

- llama.cpp **b10993**, Windows x64 CUDA 12.4.
- Ministral 3 3B Instruct 2512 GGUF **Q4_K_M**, solo texto; no se descarga el proyector visual.
- Revisión del modelo y SHA256: `installed.json`.
- Contexto 4096, salida máxima 512 tokens, temperatura 0, semilla 42, gramática derivada del esquema JSON.
- Plantilla de conversación incluida en el GGUF y prompt de sistema en español.
- Cada solicitud arranca y termina un proceso. Esto evita servicios residentes y simplifica la cancelación, a costa del tiempo de carga por solicitud.

La instalación ya se ha realizado en este equipo. Para reproducirla en otra copia del proyecto, ejecutar desde PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File LocalInference/Setup.ps1
```

El script descarga unos 2,15 GB de modelo más los binarios oficiales y verifica SHA256. No modifica instalaciones globales. La inferencia usa archivos locales y `--offline`; la instalación inicial sí necesita Internet. Pesos, ejecutables y trazas voluminosas se excluyen de Git.

Para una build de escritorio, colocar `LocalInference/runtime`, `LocalInference/models` e `installed.json` junto al ejecutable; `Assets/StreamingAssets/Inference` se incluye mediante Unity. El componente `SimulationRunner` permite cambiar `gpuLayers` (99 por defecto; 0 para probar CPU). Solo se ha medido CUDA en la RTX 3090; no se ha validado CPU ni construido un instalador. Consultar `THIRD_PARTY_NOTICES.md` antes de distribuir binarios.

## Contrato y capacidades

`Interpretation` contiene `status` (`proposal`, `clarification`, `unsupported`), `message` y `mission` (null si no es una propuesta). Una misión contiene exclusivamente `requiredFindings`, `maxSeconds` y `areas`. No altera semilla, objetivos existentes, sensores, velocidad, altura ni geometría del escenario. El validador comprueba el JSON y los valores; no puede demostrar por sí solo fidelidad al lenguaje natural.

Áreas: `all`, o una secuencia de hasta tres tercios `north`, `center`, `south`. Norte y resto se normaliza a norte → centro → sur. No se admiten zonas arbitrarias, cobertura como condición de fase, modificaciones durante ejecución, retorno a base o cambios de capacidades.

## Comprobaciones reproducibles

Desde la raíz del proyecto, con SDK .NET 8 y paquetes de Unity resueltos:

```powershell
dotnet run --project Tests/SimulationChecks
dotnet run --project Tests/SimulationChecks -- evaluate
dotnet run --project Tests/SimulationChecks -- evaluate holdout-cases.json
```

En este equipo el SDK está en `C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/DotNetSdk/dotnet.exe`.

Resultados preliminares: 20/30 con el prompt inicial, 30/30 tras ajustarlo sobre esos mismos casos y **9/12 con órdenes nuevas sin ajustes posteriores**. La última batería tarda de media unos 2,31 s por interpretación, incluyendo arranque y carga, en una RTX 3090. No es una comparación de modelos ni una estimación estadística de fiabilidad.

Fallos de la batería nueva: aclaración innecesaria ante «peinen», conversión incorrecta de un cuarto de minuto (30 s en lugar de 15 s) y rechazo de «peina el mapa». La revisión humana es necesaria; no se debe confundir JSON válido con comprensión correcta. Los resultados detallados están en `evaluation-report.json`; las trazas completas de cada ejecución permanecen en `evaluation-results` (no versionado). Se deben reservar nuevos casos si se continúa ajustando el prompt.

Se han ejecutado **9 pruebas EditMode dentro de Unity, todas correctas**, incluida una llamada real a Ministral desde el editor seguida de planificación/ejecución, y la cancelación del proceso local. Las comprobaciones adicionales del motor y las baterías de órdenes se ejecutaron desde el harness .NET. La interacción visual de esta revisión queda por comprobar en modo Play.

Con Unity cerrado, `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/RunUnityTests.ps1` repite la prueba en modo batch oculto. El informe NUnit queda en `Tests/SimulationChecks/bin/unity-results.xml`. Los dos tests de integración local se omiten en equipos sin los pesos instalados.
