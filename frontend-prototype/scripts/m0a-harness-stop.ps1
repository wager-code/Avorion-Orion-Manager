param([Parameter(Mandatory=$true)][string]$RunRoot)
$h=Get-Content (Join-Path $RunRoot "harness.json")|ConvertFrom-Json
foreach($id in @($h.apiPid,$h.webPid)){if($id){Stop-Process -Id $id -Force -ErrorAction SilentlyContinue}}
Write-Output "STOPPED $RunRoot"
