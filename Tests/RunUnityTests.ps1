$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$resultsPath = Join-Path $PSScriptRoot 'SimulationChecks/bin/unity-results.xml'
$logPath = Join-Path $PSScriptRoot 'SimulationChecks/bin/unity-tests.log'
New-Item -ItemType Directory -Force (Split-Path $resultsPath -Parent) | Out-Null
$editorPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
$arguments = @('-batchmode', '-nographics', '-projectPath', "`"$projectPath`"", '-runTests', '-testPlatform', 'EditMode', '-testFilter', 'LLM2Swarm.Tests', '-testResults', "`"$resultsPath`"", '-logFile', "`"$logPath`"")
$process = Start-Process -FilePath $editorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(240000)) { $process.Kill(); throw "Unity excedió cuatro minutos; consulta $logPath" }
if ($process.ExitCode -ne 0) { throw "Unity terminó con código $($process.ExitCode); consulta $logPath" }
[xml]$report = Get-Content -LiteralPath $resultsPath -Raw
$report.'test-run' | Select-Object result,total,passed,failed
if ($report.'test-run'.result -ne 'Passed') { throw 'Fallaron las pruebas de Unity.' }
