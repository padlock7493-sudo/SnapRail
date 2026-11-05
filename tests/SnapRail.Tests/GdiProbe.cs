using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using SnapRail;

internal static class GdiProbe
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint type);
    private static uint Count => GetGuiResources(Process.GetCurrentProcess().Handle, 0);
    private static void Collect()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    internal static void Run()
    {
        Console.WriteLine("start " + Count);
        Take(); Console.WriteLine("capture before GC " + Count); Collect(); Console.WriteLine("capture after GC " + Count);
        Thumbnails(); Console.WriteLine("thumbnail before GC " + Count); Collect(); Console.WriteLine("thumbnail after GC " + Count);
        Copy(); Console.WriteLine("clipboard before GC " + Count); Collect(); Console.WriteLine("clipboard after GC " + Count);
        NativeCopy(); Console.WriteLine("native clipboard before GC " + Count); Collect(); Console.WriteLine("native clipboard after GC " + Count);
        Encode(); Console.WriteLine("PNG encode before GC " + Count); Collect(); Console.WriteLine("PNG encode after GC " + Count);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void NativeCopy()
    {
        using var window = new HwndSource(new HwndSourceParameters("SnapRail.GdiProbe") { ParentWindow = new IntPtr(-3) });
        var source = BitmapSource.Create(300, 200, 96, 96, PixelFormats.Bgra32, null, new byte[300 * 200 * 4], 1200); source.Freeze();
        for (var i = 0; i < 40; i++) ImageClipboard.Set(source, window.Handle);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Encode()
    {
        var source = BitmapSource.Create(300, 200, 96, 96, PixelFormats.Bgra32, null, new byte[300 * 200 * 4], 1200); source.Freeze();
        for (var i = 0; i < 40; i++) { using var stream = new System.IO.MemoryStream(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); encoder.Save(stream); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Take()
    {
        for (var i = 0; i < 40; i++) { var bitmap = CaptureWindow.Take(new System.Drawing.Rectangle(0, 0, 300, 200)); GC.KeepAlive(bitmap); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Thumbnails()
    {
        var source = BitmapSource.Create(300, 200, 96, 96, PixelFormats.Bgra32, null, new byte[300 * 200 * 4], 1200); source.Freeze();
        for (var i = 0; i < 40; i++)
        {
            var store = new ImageStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SnapRail-GdiProbe"));
            store.Add(new CaptureResult(source, new AnnotationDocument { Width = 300, Height = 200 }));
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Copy()
    {
        var source = BitmapSource.Create(300, 200, 96, 96, PixelFormats.Bgra32, null, new byte[300 * 200 * 4], 1200); source.Freeze();
        for (var i = 0; i < 40; i++) Clipboard.SetImage(source);
    }
}
