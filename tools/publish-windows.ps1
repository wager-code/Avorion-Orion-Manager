[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [switch]$SkipDependencyInstall,
    [switch]$SkipFrontendBuild,
    [switch]$SmokeTest,
    [ValidateRange(1024, 65535)][int]$SmokePort = 5188
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$frontend = Join-Path $root 'frontend-prototype'
$frontendDist = Join-Path $frontend 'dist\client'
$apiProject = Join-Path $root 'backend\src\AvorionAdmin.Api\AvorionAdmin.Api.csproj'
$packageTemplate = Join-Path $root 'packaging\windows'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'artifacts\OrionAdmin-win-x64'
}
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputFullPath) {
    throw "Output already exists: $outputFullPath. Move or remove it explicitly before publishing."
}
$outputParent = Split-Path -Parent $outputFullPath
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
$staging = "$outputFullPath.staging-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $staging | Out-Null
$published = $false

try {
    foreach ($command in @('dotnet', 'node', 'npx.cmd')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Missing build command: $command"
        }
    }

    Push-Location $frontend
    try {
        if (-not $SkipDependencyInstall) {
            & npx.cmd --yes pnpm@11.19.0 install --frozen-lockfile
            if ($LASTEXITCODE -ne 0) { throw 'Locked frontend dependency installation failed' }
        }
        if (-not $SkipFrontendBuild) {
            & npm.cmd run typecheck
            if ($LASTEXITCODE -ne 0) { throw 'Frontend type-check failed' }
            & npm.cmd run build
            if ($LASTEXITCODE -ne 0) { throw 'Frontend production build failed' }
        }
    } finally {
        Pop-Location
    }

    $frontendIndex = Join-Path $frontendDist 'index.html'
    if (-not (Test-Path -LiteralPath $frontendIndex -PathType Leaf)) {
        throw "Frontend build output is missing: $frontendIndex"
    }

    & dotnet publish $apiProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained API publish failed' }

    $wwwroot = Join-Path $staging 'wwwroot'
    New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
    Copy-Item -Path (Join-Path $frontendDist '*') -Destination $wwwroot -Recurse -Force
    foreach ($file in @('Start-OrionAdmin.cmd', 'Open-OrionAdmin.ps1', 'README.txt')) {
        Copy-Item -LiteralPath (Join-Path $packageTemplate $file) -Destination (Join-Path $staging $file)
    }

    foreach ($required in @('AvorionAdmin.Api.exe', 'wwwroot\index.html', 'Start-OrionAdmin.cmd', 'Open-OrionAdmin.ps1', 'README.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $staging $required) -PathType Leaf)) {
            throw "Published package is missing: $required"
        }
    }

    Move-Item -LiteralPath $staging -Destination $outputFullPath
    $published = $true
} finally {
    if (-not $published -and (Test-Path -LiteralPath $staging)) {
        Write-Warning "Incomplete staging directory was preserved for diagnosis: $staging"
    }
}

if ($SmokeTest) {
    if (Get-NetTCPConnection -LocalPort $SmokePort -State Listen -ErrorAction SilentlyContinue) {
        throw "Smoke-test port $SmokePort is already occupied. No process was stopped."
    }
    $smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("orion-package-smoke-" + [Guid]::NewGuid().ToString('N'))
    $smokeRoot = [System.IO.Path]::GetFullPath($smokeRoot)
    New-Item -ItemType Directory -Path $smokeRoot | Out-Null
    $logs = Join-Path $smokeRoot 'logs'
    New-Item -ItemType Directory -Path $logs | Out-Null
    $previousData = $env:Avorion__DataDirectory
    $process = $null
    try {
        $env:Avorion__DataDirectory = Join-Path $smokeRoot 'data'
        $process = Start-Process -FilePath (Join-Path $outputFullPath 'AvorionAdmin.Api.exe') -WorkingDirectory $outputFullPath -ArgumentList @('--urls', "http://127.0.0.1:$SmokePort") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs 'api.out.log') -RedirectStandardError (Join-Path $logs 'api.err.log')
        $ready = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            $process.Refresh()
            if ($process.HasExited) { throw "Published API exited during smoke test. Check $logs" }
            try {
                $health = Invoke-RestMethod "http://127.0.0.1:$SmokePort/api/v1/health" -TimeoutSec 2
                $page = Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$SmokePort/server/control" -TimeoutSec 2
                if ($health.status -eq 'ok' -and $page.StatusCode -eq 200 -and $page.Content -match 'id="root"') {
                    $ready = $true
                    break
                }
            } catch {
                Start-Sleep -Milliseconds 500
            }
        }
        if (-not $ready) { throw "Published package smoke test timed out. Check $logs" }
        Write-Host "PACKAGE_SMOKE_PASS $outputFullPath"
    } finally {
        if ($null -ne $process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
                $process.WaitForExit(5000) | Out-Null
            }
        }
        $env:Avorion__DataDirectory = $previousData
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if ($smokeRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $smokeRoot).StartsWith('orion-package-smoke-', [System.StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $smokeRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "Windows package ready: $outputFullPath"
