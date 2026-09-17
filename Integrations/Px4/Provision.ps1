$ErrorActionPreference = 'Stop'
$distro = 'LLM2Swarm-PX4'
$runtime = Join-Path $PSScriptRoot '.runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
$names = ((wsl --list --quiet | Out-String).Replace([string][char]0,'') -split '\r?\n') | ForEach-Object { $_.Trim() }
if ($names -notcontains $distro) {
    $base = 'https://cloud-images.ubuntu.com/wsl/releases/24.04/current/'
    $archive = 'ubuntu-noble-wsl-amd64-wsl.rootfs.tar.gz'
    & curl.exe --fail --location --retry 3 --output (Join-Path $runtime 'SHA256SUMS') ($base + 'SHA256SUMS')
    if ($LASTEXITCODE -ne 0) { throw 'Checksum download failed' }
    & curl.exe --fail --location --retry 3 --output (Join-Path $runtime $archive) ($base + $archive)
    if ($LASTEXITCODE -ne 0) { throw 'Rootfs download failed' }
    $line = Get-Content (Join-Path $runtime 'SHA256SUMS') | Where-Object { $_.EndsWith($archive) }
    $expected = ($line -split '\s+')[0]
    if ((Get-FileHash (Join-Path $runtime $archive) -Algorithm SHA256).Hash.ToLower() -ne $expected) { throw 'Rootfs checksum mismatch' }
    wsl --import $distro (Join-Path $runtime 'wsl') (Join-Path $runtime $archive) --version 2
    if ($LASTEXITCODE -ne 0) { throw 'WSL import failed' }
}
$linuxScript = '/mnt/c/Users/mugen/LLM2Swarm/Integrations/Px4/provision.sh'
wsl -d $distro -u root -- bash $linuxScript
if ($LASTEXITCODE -ne 0) { throw 'Dedicated distro provisioning failed; inspect output' }
