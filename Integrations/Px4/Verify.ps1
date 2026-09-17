$ErrorActionPreference='Stop'
$repoPath=(Resolve-Path "$PSScriptRoot/../..").Path
$count=0
foreach($manifest in @('Experiments/ppo/frozen.json','Experiments/ppo/report/artifact-hashes.json')) {
    $entries=Get-Content "$repoPath/$manifest" -Raw | ConvertFrom-Json
    foreach($entry in $entries.PSObject.Properties) {
        if((Get-FileHash -LiteralPath "$repoPath/$($entry.Name)" -Algorithm SHA256).Hash -ne $entry.Value){throw "Frozen artifact changed: $($entry.Name)"}
        $count++
    }
}
Write-Host "Frozen artifacts: $count verified"
& 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/DotNetSdk/dotnet.exe' run --project "$PSScriptRoot/Host" -- test
if($LASTEXITCODE -ne 0){throw 'Host tests failed'}
$linuxRepo='/mnt/'+$repoPath.Substring(0,1).ToLower()+$repoPath.Substring(2).Replace('\','/')
& wsl -d LLM2Swarm-PX4 -u root -- /opt/llm2swarm/venv/bin/python "$linuxRepo/Integrations/Px4/test_bridge.py"
if($LASTEXITCODE -ne 0){throw 'Bridge tests failed'}
& wsl -d LLM2Swarm-PX4 -u root -- bash -lc 'cat /etc/os-release; cat /opt/llm2swarm/px4-commit.txt; cat /opt/llm2swarm/python-lock.txt; cat /opt/llm2swarm/gazebo-packages.txt; ps -eo pid,pgid,comm,args'
if($LASTEXITCODE -ne 0){throw 'Environment inspection failed'}
