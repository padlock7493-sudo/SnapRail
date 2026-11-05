$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$binary = Join-Path $projectRoot 'release\SnapRail-Portable\SnapRail.exe'
if (-not (Test-Path -LiteralPath $binary)) {
    & (Join-Path $PSScriptRoot 'package.ps1')
    $latest = [IO.File]::ReadAllText((Join-Path $projectRoot 'artifacts\latest-portable.txt')).Trim()
    $binary = Join-Path $latest 'SnapRail.exe'
}
Start-Process -FilePath $binary -WorkingDirectory (Split-Path $binary) -WindowStyle Hidden
