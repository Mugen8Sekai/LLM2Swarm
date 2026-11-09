# Estado conservado de Fast 2D y PPO — 17 septiembre 2026

Referencia Git: `snapshot/fast2d-ppo-phase4-20260917`.

Este snapshot conserva el estado existente, sin reentrenamiento ni reformulación:
simulador, sensores, contrato de estrategia, baselines A/B, protocolo, configuración
PPO, código de entrenamiento/evaluación, resultados emparejados y reportes.
Incluye los seis entrenamientos (seeds 11/29/47, recompensa completa y sin cobertura),
los 42 archivos `.pt` (iniciales, intermedios, últimos y seleccionados), políticas
exportadas y registros de episodios/validación. También conserva los resultados
anteriores de los baselines y los cambios existentes de integración Qwen.

`state-20260917.sha256.json` registra los bytes de los archivos versionados de
`Assets/Swarm`, `Tests/SimulationChecks` y `Experiments` en este estado. No se incluye
a sí mismo. Las reglas Git evitan normalizar saltos de línea en estos directorios;
los checkpoints se guardan directamente, sin depender de un servidor Git LFS.
Los dos manifiestos de congelación originales no se modifican.

Se excluyen cachés, `.venv`, binarios regenerables, modelos LLM descargados y runtime
PX4/WSL. No contienen cambios de política aprendida; las dependencias Python del
experimento están fijadas en `ppo/requirements-lock.txt`.

El commit y la etiqueta son locales. No se ha realizado push ni copia externa.
Para consultar el estado sin modificar el directorio de trabajo:

```powershell
git show snapshot/fast2d-ppo-phase4-20260917:Experiments/ppo/report/REPORT.md
```

La recuperación debe partir de esta referencia, no volver a generar el experimento
para intentar reproducir sus pesos. El directorio `results` está ahora versionado;
los resultados históricos permanecen disponibles aunque se hagan cambios futuros.
