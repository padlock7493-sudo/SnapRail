using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SnapRail;

// Installed only while the visible shortcut input owns keyboard focus.
internal sealed class HotkeyRecorder : IDisposable
{
    private delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    private readonly Hook callback;
    private readonly Action<HotkeySpec> capture;
    private readonly HashSet<uint> pressed = new();
    private readonly HashSet<uint> consumed = new();
    private IntPtr handle;
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    internal HotkeyRecorder(Action<HotkeySpec> capture)
    {
        this.capture = capture; callback = OnKey;
        foreach (var key in new uint[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C }) if ((GetAsyncKeyState((int)key) & 0x8000) != 0) pressed.Add(key);
        handle = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法启动快捷键录制。");
    }
    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KeyboardData>(data).Key;
            var down = message.ToInt32() is 0x100 or 0x104;
            var up = message.ToInt32() is 0x101 or 0x105;
            if (key is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C)
            {
                if (down) pressed.Add(key); else if (up) pressed.Remove(key);
            }
            else if (down)
            {
                uint mods = 0;
                foreach (var modifier in pressed) mods |= modifier switch { 0x10 or 0xA0 or 0xA1 => 4u, 0x11 or 0xA2 or 0xA3 => 2u, 0x12 or 0xA4 or 0xA5 => 1u, 0x5B or 0x5C => 8u, _ => 0u };
                if ((key == 9 && (mods == 0 || mods == 4)) || ((key == 13 || key == 27) && mods == 0)) return CallNextHookEx(handle, code, message, data);
                try { capture(new HotkeySpec { Key = key, Modifiers = mods }); } catch (InvalidOperationException) { }
                consumed.Add(key); return new IntPtr(1);
            }
            else if (up && consumed.Remove(key)) return new IntPtr(1);
        }
        return CallNextHookEx(handle, code, message, data);
    }
    public void Dispose() { if (handle != IntPtr.Zero) { UnhookWindowsHookEx(handle); handle = IntPtr.Zero; } }
}
