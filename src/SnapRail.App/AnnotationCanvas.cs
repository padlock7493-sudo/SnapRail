using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapRail;

internal sealed class AnnotationCanvas : Canvas
{
    internal BitmapSource Original { get; }
    internal AnnotationHistory History { get; } = new();
    internal Rect Bounds { get; set; }
    internal bool ShowImage { get; init; }
    internal AnnotationTool Tool { get; private set; }
    internal string Ink { get; set; } = "#E34B4B";
    internal double StrokeWidth { get; set; } = 4;
    internal double TextSize { get; set; } = 24;
    private Annotation? draft;
    private TextBox? textEditor;
    private PixelPoint textOrigin;
    private int selectedIndex = -1;
    private int editingIndex = -1;
    private Point dragStart;
    private Annotation? dragOriginal;
    internal bool HasTextEditor => textEditor != null;
    internal bool IsDrawing => draft != null;
    internal event Action? Changed;

    internal AnnotationCanvas(BitmapSource original, AnnotationDocument? document = null)
    {
        Original = original;
        Bounds = new Rect(0, 0, original.PixelWidth, original.PixelHeight);
        Background = Brushes.Transparent;
        Focusable = true;
        ClipToBounds = true;
        if (document != null) foreach (var mark in document.Marks) History.Add(mark);
        SetTool(AnnotationTool.Select);
        SizeChanged += (_, _) => { LayoutText(); InvalidateVisual(); };
    }
    internal void SetTool(AnnotationTool tool)
    {
        CommitText();
        Tool = tool;
        if (tool != AnnotationTool.Object) selectedIndex = -1;
        IsHitTestVisible = tool != AnnotationTool.Select || ShowImage;
        Cursor = tool == AnnotationTool.Text ? Cursors.IBeam : tool == AnnotationTool.Select ? Cursors.Arrow : Cursors.Cross;
        InvalidateVisual(); Changed?.Invoke();
    }
    internal void Undo() { CommitText(); selectedIndex = -1; History.Undo(); InvalidateVisual(); Changed?.Invoke(); }
    internal void Redo() { CommitText(); selectedIndex = -1; History.Redo(); InvalidateVisual(); Changed?.Invoke(); }
    internal bool HasSelection => selectedIndex >= 0 && selectedIndex < History.Marks.Count;
    internal void StyleSelection(string? color = null, double? thickness = null, double? fontSize = null)
    {
        CommitText();
        if (color != null) Ink = color;
        if (thickness != null) StrokeWidth = thickness.Value;
        if (fontSize != null) TextSize = fontSize.Value;
        if (Tool == AnnotationTool.Object && HasSelection)
        {
            var copy = History.Marks[selectedIndex].Offset(0, 0);
            if (color != null) copy.Color = color;
            if (thickness != null) copy.Thickness = thickness.Value;
            if (fontSize != null) copy.FontSize = fontSize.Value;
            History.Replace(selectedIndex, copy);
        }
        InvalidateVisual(); Changed?.Invoke();
    }
    internal void DeleteSelection()
    {
        if (!HasSelection) return;
        History.Remove(selectedIndex); selectedIndex = -1; InvalidateVisual(); Changed?.Invoke();
    }
    internal bool ObjectKey(Key key, ModifierKeys modifiers)
    {
        if (Tool != AnnotationTool.Object || !HasSelection) return false;
        if (key == Key.Delete || key == Key.Back) { DeleteSelection(); return true; }
        if (key == Key.Enter) { EditSelectedText(); return true; }
        var step = modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
        var dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
        if (dx == 0 && dy == 0) return false;
        History.Replace(selectedIndex, Shift(History.Marks[selectedIndex], dx, dy)); InvalidateVisual(); Changed?.Invoke(); return true;
    }
    private static Rect MarkBounds(Annotation mark)
    {
        if (mark.Points.Count == 0) return Rect.Empty;
        if (mark.Tool == AnnotationTool.Text)
        {
            var text = new FormattedText(mark.Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Microsoft YaHei UI"), mark.FontSize, Brushes.Black, 1);
            return new Rect(mark.Points[0].Point, new Size(Math.Max(10, text.WidthIncludingTrailingWhitespace), Math.Max(10, text.Height)));
        }
        var x = mark.Points.Min(p => p.X); var y = mark.Points.Min(p => p.Y);
        var box = new Rect(x, y, mark.Points.Max(p => p.X) - x, mark.Points.Max(p => p.Y) - y); box.Inflate(Math.Max(4, mark.Thickness), Math.Max(4, mark.Thickness)); return box;
    }
    private Annotation Shift(Annotation mark, double dx, double dy)
    {
        var box = MarkBounds(mark);
        dx = Math.Clamp(dx, Math.Min(0, Bounds.Left - box.Left), Math.Max(0, Bounds.Right - box.Right));
        dy = Math.Clamp(dy, Math.Min(0, Bounds.Top - box.Top), Math.Max(0, Bounds.Bottom - box.Bottom));
        return mark.Offset(dx, dy);
    }
    private void EditSelectedText()
    {
        if (!HasSelection || History.Marks[selectedIndex].Tool != AnnotationTool.Text) return;
        var mark = History.Marks[selectedIndex]; editingIndex = selectedIndex;
        Ink = mark.Color; TextSize = mark.FontSize; BeginText(mark.Points[0], mark.Text);
    }
    private void BeginText(PixelPoint origin, string text = "")
    {
        textOrigin = origin;
        textEditor = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Foreground = RailWindow.Brush(Ink), Background = Brushes.White, BorderBrush = RailWindow.Brush("#126B64"), BorderThickness = new Thickness(1), Padding = new Thickness(2), ToolTip = "Ctrl+Enter 完成，Esc 放弃" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(textEditor, "annotation-text");
        Children.Add(textEditor); LayoutText(); textEditor.Focus(); textEditor.SelectAll();
    }
    internal Point Pixel(Point point) => new(point.X * Original.PixelWidth / Math.Max(1, ActualWidth), point.Y * Original.PixelHeight / Math.Max(1, ActualHeight));
    private PixelPoint Clamped(Point point) => new(Math.Clamp(point.X, Bounds.Left, Bounds.Right), Math.Clamp(point.Y, Bounds.Top, Bounds.Bottom));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Tool == AnnotationTool.Select || e.OriginalSource is TextBox || textEditor?.IsMouseOver == true) return;
        var point = Pixel(e.GetPosition(this));
        if (!Bounds.Contains(point)) { CommitText(); return; }
        CommitText();
        if (Tool == AnnotationTool.Object)
        {
            selectedIndex = History.Marks.FindLastIndex(mark => MarkBounds(mark).Contains(point));
            if (HasSelection)
            {
                if (e.ClickCount == 2) EditSelectedText();
                else { dragStart = point; dragOriginal = History.Marks[selectedIndex]; CaptureMouse(); Focus(); }
            }
            e.Handled = true; InvalidateVisual(); Changed?.Invoke(); return;
        }
        if (Tool == AnnotationTool.Text)
        {
            BeginText(Clamped(point));
        }
        else
        {
            draft = new Annotation { Tool = Tool, Color = Ink, Thickness = StrokeWidth, FontSize = TextSize, Points = new() { Clamped(point) } };
            if (Tool != AnnotationTool.Pen) draft.Points.Add(Clamped(point));
            CaptureMouse(); Focus();
        }
        e.Handled = true; InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragOriginal != null)
        {
            var delta = Pixel(e.GetPosition(this)) - dragStart; draft = Shift(dragOriginal, delta.X, delta.Y); InvalidateVisual(); e.Handled = true; return;
        }
        if (draft == null) return;
        var point = Clamped(Pixel(e.GetPosition(this)));
        if (draft.Tool == AnnotationTool.Pen) { if ((point.Point - draft.Points[^1].Point).Length >= 1) draft.Points.Add(point); }
        else draft.Points[^1] = point;
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragOriginal != null)
        {
            if (draft != null) History.Replace(selectedIndex, draft);
            dragOriginal = null; draft = null; ReleaseMouseCapture(); InvalidateVisual(); Changed?.Invoke(); e.Handled = true; return;
        }
        if (draft == null) return;
        if (draft.Tool != AnnotationTool.Pen) draft.Points[^1] = Clamped(Pixel(e.GetPosition(this)));
        var valid = draft.Tool == AnnotationTool.Pen || (draft.Points[^1].Point - draft.Points[0].Point).Length >= 2;
        if (valid) History.Add(draft);
        draft = null; ReleaseMouseCapture(); InvalidateVisual(); Changed?.Invoke(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (draft != null || dragOriginal != null) { draft = null; dragOriginal = null; InvalidateVisual(); }
    }
    internal bool CancelDraft()
    {
        if (textEditor != null) { Children.Remove(textEditor); textEditor = null; editingIndex = -1; Focus(); return true; }
        if (dragOriginal != null) { dragOriginal = null; draft = null; ReleaseMouseCapture(); InvalidateVisual(); return true; }
        if (Tool == AnnotationTool.Object && HasSelection) { selectedIndex = -1; InvalidateVisual(); Changed?.Invoke(); return true; }
        if (draft != null) { draft = null; ReleaseMouseCapture(); InvalidateVisual(); return true; }
        return false;
    }
    internal void CommitText()
    {
        if (textEditor == null) return;
        var value = textEditor.Text;
        Children.Remove(textEditor); textEditor = null;
        if (!string.IsNullOrWhiteSpace(value))
        {
            var mark = new Annotation { Tool = AnnotationTool.Text, Color = Ink, FontSize = TextSize, Points = new() { textOrigin }, Text = value };
            if (editingIndex >= 0) History.Replace(editingIndex, mark); else History.Add(mark);
        }
        else if (editingIndex >= 0) { History.Remove(editingIndex); selectedIndex = -1; }
        editingIndex = -1;
        InvalidateVisual(); Changed?.Invoke();
    }
    private void LayoutText()
    {
        if (textEditor == null) return;
        var scaleX = ActualWidth / Original.PixelWidth; var scaleY = ActualHeight / Original.PixelHeight;
        SetLeft(textEditor, textOrigin.X * scaleX); SetTop(textEditor, textOrigin.Y * scaleY);
        textEditor.FontSize = TextSize * scaleY;
        textEditor.MaxWidth = Math.Max(20, (Bounds.Right - textOrigin.X) * scaleX);
        textEditor.MinWidth = Math.Min(80, textEditor.MaxWidth);
        textEditor.MaxHeight = Math.Max(20, (Bounds.Bottom - textOrigin.Y) * scaleY);
        textEditor.MinHeight = Math.Min(30, textEditor.MaxHeight);
    }
    internal CaptureResult Export(Int32Rect rectangle)
    {
        CommitText();
        // Detach the crop: retaining a CroppedBitmap would retain the entire desktop snapshot.
        var crop = ImageStore.Detach(new CroppedBitmap(Original, rectangle));
        var document = new AnnotationDocument
        {
            Width = rectangle.Width, Height = rectangle.Height,
            Marks = History.Marks.Select(mark => mark.Offset(-rectangle.X, -rectangle.Y)).ToList()
        };
        return new CaptureResult(crop, document);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        if (ShowImage) dc.DrawImage(Original, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.PushTransform(new ScaleTransform(ActualWidth / Original.PixelWidth, ActualHeight / Original.PixelHeight));
        dc.PushClip(new RectangleGeometry(Bounds));
        for (var i = 0; i < History.Marks.Count; i++) if (dragOriginal == null || i != selectedIndex) AnnotationRenderer.Draw(dc, Original, History.Marks[i]);
        if (draft != null)
        {
            if (draft.Tool == AnnotationTool.Mosaic)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 80, 110, 115)), new Pen(Brushes.White, 1), new Rect(draft.Points[0].Point, draft.Points[^1].Point));
            else AnnotationRenderer.Draw(dc, Original, draft);
        }
        if (Tool == AnnotationTool.Object && HasSelection)
        {
            var box = MarkBounds(draft ?? History.Marks[selectedIndex]);
            dc.DrawRectangle(null, new Pen(RailWindow.Brush("#126B64"), 1.5) { DashStyle = DashStyles.Dash }, box);
        }
        dc.Pop(); dc.Pop();
    }
}
