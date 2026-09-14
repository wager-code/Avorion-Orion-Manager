param([string]$RunId = (Get-Date -Format "yyyyMMdd-HHmmss"), [int]$ApiPort = 5188, [int]$WebPort = 4273)
$ErrorActionPreference="Stop"
$root=Split-Path -Parent $PSScriptRoot
$run=Join-Path $root ("qa/harness/runs/"+$RunId)
foreach($d in @("data","db","steamcmd","avorion-server","reports","screenshots","logs")){New-Item -ItemType Directory -Force (Join-Path $run $d)|Out-Null}
$api=Join-Path $root "../backend/src/AvorionAdmin.Api/bin/Release/net8.0/AvorionAdmin.Api.exe"
$vite=Join-Path $root "node_modules/vite/bin/vite.js"
$node=(Get-Command node).Source
$env:AVORION_API_TARGET="http://127.0.0.1:$ApiPort"; $env:Avorion__DataDirectory=Join-Path $run "data"
$apiP=Start-Process $api -WorkingDirectory (Split-Path $api) -ArgumentList @("--urls","http://127.0.0.1:$ApiPort") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run "logs/api.out.log") -RedirectStandardError (Join-Path $run "logs/api.err.log")
$webP=Start-Process $node -WorkingDirectory $root -ArgumentList @(("`"$vite`""),"--host","127.0.0.1","--port",$WebPort,"--strictPort") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run "logs/web.out.log") -RedirectStandardError (Join-Path $run "logs/web.err.log")
[pscustomobject]@{runId=$RunId;runRoot=$run;apiPort=$ApiPort;webPort=$WebPort;apiPid=$apiP.Id;webPid=$webP.Id}|ConvertTo-Json|Set-Content (Join-Path $run "harness.json")
Write-Output (Join-Path $run "harness.json")
