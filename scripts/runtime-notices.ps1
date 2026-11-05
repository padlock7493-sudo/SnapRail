param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$config = Get-Content -LiteralPath (Join-Path $projectRoot 'src\SnapRail.App\bin\Release\net10.0-windows\win-x64\SnapRail.runtimeconfig.json') -Raw | ConvertFrom-Json
$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$parts = [Collections.Generic.List[string]]::new()
$parts.Add('SnapRail includes the .NET Windows x64 runtime. Original license and notices follow.')
foreach ($framework in $config.runtimeOptions.includedFrameworks) {
    $package = switch ($framework.name) {
        'Microsoft.NETCore.App' { 'microsoft.netcore.app.runtime.win-x64' }
        'Microsoft.WindowsDesktop.App' { 'microsoft.windowsdesktop.app.runtime.win-x64' }
        default { throw ('未配置运行时声明：' + $framework.name) }
    }
    $directory = Join-Path (Join-Path $cache $package) $framework.version
    $license = Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -match '^LICENSE(\.txt)?$' } | Select-Object -First 1
    if ($null -eq $license) { throw ('找不到运行时许可证：' + $directory) }
    $parts.Add("`r`n=== $($framework.name) $($framework.version) ===`r`n" + [IO.File]::ReadAllText($license.FullName))
    $notices = Join-Path $directory 'THIRD-PARTY-NOTICES.TXT'
    if (Test-Path -LiteralPath $notices) { $parts.Add([IO.File]::ReadAllText($notices)) }
}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Destination),($parts -join "`r`n"),[Text.UTF8Encoding]::new($false))
