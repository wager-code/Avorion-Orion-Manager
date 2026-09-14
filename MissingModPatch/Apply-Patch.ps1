param([Parameter(Mandatory=$true)][string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path.TrimEnd('\')
$relative = 'management-mod/OrionAdminBridge/data/scripts/commands/orionadmin.lua'
$payload = Join-Path $PSScriptRoot 'orionadmin.lua'
$target = Join-Path $root $relative
$ignorePath = Join-Path $root '.gitignore'
$manifestPath = Join-Path $root 'MANIFEST.sha256'
foreach ($required in @($ignorePath,$manifestPath,(Join-Path $root 'management-mod/OrionAdminBridge/modinfo.lua'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Not an expected handoff project: missing $required" }
}
$cursor = $root
foreach ($part in @('', 'management-mod','OrionAdminBridge','data','scripts','commands')) {
    if ($part) { $cursor = Join-Path $cursor $part }
    if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked path: $cursor" }
}
foreach ($path in @($target,$ignorePath,$manifestPath)) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Refusing linked file: $path" }
}
$hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Test-Path -LiteralPath $target) -and ((Get-FileHash -LiteralPath $target).Hash -ne $hash)) {
    throw 'Existing orionadmin.lua differs. No changes made; compare it manually instead of overwriting.'
}
$utf8 = [Text.UTF8Encoding]::new($false)
$ignoreOriginal = [IO.File]::ReadAllText($ignorePath)
$ignoreFixed = [regex]::Replace($ignoreOriginal, '(?m)^\*\*/data/\r?$', '/backend/src/AvorionAdmin.Api/data/')
$manifestOriginal = [IO.File]::ReadAllText($manifestPath)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$backup = Join-Path $root ('.migration-patch-backup/' + $stamp)
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $ignorePath -Destination (Join-Path $backup 'gitignore.txt')
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $backup 'MANIFEST.sha256')
New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
if (-not (Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $payload -Destination $target }
if ($ignoreFixed -ne $ignoreOriginal) { [IO.File]::WriteAllText($ignorePath,$ignoreFixed,$utf8) }
$ignoreHash = (Get-FileHash -LiteralPath $ignorePath -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @($manifestOriginal -split '\r?\n' | Where-Object { $_ -and $_ -notmatch '^[a-fA-F0-9]{64}  (\.gitignore|management-mod[/\\]OrionAdminBridge[/\\]data[/\\]scripts[/\\]commands[/\\]orionadmin\.lua)$' })
$lines += "$ignoreHash  .gitignore"
$lines += "$hash  $relative"
[IO.File]::WriteAllLines($manifestPath,[string[]]$lines,$utf8)
Write-Host 'PATCH_PASS: original MOD source restored; ignore rule and matching manifest entries repaired.'
Write-Host 'Other source files and their manifest entries were not changed.'
Write-Host "Previous ignore/manifest files saved in $backup"
