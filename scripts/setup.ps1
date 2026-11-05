$ErrorActionPreference = 'Stop'
$installer = Join-Path $env:TEMP 'snaprail-dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
& $installer -Version '10.0.403' -InstallDir (Join-Path $env:USERPROFILE '.dotnet') -NoPath
if ($LASTEXITCODE -ne 0) { throw 'SDK 安装失败。' }
$sdkPath = Join-Path $env:USERPROFILE '.dotnet'
[Environment]::SetEnvironmentVariable('DOTNET_ROOT', $sdkPath, 'User')
$userPathValue = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($null -eq $userPathValue) { $userPathValue = '' }
if (($userPathValue -split ';') -notcontains $sdkPath) {
    [Environment]::SetEnvironmentVariable('Path', ($userPathValue.TrimEnd(';') + ';' + $sdkPath), 'User')
}
& (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe') --info
