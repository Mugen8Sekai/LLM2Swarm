# Piloto ampliado de integración PX4

Protocolo fijado antes de la ejecución: 20 semillas de validación nuevas,
100003–100022, con A, B y PPO seed 47; 60 vuelos, una ejecución por combinación.
Las semillas 100000–100002 pertenecen al piloto exploratorio anterior y no se
mezclan con la muestra ampliada. TEST permanece reservado. No se ajusta PPO.

Se reutiliza una sola intención confirmada del LLM: encontrar 6 objetivos en
toda la región antes de 120 segundos. El escenario PX4 mantiene 12 objetivos
uniformes, mapa 60×40 m, tres salidas (10,0), (30,0), (50,0), sensor de 3 m,
rejilla de decisión 6×4 y alturas 5/8/11 m. Solo cambia la semilla de objetivos
entre escenarios. Cada terna comparte escenario exacto y rota el orden de métodos.
Gazebo y los vehículos se reinician en cada vuelo. No se garantiza identidad
de trayectorias físicas entre reinicios.

Métrica principal: tiempo restringido al sexto hallazgo; asignar el plazo a los
fallos de búsqueda. Publicar también éxito, distancia horizontal acumulada y
cobertura al finalizar. No interpretar mayor cobertura como victoria si un método
terminó antes. Diferencias PPO−A y PPO−B por semilla, media e IC percentil 95 %
mediante bootstrap de 20 000 muestras de semillas (RNG 20261103), exploratorios
y sin corrección múltiple. Los drones y los ticks no son réplicas independientes.

Un error de infraestructura detiene el lote y conserva resultados y logs; no se
reintenta ni se sustituye silenciosamente. Solo se analizan pares técnicamente
válidos, declarando el número de pares. Los fallos de búsqueda sí se incluyen.
El manifiesto fija el commit y los SHA256 del código de misión/integración y
checkpoint. El ejecutor verifica que no cambien antes de cada vuelo.

Salida: `batch.json`, misión fuente, peticiones por vuelo, logs y registros,
`episodes.csv`, `REPORT.md`, `paired-analysis.json` y `PAIRED.md`.
