param([Parameter(Mandatory=$true)][string]$Folder)
$ErrorActionPreference = 'Stop'
$fullPath = [System.IO.Path]::GetFullPath($Folder)
New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
$shell = New-Object -ComObject Shell.Application
$shell.Open($fullPath)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ExplorerPosition {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
[ExplorerPosition]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    foreach ($view in $shell.Windows()) {
        if (-not $view.LocationURL) { continue }
        try { $path = ([Uri]$view.LocationURL).LocalPath.TrimEnd('\') } catch { continue }
        if ($path -eq $fullPath.TrimEnd('\')) {
            $handle = [IntPtr]([long]$view.HWND)
            [ExplorerPosition]::SetWindowPos($handle, [IntPtr]::Zero, 1050, 700, 1000, 800, 0x40) | Out-Null
            [pscustomobject]@{ Handle = [long]$handle; X = 1050; Y = 700; Width = 1000; Height = 800 } | ConvertTo-Json -Compress
            return
        }
    }
    Start-Sleep -Milliseconds 150
}
throw '未找到测试目录的资源管理器窗口。'
