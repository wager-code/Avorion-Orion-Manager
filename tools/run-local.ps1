param(
    [ValidateRange(1024,65535)][int]$ApiPort = 5088,
    [ValidateRange(1024,65535)][int]$WebPort = 4173,
    [switch]$SmokeTest
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$apiDirectory = Join-Path $root 'backend\src\AvorionAdmin.Api'
$webDirectory = Join-Path $root 'frontend-prototype'
$apiExecutable = Join-Path $apiDirectory 'bin\Release\net8.0\AvorionAdmin.Api.exe'
$vite = Join-Path $webDirectory 'node_modules\vite\bin\vite.js'
$node = (Get-Command node -ErrorAction Stop).Source
if (-not (Test-Path $apiExecutable) -or -not (Test-Path $vite)) { throw 'Run 01-prepare.cmd first.' }
if ($ApiPort -eq $WebPort) { throw 'API and Web ports must be different.' }
foreach ($port in @($ApiPort,$WebPort)) {
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw "Port $port is already occupied. No process was stopped." }
}
$local = Join-Path $root '.local'
$logs = Join-Path $local 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$owned = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$previousTarget = $env:AVORION_API_TARGET
$previousData = $env:Avorion__DataDirectory
try {
    $env:AVORION_API_TARGET = "http://127.0.0.1:$ApiPort"
    $env:Avorion__DataDirectory = Join-Path $local 'data'
    $api = Start-Process -FilePath $apiExecutable -WorkingDirectory $apiDirectory -ArgumentList @('--urls',"http://127.0.0.1:$ApiPort") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs "$stamp-api.out.log") -RedirectStandardError (Join-Path $logs "$stamp-api.err.log")
    $owned.Add($api)
    $web = Start-Process -FilePath $node -WorkingDirectory $webDirectory -ArgumentList @(('"'+$vite+'"'),'--host','127.0.0.1','--port',"$WebPort",'--strictPort') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs "$stamp-web.out.log") -RedirectStandardError (Join-Path $logs "$stamp-web.err.log")
    $owned.Add($web)
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        foreach ($process in $owned) { if ($process.HasExited) { throw "Service exited. Check $logs" } }
        try {
            $health = Invoke-RestMethod "http://127.0.0.1:$WebPort/api/v1/health" -TimeoutSec 2
            $page = Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$WebPort/server/control" -TimeoutSec 2
            if ($health.status -eq 'ok' -and $page.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "Startup timeout. Check $logs" }
    Write-Host "API and frontend proxy verified. Open http://127.0.0.1:$WebPort/server/control"
    if ($SmokeTest) { Write-Host 'SMOKE_PASS'; return }
    Start-Process "http://127.0.0.1:$WebPort/server/control"
    Write-Host 'Keep this window running. Ctrl+C stops only the API and frontend started here.'
    Write-Host 'If you start an Avorion game server, shut it down safely in the UI before stopping the Agent.'
    while ($true) {
        Start-Sleep -Seconds 3
        foreach ($process in $owned) { if ($process.HasExited) { throw "Service exited. Check $logs" } }
    }
} finally {
    foreach ($process in $owned) { if (-not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue } }
    $env:AVORION_API_TARGET = $previousTarget
    $env:Avorion__DataDirectory = $previousData
}
