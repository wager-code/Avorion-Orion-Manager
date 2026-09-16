param(
    [ValidateRange(1024, 65535)][int]$Port = 5088
)
$ErrorActionPreference = 'SilentlyContinue'
$url = "http://127.0.0.1:$Port/server/control"
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    $health = Invoke-RestMethod "http://127.0.0.1:$Port/api/v1/health" -TimeoutSec 2
    if ($health.status -eq 'ok') {
        Start-Process $url
        exit 0
    }
    Start-Sleep -Milliseconds 500
}
exit 1
