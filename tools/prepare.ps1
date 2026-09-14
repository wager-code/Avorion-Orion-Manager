$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($command in @('node','dotnet','npx.cmd')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Missing $command. Read 03_NEW_COMPUTER_SETUP.md first." }
}
$runtimes = & dotnet --list-runtimes
if (-not ($runtimes -match 'Microsoft.AspNetCore.App 8\.')) { throw 'Install .NET 8 SDK with ASP.NET Core 8 runtime first.' }
Push-Location $root
try {
    & (Join-Path $root 'tools\check-architecture.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Architecture boundary check failed' }
    & dotnet build 'backend\AvorionAdmin.sln' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed' }
    Push-Location (Join-Path $root 'frontend-prototype')
    try {
        & npx.cmd --yes pnpm@11.19.0 install --frozen-lockfile
        if ($LASTEXITCODE -ne 0) { throw 'Locked dependency installation failed' }
        & npm.cmd run typecheck
        if ($LASTEXITCODE -ne 0) { throw 'TypeScript check failed' }
        & npm.cmd run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
    } finally { Pop-Location }
    Write-Host 'Ready. Run 02-start.cmd to open the local management UI.'
} finally { Pop-Location }
