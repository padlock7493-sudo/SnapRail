param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnetPath)) { throw '未找到 .NET 10 SDK。请先运行 scripts\setup.ps1。' }
$developmentExe = Join-Path $projectRoot 'src\SnapRail.App\bin\Release\net10.0-windows\SnapRail.exe'
$publishedExe = Join-Path $projectRoot 'artifacts\SnapRail-win-x64\SnapRail.exe'
$running = Get-Process SnapRail -ErrorAction SilentlyContinue
if ($running | Where-Object { $_.Path -eq $developmentExe -or ($Publish -and $_.Path -eq $publishedExe) }) {
    throw '请先从托盘退出待更新的随手挂，再运行构建，避免覆盖运行中的程序。'
}
Push-Location $projectRoot
try {
    & $dotnetPath build 'src\SnapRail.App\SnapRail.App.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
    if ($Publish) {
        & $dotnetPath publish 'src\SnapRail.App\SnapRail.App.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o 'artifacts\SnapRail-win-x64'
        if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\USER_GUIDE.md') -Destination (Join-Path $projectRoot 'artifacts\SnapRail-win-x64\使用说明.md')
    }
} finally { Pop-Location }
