$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' }
Push-Location $projectRoot
try {
    & $dotnetPath run --project 'tests\SnapRail.Tests\SnapRail.Tests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw '行为测试失败。' }
} finally { Pop-Location }
