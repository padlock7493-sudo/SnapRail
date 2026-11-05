param([switch]$IncludeSource)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -Publish
    if ($LASTEXITCODE -ne 0) { throw '发布构建失败。' }
    [xml]$project = Get-Content -LiteralPath 'src\SnapRail.App\SnapRail.App.csproj'
    $version = [string]$project.Project.PropertyGroup.Version
    $work = Join-Path $projectRoot ('artifacts\packages\' + $version + '-' + [guid]::NewGuid().ToString('N'))
    $portable = Join-Path $work 'SnapRail-Portable'
    $destination = Join-Path $projectRoot ('release\SnapRail-v' + $version)
    New-Item -ItemType Directory -Path $portable,(Join-Path $portable 'images'),(Join-Path $portable 'documents'),$destination -Force | Out-Null
    Copy-Item -LiteralPath 'artifacts\SnapRail-win-x64\SnapRail.exe' -Destination $portable
    Copy-Item -LiteralPath 'LICENSE' -Destination $portable
    & (Join-Path $PSScriptRoot 'runtime-notices.ps1') -Destination (Join-Path $portable 'THIRD-PARTY-NOTICES.txt')
    Copy-Item -LiteralPath 'docs\USER_GUIDE.md' -Destination (Join-Path $portable '使用说明.md')
    [IO.File]::WriteAllText((Join-Path $portable 'SnapRail.portable'),'Portable mode: keep this file beside SnapRail.exe.')
    $zipName = 'SnapRail-v' + $version + '-Portable-win-x64.zip'
    $zipPath = Join-Path $destination $zipName
    Compress-Archive -LiteralPath $portable -DestinationPath $zipPath -Force
    Copy-Item -LiteralPath 'docs\RELEASE_NOTES.md' -Destination $destination
    $guide = Join-Path $work ('SnapRail-v' + $version + '-Guide')
    $guideIcon = Join-Path $guide 'src\SnapRail.App\Assets'
    New-Item -ItemType Directory -Path $guideIcon -Force | Out-Null
    Copy-Item -LiteralPath 'README.md','LICENSE','CHANGELOG.md' -Destination $guide
    Copy-Item -LiteralPath 'docs' -Destination $guide -Recurse
    Copy-Item -LiteralPath 'src\SnapRail.App\Assets\SnapRail-mist-blue-v1.png' -Destination $guideIcon
    Compress-Archive -LiteralPath $guide -DestinationPath (Join-Path $destination ('SnapRail-v' + $version + '-Guide.zip')) -Force
    if ($IncludeSource) {
        $source = Join-Path $work ('SnapRail-v' + $version + '-source')
        New-Item -ItemType Directory -Path $source | Out-Null
        $roots = @('README.md','CHANGELOG.md','.gitignore','global.json','SnapRail.sln','src','scripts','tests','docs')
        if (Test-Path -LiteralPath 'LICENSE') { $roots += 'LICENSE' }
        foreach ($entry in $roots) {
            $files = if ((Get-Item -LiteralPath $entry).PSIsContainer) { Get-ChildItem -LiteralPath $entry -Recurse -File } else { Get-Item -LiteralPath $entry }
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($projectRoot.TrimEnd('\').Length + 1)
                if ($relative -match '(^|[\\/])(bin|obj|__pycache__)([\\/]|$)' -or $file.Name -eq 'dotnet-install.ps1') { continue }
                $target = Join-Path $source $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $target
            }
        }
        Compress-Archive -LiteralPath $source -DestinationPath (Join-Path $destination ('SnapRail-v' + $version + '-source.zip')) -Force
    }
    Get-ChildItem -LiteralPath $destination -Filter '*.zip' | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash,$_.Name
    } | Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Encoding utf8
    Write-Output ('发布文件：' + $destination)
    Write-Output ('干净便携目录：' + $portable)
    [IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\latest-portable.txt'),$portable)
} finally { Pop-Location }
