using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace SnapRail;

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT rectangle, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    internal static double Dpi(Forms.Screen screen)
    {
        var bounds = screen.Bounds;
        var rect = new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        var monitor = MonitorFromRect(ref rect, 2);
        return GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x / 96d : 1;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
    internal static System.Drawing.Rectangle Bounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rectangle)) return System.Drawing.Rectangle.Empty;
        return System.Drawing.Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
    }

    internal static void Place(Window window, System.Drawing.Rectangle bounds, bool activate = false)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, activate ? 0x40u : 0x50u);
    }

    internal static Forms.Screen CursorScreen => Forms.Screen.FromPoint(Forms.Cursor.Position);
}

internal sealed class Hotkeys : IDisposable
{
    private readonly HwndSource source;
    internal event Action<int>? Pressed;
    private HotkeySpec? capture;
    private HotkeySpec? toggle;
    internal Hotkeys()
    {
        source = new HwndSource(new HwndSourceParameters("SnapRail.Hotkeys") { ParentWindow = new IntPtr(-3) });
        source.AddHook(Hook);
    }
    internal bool Register(int id, uint key) => Native.RegisterHotKey(source.Handle, id, 0x4003, key);
    internal bool Configure(HotkeySpec captureKey, HotkeySpec toggleKey, out string error)
    {
        captureKey.Validate(); toggleKey.Validate();
        Native.UnregisterHotKey(source.Handle, 1); Native.UnregisterHotKey(source.Handle, 2);
        var first = Native.RegisterHotKey(source.Handle, 1, captureKey.Modifiers | 0x4000, captureKey.Key);
        var second = Native.RegisterHotKey(source.Handle, 2, toggleKey.Modifiers | 0x4000, toggleKey.Key);
        if (first && second) { capture = captureKey; toggle = toggleKey; error = ""; return true; }
        Native.UnregisterHotKey(source.Handle, 1); Native.UnregisterHotKey(source.Handle, 2);
        if (capture != null) Native.RegisterHotKey(source.Handle, 1, capture.Modifiers | 0x4000, capture.Key);
        if (toggle != null) Native.RegisterHotKey(source.Handle, 2, toggle.Modifiers | 0x4000, toggle.Key);
        error = "快捷键被其他应用占用或系统保留；原快捷键已保留，可从托盘截图。"; return false;
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312) { handled = true; Pressed?.Invoke(wParam.ToInt32()); }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        Native.UnregisterHotKey(source.Handle, 1);
        Native.UnregisterHotKey(source.Handle, 2);
        source.Dispose();
    }
}
