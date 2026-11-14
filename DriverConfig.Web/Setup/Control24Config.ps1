# Lanceur de Control24 Config : démarre le serveur local (s'il n'est pas déjà lancé) puis ouvre le navigateur.
$url = "http://127.0.0.1:5119"
$dir = $PSScriptRoot

function Test-Server {
    try { Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 1 | Out-Null; $true } catch { $false }
}

if (-not (Test-Server)) {
    Start-Process (Join-Path $dir "DriverConfig.Web.exe") -WorkingDirectory $dir -WindowStyle Hidden
    for ($i = 0; $i -lt 30 -and -not (Test-Server); $i++) { Start-Sleep -Milliseconds 500 }
}
Start-Process $url
