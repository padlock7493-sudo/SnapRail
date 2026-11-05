using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapRail;

public enum AnnotationTool { Select, Rectangle, Ellipse, Arrow, Pen, Text, Mosaic, Object }
public readonly record struct PixelPoint(double X, double Y)
{
    internal Point Point => new(X, Y);
}
public sealed class Annotation
{
    public AnnotationTool Tool { get; set; }
    public string Color { get; set; } = "#E34B4B";
    public double Thickness { get; set; } = 4;
    public double FontSize { get; set; } = 24;
    public string Text { get; set; } = "";
    public List<PixelPoint> Points { get; set; } = new();
    [JsonIgnore] internal BitmapSource? Mosaic { get; set; }
    internal Annotation Offset(double x, double y) => new()
    {
        Tool = Tool, Color = Color, Thickness = Thickness, FontSize = FontSize, Text = Text,
        Points = Points.Select(p => new PixelPoint(p.X + x, p.Y + y)).ToList()
    };
}
public sealed class AnnotationDocument
{
    public int Version { get; set; } = 1;
    public int Width { get; set; }
    public int Height { get; set; }
    public List<Annotation> Marks { get; set; } = new();
}

internal sealed class AnnotationHistory
{
    internal List<Annotation> Marks { get; } = new();
    private readonly Stack<Annotation[]> undo = new();
    private readonly Stack<Annotation[]> redo = new();
    internal bool CanRedo => redo.Count > 0;
    internal bool CanUndo => undo.Count > 0;
    private void Checkpoint()
    {
        undo.Push(Marks.ToArray()); redo.Clear();
        if (undo.Count <= 100) return;
        var recent = undo.Take(100).Reverse().ToArray(); undo.Clear(); foreach (var state in recent) undo.Push(state);
    }
    internal void Add(Annotation mark) { Checkpoint(); Marks.Add(mark); }
    internal void Replace(int index, Annotation mark) { Checkpoint(); Marks[index] = mark; }
    internal void Remove(int index) { Checkpoint(); Marks.RemoveAt(index); }
    internal void Undo() { if (undo.Count > 0) { redo.Push(Marks.ToArray()); var state = undo.Pop(); Marks.Clear(); Marks.AddRange(state); } }
    internal void Redo() { if (redo.Count > 0) { undo.Push(Marks.ToArray()); var state = redo.Pop(); Marks.Clear(); Marks.AddRange(state); } }
}

internal static class AnnotationRenderer
{
    internal static BitmapSource Render(BitmapSource original, AnnotationDocument document)
    {
        if (document.Marks.Count == 0) return original;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, original.PixelWidth, original.PixelHeight)));
            dc.DrawImage(original, new Rect(0, 0, original.PixelWidth, original.PixelHeight));
            foreach (var mark in document.Marks) Draw(dc, original, mark);
            dc.Pop();
        }
        var result = new RenderTargetBitmap(original.PixelWidth, original.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }

    internal static void Draw(DrawingContext dc, BitmapSource original, Annotation mark)
    {
        if (mark.Points.Count == 0) return;
        var first = mark.Points[0].Point;
        var last = mark.Points[^1].Point;
        var box = new Rect(first, last);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(mark.Color));
        var pen = new Pen(brush, mark.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (mark.Tool)
        {
            case AnnotationTool.Rectangle: dc.DrawRectangle(null, pen, box); break;
            case AnnotationTool.Ellipse: dc.DrawEllipse(null, pen, new Point(box.X + box.Width / 2, box.Y + box.Height / 2), box.Width / 2, box.Height / 2); break;
            case AnnotationTool.Arrow:
                dc.DrawLine(pen, first, last);
                var direction = first - last;
                if (direction.Length < 1) break;
                direction.Normalize();
                var length = Math.Max(14, mark.Thickness * 4);
                var normal = new Vector(-direction.Y, direction.X);
                var head = new StreamGeometry();
                using (var context = head.Open())
                {
                    context.BeginFigure(last, true, true);
                    context.LineTo(last + direction * length + normal * length * .45, true, false);
                    context.LineTo(last + direction * length - normal * length * .45, true, false);
                }
                dc.DrawGeometry(brush, null, head);
                break;
            case AnnotationTool.Pen:
                if (mark.Points.Count == 1) { dc.DrawEllipse(brush, null, first, mark.Thickness / 2, mark.Thickness / 2); break; }
                var stroke = new StreamGeometry();
                using (var context = stroke.Open())
                {
                    context.BeginFigure(first, false, false);
                    context.PolyLineTo(mark.Points.Skip(1).Select(p => p.Point).ToArray(), true, true);
                }
                dc.DrawGeometry(null, pen, stroke);
                break;
            case AnnotationTool.Text:
                var text = new FormattedText(mark.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI, Microsoft YaHei UI"), mark.FontSize, brush, 1);
                dc.DrawText(text, first);
                break;
            case AnnotationTool.Mosaic:
                var region = MosaicBounds(original, box);
                if (region.IsEmpty) break;
                mark.Mosaic ??= Pixelate(original, region);
                dc.PushClip(new RectangleGeometry(new Rect(region.X, region.Y, region.Width, region.Height)));
                dc.DrawImage(mark.Mosaic, new Rect(region.X, region.Y, region.Width, region.Height));
                dc.Pop();
                break;
        }
    }

    private static Int32Rect MosaicBounds(BitmapSource original, Rect box)
    {
        var x = Math.Clamp((int)Math.Floor(box.X), 0, original.PixelWidth);
        var y = Math.Clamp((int)Math.Floor(box.Y), 0, original.PixelHeight);
        var right = Math.Clamp((int)Math.Ceiling(box.Right), 0, original.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(box.Bottom), 0, original.PixelHeight);
        return right <= x || bottom <= y ? Int32Rect.Empty : new Int32Rect(x, y, right - x, bottom - y);
    }
    private static BitmapSource Pixelate(BitmapSource original, Int32Rect region)
    {
        const int block = 14;
        var converted = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[region.Width * region.Height * 4];
        converted.CopyPixels(region, pixels, region.Width * 4, 0);
        for (var y = 0; y < region.Height; y += block)
        for (var x = 0; x < region.Width; x += block)
        {
            long b = 0, g = 0, r = 0, a = 0;
            var width = Math.Min(block, region.Width - x); var height = Math.Min(block, region.Height - y);
            for (var dy = 0; dy < height; dy++)
            for (var dx = 0; dx < width; dx++)
            {
                var offset = ((y + dy) * region.Width + x + dx) * 4;
                b += pixels[offset]; g += pixels[offset + 1]; r += pixels[offset + 2]; a += pixels[offset + 3];
            }
            var count = width * height;
            for (var dy = 0; dy < height; dy++)
            for (var dx = 0; dx < width; dx++)
            {
                var offset = ((y + dy) * region.Width + x + dx) * 4;
                pixels[offset] = (byte)(b / count); pixels[offset + 1] = (byte)(g / count);
                pixels[offset + 2] = (byte)(r / count); pixels[offset + 3] = (byte)(a / count);
            }
        }
        var result = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgra32, null, pixels, region.Width * 4);
        result.Freeze(); return result;
    }
}

internal sealed record CaptureResult(BitmapSource Original, AnnotationDocument Document)
{
    internal BitmapSource Flatten() => AnnotationRenderer.Render(Original, Document);
}
