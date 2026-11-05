using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapRail;

internal static class ImageClipboard
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);

    internal static void Set(BitmapSource bitmap, IntPtr owner)
    {
        var source = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight]; source.CopyPixels(pixels, stride, 0);
        var dib = new byte[40 + pixels.Length];
        using (var stream = new MemoryStream(dib))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(40); writer.Write(source.PixelWidth); writer.Write(source.PixelHeight);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(pixels.Length);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (var y = source.PixelHeight - 1; y >= 0; y--) writer.Write(pixels, y * stride, stride);
        }
        IntPtr dibMemory = IntPtr.Zero;
        try
        {
            dibMemory = Allocate(dib);
            if (!OpenClipboard(owner)) throw Error();
            try
            {
                if (!EmptyClipboard() || SetClipboardData(8, dibMemory) == IntPtr.Zero) throw Error();
                dibMemory = IntPtr.Zero; // Ownership transferred to Windows, which frees it on replacement.
            }
            finally { CloseClipboard(); }
        }
        finally
        {
            if (dibMemory != IntPtr.Zero) GlobalFree(dibMemory);
        }
    }
    private static IntPtr Allocate(byte[] bytes)
    {
        var memory = GlobalAlloc(0x42, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero) throw new OutOfMemoryException();
        var pointer = GlobalLock(memory);
        if (pointer == IntPtr.Zero) { GlobalFree(memory); throw Error(); }
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); } finally { GlobalUnlock(memory); }
        return memory;
    }
    private static COMException Error() => new("剪贴板暂不可用，请稍后重试。", Marshal.GetHRForLastWin32Error());
}
