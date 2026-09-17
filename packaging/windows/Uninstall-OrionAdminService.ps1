[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')]
    [string]$ServiceName = 'OrionAdmin',
    [ValidateRange(1024, 65535)]
    [int]$Port = 5088,
    [switch]$ConfirmGameServerStopped,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$listenUrl = "http://127.0.0.1:$Port"
$configuration = [pscustomobject]@{
    mode = if ($ValidateOnly) { 'validate-only' } else { 'uninstall' }
    serviceName = $ServiceName
    statusUrl = "$listenUrl/api/v1/servers/local/status"
    dataPolicy = 'preserve'
    unverifiedStopRequires = '-ConfirmGameServerStopped'
}

if ($ValidateOnly) {
    $configuration | ConvertTo-Json -Depth 3
    return
}

if (-not (Test-IsAdministrator)) {
    throw 'Run this script from an elevated PowerShell window (Run as administrator).'
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Service '$ServiceName' is not installed. Persistent data was not changed."
    return
}

if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
    $serverStatus = $null
    try {
        $serverStatus = Invoke-RestMethod "$listenUrl/api/v1/servers/local/status" -TimeoutSec 5
    } catch {
        if (-not $ConfirmGameServerStopped) {
            throw "Could not verify the Avorion game server state at $listenUrl. Safely stop the game server first, then retry with -ConfirmGameServerStopped only after verifying it is stopped."
        }
    }

    if ($null -ne $serverStatus) {
        $lifecycle = [string]$serverStatus.lifecycle
        if (-not [string]::Equals($lifecycle, 'stopped', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to stop OrionAdmin because the managed Avorion server lifecycle is '$lifecycle'. Use the UI safe shutdown and wait for 'stopped'."
        }
    }

    if ($PSCmdlet.ShouldProcess($ServiceName, 'Gracefully stop Windows service')) {
        Stop-Service -Name $ServiceName -ErrorAction Stop
        $service = Get-Service -Name $ServiceName
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    }
}

if ($PSCmdlet.ShouldProcess($ServiceName, 'Delete Windows service registration while preserving data')) {
    $deleteOutput = & sc.exe delete $ServiceName 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe delete failed ($LASTEXITCODE): $($deleteOutput -join ' ')"
    }
}

$dataFullPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'OrionAdmin\data'
Write-Host "SERVICE_UNINSTALL_PASS $ServiceName"
Write-Host "Persistent data was preserved: $dataFullPath"
