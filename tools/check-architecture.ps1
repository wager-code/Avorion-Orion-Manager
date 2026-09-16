$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Require-File([string]$relativePath) {
    $path = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required module: $relativePath"
    }
    return $path
}

function Require-MaxLines([string]$relativePath, [int]$maximum) {
    $path = Require-File $relativePath
    $lines = (Get-Content -LiteralPath $path).Count
    if ($lines -gt $maximum) {
        throw "$relativePath has $lines lines; split it before exceeding the $maximum-line boundary."
    }
    return $path
}

$program = Require-MaxLines 'backend\src\AvorionAdmin.Api\Program.cs' 100
$programSource = Get-Content -LiteralPath $program -Raw
if ($programSource -match 'app\.Map(Get|Post|Put|Patch|Delete)\s*\(') {
    throw 'Program.cs contains an inline HTTP endpoint. Move it to Endpoints/.'
}
if (Test-Path -LiteralPath (Join-Path $projectRoot 'backend\src\AvorionAdmin.Api\ApiSupport.cs')) {
    throw 'ApiSupport.cs must not return; place support types in Security, Capabilities, Operations or Infrastructure.'
}

$endpointModules = @(
    'SecurityEndpoints.cs',
    'ManagementBridgeEndpoints.cs',
    'PlayerEndpoints.cs',
    'AllianceEndpoints.cs',
    'GameManagementEndpoints.cs',
    'InventoryEndpoints.cs',
    'InventoryCatalogEndpoints.cs',
    'ServerOverviewEndpoints.cs',
    'PerformanceEndpoints.cs',
    'SectorEndpoints.cs',
    'ProvisioningEndpoints.cs',
    'ProvisioningEndpoints.UpdateEnvironment.cs',
    'ProvisioningEndpoints.Installation.cs',
    'ProvisioningEndpoints.ServerSetup.cs',
    'ProvisioningEndpoints.FileSystem.cs',
    'AutomationEndpoints.cs',
    'BackupEndpoints.cs',
    'LogEndpoints.cs',
    'DiagnosticEndpoints.cs',
    'ServerControlEndpoints.cs',
    'UpdateCommandEndpoints.cs'
)
foreach ($module in $endpointModules) {
    Require-MaxLines "backend\src\AvorionAdmin.Api\Endpoints\$module" 550 | Out-Null
}

$app = Require-MaxLines 'frontend-prototype\src\App.tsx' 120
if ((Get-Content -LiteralPath $app -Raw) -match '<Route\b') {
    throw 'App.tsx contains page routes. Keep them in src/app/AppRoutes.tsx.'
}
Require-File 'frontend-prototype\src\app\AppRoutes.tsx' | Out-Null

$command = Require-MaxLines 'management-mod\OrionAdminBridge\data\scripts\commands\orionadmin.lua' 100
foreach ($module in @('protocol.lua', 'dispatch.lua', 'players.lua', 'playerassets.lua', 'rewards.lua', 'mailrewards.lua', 'alliances.lua', 'allianceassets.lua', 'inventory.lua', 'sectors.lua')) {
    Require-File "management-mod\OrionAdminBridge\data\scripts\lib\orionadmin\$module" | Out-Null
}

Write-Host 'PASS: modular-monolith architecture boundaries are intact.'
