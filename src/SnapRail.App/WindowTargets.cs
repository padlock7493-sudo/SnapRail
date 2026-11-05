using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SnapRail;

internal sealed record WindowTarget(Rectangle Bounds, string Title, IntPtr Handle);
internal static class WindowTargets
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Frame frame);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetFrame(IntPtr hwnd, int attribute, out Frame frame, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetCloaked(IntPtr hwnd, int attribute, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Frame { internal int Left, Top, Right, Bottom; }
    internal static IReadOnlyList<WindowTarget> Snapshot()
    {
        var windows = new List<WindowTarget>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var process);
            if (process == Environment.ProcessId || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            if (GetCloaked(hwnd, 14, out var cloaked, 4) == 0 && cloaked != 0) return true;
            var title = new StringBuilder(512); GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;
            if (GetFrame(hwnd, 9, out var frame, 16) != 0 && !GetWindowRect(hwnd, out frame)) return true;
            var bounds = Rectangle.Intersect(Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom), System.Windows.Forms.SystemInformation.VirtualScreen);
            if (bounds.Width > 20 && bounds.Height > 20) windows.Add(new WindowTarget(bounds, title.ToString(), hwnd));
            return true;
        }, IntPtr.Zero);
        return windows;
    }
}
