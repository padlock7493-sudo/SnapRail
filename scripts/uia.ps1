param(
    [string]$Window = '随手挂 · 选择截图区域',
    [string]$Id,
    [string]$Name,
    [ValidateSet('bounds', 'invoke', 'value', 'range', 'list', 'window', 'select', 'focus', 'toggle', 'close', 'read', 'expand')][string]$Action = 'bounds',
    [string]$Text
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$scope = [System.Windows.Automation.TreeScope]
$element = [System.Windows.Automation.AutomationElement]
$process = Get-Process SnapRail | Select-Object -First 1
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class UiNative {
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    public static IntPtr[] Windows(uint process) {
        var found = new List<IntPtr>();
        EnumWindows((window, data) => { uint id; GetWindowThreadProcessId(window, out id); if(id == process && IsWindowVisible(window)) found.Add(window); return true; }, IntPtr.Zero);
        return found.ToArray();
    }
}
'@
[UiNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$root = $null
foreach ($handle in [UiNative]::Windows($process.Id)) {
    $candidate = $element::FromHandle($handle)
    if ($candidate.Current.Name -eq $Window) { $root = $candidate; break }
}
if ($null -eq $root) { throw "Window not found: $Window" }
if ($Action -eq 'close') { $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); return }
if ($Action -eq 'window') {
    $bounds = $root.Current.BoundingRectangle
    [pscustomobject]@{ X = $bounds.X; Y = $bounds.Y; Width = $bounds.Width; Height = $bounds.Height; Handle = [long]$handle } | ConvertTo-Json -Compress
    return
}
if ($Action -eq 'list') {
    $root.FindAll($scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
        if ($_.Current.AutomationId -or $_.Current.Name) {
            [pscustomobject]@{ Id = $_.Current.AutomationId; Name = $_.Current.Name; Enabled = $_.Current.IsEnabled }
        }
    } | ConvertTo-Json -Compress
    return
}
if ($Id) { $condition = New-Object System.Windows.Automation.PropertyCondition($element::AutomationIdProperty, $Id) }
elseif ($Name) { $condition = New-Object System.Windows.Automation.PropertyCondition($element::NameProperty, $Name) }
else { throw 'Id or Name required' }
$target = $root.FindFirst($scope::Descendants, $condition)
if ($null -eq $target) { throw "Control not found: $Id $Name" }
if ($Action -eq 'invoke') {
    $invoke = $null
    if ($target.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) { $invoke.Invoke() }
    elseif ($target.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $target.Current.NativeWindowHandle -ne 0) {
        [UiNative]::SendMessage([IntPtr]$target.Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    } else { throw 'Control does not support invocation.' }
} elseif ($Action -eq 'value') {
    $target.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
} elseif ($Action -eq 'range') {
    $target.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue([double]::Parse($Text,[Globalization.CultureInfo]::InvariantCulture))
} elseif ($Action -eq 'select') {
    $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
} elseif ($Action -eq 'focus') {
    $target.SetFocus()
} elseif ($Action -eq 'toggle') {
    $target.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
} elseif ($Action -eq 'expand') {
    $target.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
} elseif ($Action -eq 'read') {
    $pattern = $null; $value = $null
    if ($target.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) { $value = $pattern.Current.Value }
    $toggle = $null; $toggleState = $null
    if ($target.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$toggle)) { $toggleState = $toggle.Current.ToggleState.ToString() }
    [pscustomobject]@{ Name = $target.Current.Name; Value = $value; Enabled = $target.Current.IsEnabled; ToggleState = $toggleState } | ConvertTo-Json -Compress
} else {
    $bounds = $target.Current.BoundingRectangle
    [pscustomobject]@{ X = $bounds.X; Y = $bounds.Y; Width = $bounds.Width; Height = $bounds.Height; Enabled = $target.Current.IsEnabled } | ConvertTo-Json -Compress
}
