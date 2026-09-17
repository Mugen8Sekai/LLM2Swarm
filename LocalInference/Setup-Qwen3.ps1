$ErrorActionPreference = 'Stop'
$basePath = $PSScriptRoot
$repo = 'Qwen/Qwen3-4B-GGUF'
$revision = 'bc640142c66e1fdd12af0bd68f40445458f3869b'
$modelName = 'Qwen3-4B-Q4_K_M.gguf'
$manifestName = 'installed-qwen3-4b.json'
New-Item -ItemType Directory -Force -Path "$basePath/models" | Out-Null
$info = Invoke-RestMethod "https://huggingface.co/api/models/$repo/revision/${revision}?blobs=true"
$modelInfo = $info.siblings | Where-Object rfilename -eq $modelName
if (!$modelInfo -or !$modelInfo.lfs.sha256) { throw "No se encontró $modelName o su SHA256." }
$modelPath = Join-Path "$basePath/models" $modelName
if (!(Test-Path $modelPath)) {
    & curl.exe --fail --location --retry 3 --output $modelPath "https://huggingface.co/$repo/resolve/$($info.sha)/$modelName"
    if ($LASTEXITCODE -ne 0) { throw 'Descarga del modelo Qwen fallida.' }
}
$hash = (Get-FileHash $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $modelInfo.lfs.sha256) { throw 'SHA256 del modelo Qwen incorrecto.' }
@{ runtime='b10993'; model=$repo; revision=$info.sha; file=$modelName; sha256=$hash; quantization='Q4_K_M' } |
    ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $basePath $manifestName)
Write-Output 'Qwen3 4B instalado y verificado.'
