$ErrorActionPreference = 'Stop'
$basePath = $PSScriptRoot
$releaseTag = 'b10993'
$release = Invoke-RestMethod "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/$releaseTag"
New-Item -ItemType Directory -Force -Path "$basePath/downloads", "$basePath/runtime", "$basePath/models" | Out-Null
foreach ($name in @("llama-$releaseTag-bin-win-cuda-12.4-x64.zip", 'cudart-llama-bin-win-cuda-12.4-x64.zip')) {
    $asset = $release.assets | Where-Object name -eq $name
    if (!$asset -or !$asset.digest.StartsWith('sha256:')) { throw "Falta el archivo o su SHA256: $name" }
    $archive = Join-Path "$basePath/downloads" $name
    if (!(Test-Path $archive)) {
        & curl.exe --fail --location --retry 3 --output $archive $asset.browser_download_url
        if ($LASTEXITCODE -ne 0) { throw "Descarga fallida: $name" }
    }
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset.digest.Substring(7)) { throw "SHA256 incorrecto: $name" }
    Expand-Archive -LiteralPath $archive -DestinationPath "$basePath/runtime" -Force
}
$repo = 'mistralai/Ministral-3-3B-Instruct-2512-GGUF'
$revision = 'eb599d4'
$modelName = 'Ministral-3-3B-Instruct-2512-Q4_K_M.gguf'
$info = Invoke-RestMethod "https://huggingface.co/api/models/$repo/revision/${revision}?blobs=true"
$modelInfo = $info.siblings | Where-Object rfilename -eq $modelName
$modelPath = Join-Path "$basePath/models" $modelName
if (!(Test-Path $modelPath)) {
    & curl.exe --fail --location --retry 3 --output $modelPath "https://huggingface.co/$repo/resolve/$($info.sha)/$modelName"
    if ($LASTEXITCODE -ne 0) { throw 'Descarga del modelo fallida.' }
}
$hash = (Get-FileHash $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $modelInfo.lfs.sha256) { throw 'SHA256 del modelo incorrecto.' }
@{ runtime=$releaseTag; model=$repo; revision=$info.sha; file=$modelName; sha256=$hash; quantization='Q4_K_M' } |
    ConvertTo-Json | Set-Content -Encoding utf8 "$basePath/installed.json"
Write-Output 'Motor y modelo instalados y verificados.'
