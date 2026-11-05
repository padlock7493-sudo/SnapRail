using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapRail;

internal enum StitchStatus { Added, Unchanged, NoMatch, Limit }
internal sealed class ScrollStitcher
{
    private readonly List<BitmapSource> strips = new();
    private byte[] previous;
    internal int Width { get; }
    internal int FrameHeight { get; }
    internal int Height { get; private set; }
    internal int Frames => strips.Count;
    internal ScrollStitcher(BitmapSource initial)
    {
        Width = initial.PixelWidth; FrameHeight = initial.PixelHeight; Height = FrameHeight;
        previous = Pixels(initial); strips.Add(ImageStore.Detach(initial));
    }
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[source.PixelWidth * source.PixelHeight * 4]; converted.CopyPixels(bytes, source.PixelWidth * 4, 0); return bytes;
    }
    private double Error(byte[] next, int shift, bool dense = false)
    {
        var height = FrameHeight - shift; double error = 0; var samples = 0;
        for (var y = 5; y < height - 5; y += Math.Max(1, height / (dense ? 500 : 34)))
        for (var x = 6; x < Width - 6; x += Math.Max(1, Width / (dense ? 800 : 32)))
        {
            var a = ((y + shift) * Width + x) * 4; var b = (y * Width + x) * 4;
            for (var channel = 0; channel < 3; channel++) { error += Math.Abs(previous[a + channel] - next[b + channel]); samples++; }
        }
        return samples == 0 ? double.MaxValue : error / samples;
    }
    internal StitchStatus Append(BitmapSource frame)
    {
        if (frame.PixelWidth != Width || frame.PixelHeight != FrameHeight) return StitchStatus.NoMatch;
        var next = Pixels(frame);
        if (previous.AsSpan().SequenceEqual(next)) return StitchStatus.Unchanged;
        var best = double.MaxValue; var shift = 0;
        var errors = new double[FrameHeight];
        for (var candidate = 1; candidate <= FrameHeight - Math.Max(32, FrameHeight / 5); candidate++)
        {
            var coarse = Error(next,candidate);
            var error = coarse <= 3 ? Error(next,candidate,true) : double.MaxValue; errors[candidate] = error;
            if (error < best) { best = error; shift = candidate; }
        }
        if (shift == 0 || best > 2.5) return StitchStatus.NoMatch;
        // Repeated rows/blank areas can produce several equally good offsets.
        // Refuse ambiguous alignment rather than silently omit content.
        for (var candidate = 1; candidate <= FrameHeight - Math.Max(32,FrameHeight / 5); candidate++)
            if (Math.Abs(candidate - shift) > 4 && errors[candidate] <= best + .3) return StitchStatus.NoMatch;
        if (Height + shift > 30000 || (long)Width * (Height + shift) > 50_000_000 || Frames >= 80) return StitchStatus.Limit;
        strips.Add(ImageStore.Detach(new CroppedBitmap(frame,new Int32Rect(0,FrameHeight-shift,Width,shift))));
        Height += shift; previous = next; return StitchStatus.Added;
    }
    internal BitmapSource Render()
    {
        var output = new WriteableBitmap(Width,Height,96,96,PixelFormats.Bgra32,null); var y = 0;
        foreach (var strip in strips)
        {
            var pixels = Pixels(strip); output.WritePixels(new Int32Rect(0,y,Width,strip.PixelHeight),pixels,Width*4,0); y += strip.PixelHeight;
        }
        output.Freeze(); return output;
    }
}
