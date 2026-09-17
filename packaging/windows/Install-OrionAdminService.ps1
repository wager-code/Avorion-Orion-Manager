[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')]
    [string]$ServiceName = 'OrionAdmin',
    [ValidateLength(1, 255)]
    [string]$DisplayName = 'OrionAdmin Avorion Manager',
    [ValidateRange(1024, 65535)]
    [int]$Port = 5088,
    [string]$DataDirectory = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'OrionAdmin\data'),
    [switch]$NoStart,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$packageRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSCommandPath))
$executablePath = [System.IO.Path]::GetFullPath((Join-Path $packageRoot 'AvorionAdmin.Api.exe'))
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable is missing: $executablePath"
}
if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
    throw 'DataDirectory cannot be empty.'
}
$dataFullPath = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($DataDirectory))
$listenUrl = "http://127.0.0.1:$Port"
$binaryPath = ('"{0}" --urls "{1}" --Avorion:DataDirectory="{2}"' -f $executablePath, $listenUrl, $dataFullPath)

$configuration = [pscustomobject]@{
    mode = if ($ValidateOnly) { 'validate-only' } else { 'install' }
    serviceName = $ServiceName
    displayName = $DisplayName
    executablePath = $executablePath
    listenUrl = $listenUrl
    dataDirectory = $dataFullPath
    binaryPath = $binaryPath
    startMode = 'delayed-auto'
}

if ($ValidateOnly) {
    $configuration | ConvertTo-Json -Depth 3
    return
}

if (-not (Test-IsAdministrator)) {
    throw 'Run this script from an elevated PowerShell window (Run as administrator).'
}
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Service '$ServiceName' already exists. Use Uninstall-OrionAdminService.ps1 after safely stopping the Avorion server, then install the new package."
}
if (-not $NoStart -and (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
    throw "Port $Port is already occupied. No service was created and no process was stopped."
}

New-Item -ItemType Directory -Path $dataFullPath -Force | Out-Null

if ($PSCmdlet.ShouldProcess($ServiceName, "Create delayed-auto Windows service using $executablePath")) {
    $createOutput = & sc.exe create $ServiceName binPath= $binaryPath start= delayed-auto type= own DisplayName= $DisplayName 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe create failed ($LASTEXITCODE): $($createOutput -join ' ')"
    }

    $descriptionOutput = & sc.exe description $ServiceName 'Local-only OrionAdmin manager for an Avorion dedicated server.' 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Service was created, but setting its description failed ($LASTEXITCODE): $($descriptionOutput -join ' ')"
    }
}

if ($NoStart) {
    Write-Host "SERVICE_INSTALLED $ServiceName (not started)"
    Write-Host "Persistent data: $dataFullPath"
    return
}

Start-Service -Name $ServiceName
$service = Get-Service -Name $ServiceName
$service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))

$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    try {
        $health = Invoke-RestMethod "$listenUrl/api/v1/health" -TimeoutSec 2
        if ($health.status -eq 'ok') {
            $ready = $true
            break
        }
    } catch {
        Start-Sleep -Milliseconds 500
    }
}
if (-not $ready) {
    throw "Service is running, but OrionAdmin health did not become ready at $listenUrl. Check Event Viewer > Windows Logs > Application (Source: OrionAdmin)."
}

Write-Host "SERVICE_INSTALL_PASS $ServiceName"
Write-Host "Open: $listenUrl/server/control"
Write-Host "Persistent data: $dataFullPath"
