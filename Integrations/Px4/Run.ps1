param([ValidateSet('smoke','slice')][string]$Mode='slice')
$ErrorActionPreference='Stop'
$repoPath=(Resolve-Path "$PSScriptRoot/../..").Path
$dotnetPath='C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/DotNetSdk/dotnet.exe'
function Check-Frozen {
    $count=0
    foreach($manifest in @('Experiments/ppo/frozen.json','Experiments/ppo/report/artifact-hashes.json')) {
        $entries=Get-Content -LiteralPath "$repoPath/$manifest" -Raw | ConvertFrom-Json
        foreach($entry in $entries.PSObject.Properties) {
            if((Get-FileHash -LiteralPath "$repoPath/$($entry.Name)" -Algorithm SHA256).Hash -ne $entry.Value){throw "Frozen artifact changed: $($entry.Name)"}
            $count++
        }
    }
    Write-Host "Frozen artifacts: $count verified"
}
Check-Frozen
& $dotnetPath run --project "$PSScriptRoot/Host" -- test
if($LASTEXITCODE -ne 0){throw 'Contract tests failed'}
$runPath=Join-Path $PSScriptRoot ('.runtime/'+$Mode+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runPath -Force | Out-Null
$linuxRepo='/mnt/'+$repoPath.Substring(0,1).ToLower()+$repoPath.Substring(2).Replace('\','/')
$linuxRun='/mnt/'+$runPath.Substring(0,1).ToLower()+$runPath.Substring(2).Replace('\','/')
$vehicles=if($Mode -eq 'smoke'){1}else{3}
$smokeFlag=if($Mode -eq 'smoke'){'--smoke'}else{''}
$arguments="-d LLM2Swarm-PX4 -u root -- /opt/llm2swarm/venv/bin/python `"$linuxRepo/Integrations/Px4/bridge.py`" --vehicles $vehicles $smokeFlag --output `"$linuxRun`""
$process=Start-Process wsl.exe -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput "$runPath/bridge.stdout.log" -RedirectStandardError "$runPath/bridge.stderr.log"
# Retain the native handle while the host runs, including if WSL exits before WaitForExit.
$processHandle=$process.Handle
try {
    if($Mode -eq 'slice') {
        $deadline=[DateTime]::UtcNow.AddSeconds(150)
        while(!(Test-Path "$runPath/session.json")) {
            if($process.HasExited){throw "Bridge exited; inspect $runPath/bridge.stderr.log"}
            if([DateTime]::UtcNow -gt $deadline){throw 'Bridge startup timeout'}
            Start-Sleep -Milliseconds 250
        }
        & $dotnetPath run --no-build --project "$PSScriptRoot/Host" -- run "$runPath/session.json" "$runPath"
        if($LASTEXITCODE -ne 0){throw 'Mission host failed'}
    }
    if(!$process.WaitForExit(240000)){throw 'Supervisor timeout'}
    $nativeExit=$process.ExitCode
    @{exitCode=$nativeExit;hasExited=$process.HasExited} | ConvertTo-Json | Set-Content "$runPath/launcher-result.json"
    if($null -eq $nativeExit -or $nativeExit -ne 0){throw "Bridge exit [$nativeExit]; inspect $runPath/bridge.stderr.log"}
} finally {
    if(!$process.HasExited -and (Test-Path "$runPath/supervisor.json")) {
        $owned=Get-Content "$runPath/supervisor.json" -Raw | ConvertFrom-Json
        & wsl -d LLM2Swarm-PX4 -u root -- kill -TERM $owned.pid
        $process.WaitForExit(65000) | Out-Null
    }
    Check-Frozen
    Write-Host "Artifacts: $runPath"
}
