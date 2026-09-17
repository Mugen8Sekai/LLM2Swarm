# Componentes externos del prototipo local

- **Ministral 3 3B Instruct 2512**, Mistral AI, repositorio `mistralai/Ministral-3-3B-Instruct-2512-GGUF`. La ficha oficial identifica Apache-2.0. Revisión y SHA256 del archivo descargado: `installed.json`. Fuente: https://huggingface.co/mistralai/Ministral-3-3B-Instruct-2512-GGUF
- **Qwen3 4B**, Qwen Team, repositorio `Qwen/Qwen3-4B-GGUF`. La ficha oficial identifica Apache-2.0. Revisión y SHA256 del archivo descargado: `installed-qwen3-4b.json`. Fuente: https://huggingface.co/Qwen/Qwen3-4B-GGUF
- **llama.cpp**, ggml-org, compilación `b10993`, licencia MIT del proyecto: https://github.com/ggml-org/llama.cpp/blob/b10993/LICENSE
- El paquete CUDA oficial incluye bibliotecas de NVIDIA y otros componentes con sus propios términos. Conservar los avisos incluidos en los archivos del runtime. Esta descarga de desarrollo no constituye un instalador redistribuible revisado.
- **Newtonsoft.Json** se utiliza mediante el paquete de Unity ya instalado. Su licencia se encuentra en `Library/PackageCache/com.unity.nuget.newtonsoft-json*/LICENSE.md`.

Los binarios y los pesos se descargan de sus fuentes oficiales y se excluyen de Git. Antes de publicar un instalador hay que incluir los textos de licencia y avisos exigidos por todos los componentes empaquetados.
