using System;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;
using CopyPixelOperation = System.Drawing.CopyPixelOperation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using Rectangle = System.Windows.Shapes.Rectangle;
using Image = System.Windows.Controls.Image;

namespace SnapRail;

internal sealed class CaptureWindow : Window
{
    private readonly BitmapSource snapshot;
    private readonly System.Drawing.Rectangle desktop;
    private readonly Border hint;
    private readonly Canvas canvas = new() { Background = Brushes.Transparent };
    private readonly Rectangle selection = new() { Stroke = Brushes.White, StrokeThickness = 1, Fill = Brushes.Transparent, IsHitTestVisible = false };
    private readonly Rectangle shade = new() { Fill = RailWindow.Brush("#78000000"), IsHitTestVisible = false };
    private readonly TextBlock sizeLabel = new() { Foreground = Brushes.White, Background = Brushes.Black, Padding = new Thickness(8, 4, 8, 4), FontSize = 13, IsHitTestVisible = false };
    private readonly AnnotationCanvas editor;
    private readonly AnnotationToolbar toolbar;
    private readonly Border magnifier;
    private readonly Image zoom = new() { Width = 132, Height = 96, Stretch = Stretch.Fill };
    private readonly TextBlock coordinate = new() { Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Rectangle[] handles = new Rectangle[8];
    private Point start;
    private Rect selected = Rect.Empty;
    private bool drawing;
    private bool moving;
    private int resizeHandle = -1;
    private Rect moveRect;
    private Point lastPointer;
    private double chromeScale;
    private readonly System.Collections.Generic.IReadOnlyList<WindowTarget> windowTargets = WindowTargets.Snapshot();
    private readonly Rectangle windowOutline = new() { Stroke = RailWindow.Brush("#75E4CA"), StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private WindowTarget? hoverWindow;
    internal event Action<CaptureResult, Forms.Screen>? Completed;
    internal event Action? Cancelled;
    internal event Action<System.Drawing.Rectangle>? ScrollRequested;

    internal CaptureWindow(System.Drawing.Rectangle desktop, BitmapSource snapshot)
    {
        this.desktop = desktop; this.snapshot = snapshot;
        Title = "随手挂 · 选择截图区域";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Black;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); Cursor = Cursors.Cross;
        var root = new Grid();
        root.Children.Add(new Image { Source = snapshot, Stretch = Stretch.Fill }); root.Children.Add(canvas);
        canvas.Children.Add(shade);
        editor = new AnnotationCanvas(snapshot);
        canvas.Children.Add(editor);
        canvas.Children.Add(windowOutline);
        canvas.Children.Add(selection); canvas.Children.Add(sizeLabel);
        for (var index = 0; index < handles.Length; index++)
        {
            var handle = new Rectangle { Width = 7, Height = 7, Fill = Brushes.White, Stroke = RailWindow.Brush("#126B64"), StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            handles[index] = handle; canvas.Children.Add(handle);
        }
        hint = new Border { Background = RailWindow.Brush("#EC15282D"), CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 12, 18, 12), IsHitTestVisible = false,
            Child = new TextBlock { Text = "单击选窗口 / 拖动框选  ·  Enter 挂图  ·  Esc 取消  ·  Ctrl+A 全屏", Foreground = Brushes.White, FontSize = 14 } };
        Canvas.SetTop(hint, 20); Canvas.SetLeft(hint, 24); canvas.Children.Add(hint);
        var magnified = new Grid(); magnified.Children.Add(zoom);
        magnified.Children.Add(new System.Windows.Shapes.Line { X1 = 66, X2 = 66, Y1 = 0, Y2 = 96, Stroke = Brushes.White, StrokeThickness = .7 });
        magnified.Children.Add(new System.Windows.Shapes.Line { X1 = 0, X2 = 132, Y1 = 48, Y2 = 48, Stroke = Brushes.White, StrokeThickness = .7 });
        var magnifierContent = new StackPanel(); magnifierContent.Children.Add(magnified); magnifierContent.Children.Add(coordinate);
        magnifier = new Border { Child = magnifierContent, Background = RailWindow.Brush("#EB15282D"), BorderBrush = RailWindow.Brush("#C7D9D3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        RenderOptions.SetBitmapScalingMode(zoom, BitmapScalingMode.NearestNeighbor);
        canvas.Children.Add(magnifier);
        toolbar = new AnnotationToolbar(editor, true) { Visibility = Visibility.Collapsed };
        toolbar.Confirmed += Confirm; toolbar.Cancelled += () => Cancelled?.Invoke();
        toolbar.FullScreenRequested += SelectFullScreen; toolbar.SaveRequested += SaveAs;
        toolbar.OcrRequested += () => { if (!selected.IsEmpty) new OcrWindow(editor.Export(PixelRect()).Flatten()) { Owner = this, Topmost = true }.Show(); };
        toolbar.ScrollRequested += () =>
        {
            if (selected.IsEmpty || drawing || editor.IsDrawing) return;
            if (editor.History.Marks.Count > 0) { MessageBox.Show(this, "长截图请先选择内容区域，再在拼接完成后添加标注。当前标注不会应用到长图。", "长截图"); return; }
            var box = PixelRect(); ScrollRequested?.Invoke(new System.Drawing.Rectangle(desktop.Left + box.X, desktop.Top + box.Y, box.Width, box.Height));
        };
        editor.Changed += Redraw;
        canvas.Children.Add(toolbar);
        Content = root;
        Loaded += (_, _) =>
        {
            Native.Place(this, desktop, true);
            Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            Activate(); Focus(); Keyboard.Focus(this); Redraw();
        };
        PreviewKeyDown += KeyPressed;
        canvas.MouseLeftButtonDown += Down; canvas.MouseMove += Move; canvas.MouseLeftButtonUp += Up;
        canvas.MouseRightButtonDown += (_, _) => { if (!editor.CancelDraft()) Cancelled?.Invoke(); };
        SizeChanged += (_, _) => Redraw();
    }

    internal static BitmapSource Take(Forms.Screen screen)
        => Take(screen.Bounds);
    internal static BitmapSource Take(System.Drawing.Rectangle bounds)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); return source;
        }
        finally { DeleteObject(handle); }
    }
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (Within(e.OriginalSource as DependencyObject, toolbar) || editor.Tool != AnnotationTool.Select) return;
        var point = Clamp(e.GetPosition(canvas));
        lastPointer = point;
        if (selected.IsEmpty)
        {
            var pixel = editor.Pixel(point); var global = new System.Drawing.Point(desktop.Left+(int)pixel.X,desktop.Top+(int)pixel.Y);
            hoverWindow = System.Linq.Enumerable.FirstOrDefault(windowTargets,target => target.Bounds.Contains(global));
        }
        windowOutline.Visibility = Visibility.Collapsed;
        if (e.ClickCount == 2 && selected.Contains(point)) { Confirm(); return; }
        resizeHandle = HitHandle(point);
        moving = resizeHandle < 0 && selected.Contains(point);
        moveRect = selected; start = point; drawing = true;
        toolbar.Visibility = Visibility.Collapsed;
        canvas.CaptureMouse();
        if (!moving && resizeHandle < 0) selected = new Rect(start, start);
        e.Handled = true;
    }
    private static bool Within(DependencyObject? element, DependencyObject parent)
    {
        while (element != null) { if (element == parent) return true; element = VisualTreeHelper.GetParent(element); }
        return false;
    }
    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, canvas.ActualWidth), Math.Clamp(point.Y, 0, canvas.ActualHeight));
    private Point[] HandlePoints() => selected.IsEmpty ? Array.Empty<Point>() : new[]
    {
        selected.TopLeft, new Point(selected.X + selected.Width / 2, selected.Top), selected.TopRight,
        new Point(selected.Right, selected.Y + selected.Height / 2), selected.BottomRight,
        new Point(selected.X + selected.Width / 2, selected.Bottom), selected.BottomLeft,
        new Point(selected.Left, selected.Y + selected.Height / 2)
    };
    private int HitHandle(Point point)
    {
        var positions = HandlePoints();
        for (var index = 0; index < positions.Length; index++)
            if (Math.Abs(point.X - positions[index].X) <= 7 && Math.Abs(point.Y - positions[index].Y) <= 7) return index;
        return -1;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        lastPointer = Clamp(e.GetPosition(canvas));
        if (!drawing && selected.IsEmpty)
        {
            var pixel = editor.Pixel(lastPointer);
            var global = new System.Drawing.Point(desktop.Left + (int)pixel.X, desktop.Top + (int)pixel.Y);
            hoverWindow = System.Linq.Enumerable.FirstOrDefault(windowTargets, target => target.Bounds.Contains(global));
            windowOutline.Visibility = hoverWindow == null ? Visibility.Collapsed : Visibility.Visible;
            if (hoverWindow != null)
            {
                var box = PhysicalRect(hoverWindow.Bounds);
                Canvas.SetLeft(windowOutline, box.X); Canvas.SetTop(windowOutline, box.Y); windowOutline.Width = box.Width; windowOutline.Height = box.Height;
            }
        }
        if (drawing)
        {
            if (resizeHandle >= 0)
            {
                var left = moveRect.Left; var top = moveRect.Top; var right = moveRect.Right; var bottom = moveRect.Bottom;
                if (resizeHandle is 0 or 6 or 7) left = Math.Min(lastPointer.X, right - 1);
                if (resizeHandle is 2 or 3 or 4) right = Math.Max(lastPointer.X, left + 1);
                if (resizeHandle is 0 or 1 or 2) top = Math.Min(lastPointer.Y, bottom - 1);
                if (resizeHandle is 4 or 5 or 6) bottom = Math.Max(lastPointer.Y, top + 1);
                selected = new Rect(new Point(left, top), new Point(right, bottom));
            }
            else selected = moving
                ? new Rect(Math.Clamp(moveRect.X + lastPointer.X - start.X, 0, Math.Max(0, canvas.ActualWidth - moveRect.Width)), Math.Clamp(moveRect.Y + lastPointer.Y - start.Y, 0, Math.Max(0, canvas.ActualHeight - moveRect.Height)), moveRect.Width, moveRect.Height)
                : new Rect(start, lastPointer);
            Redraw();
        }
        if (editor.Tool == AnnotationTool.Select && !Within(e.OriginalSource as DependencyObject, toolbar))
        {
            var hit = drawing ? resizeHandle : HitHandle(lastPointer);
            Cursor = hit is 0 or 4 ? Cursors.SizeNWSE : hit is 2 or 6 ? Cursors.SizeNESW : hit is 1 or 5 ? Cursors.SizeNS : hit is 3 or 7 ? Cursors.SizeWE : selected.Contains(lastPointer) ? Cursors.SizeAll : Cursors.Cross;
            UpdateMagnifier();
        }
        else magnifier.Visibility = Visibility.Collapsed;
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (!drawing) return;
        drawing = false; canvas.ReleaseMouseCapture();
        if (!moving && resizeHandle < 0 && (lastPointer - start).Length < 4 && hoverWindow != null) selected = PhysicalRect(hoverWindow.Bounds);
        else if (selected.Width < 2 || selected.Height < 2) selected = Rect.Empty;
        windowOutline.Visibility = Visibility.Collapsed;
        Redraw();
    }
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (!editor.CancelDraft()) Cancelled?.Invoke(); e.Handled = true; return; }
        if (editor.HasTextEditor)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { editor.CommitText(); editor.Focus(); e.Handled = true; }
            return;
        }
        if (Keyboard.FocusedElement is ComboBox || Keyboard.FocusedElement is ComboBoxItem) return;
        if (editor.ObjectKey(e.Key, Keyboard.Modifiers)) { e.Handled = true; return; }
        if (e.Key == Key.Enter && Keyboard.FocusedElement is ButtonBase) return;
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (control && e.Key == Key.A) { SelectFullScreen(); e.Handled = true; }
        else if (control && e.Key == Key.Z) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) editor.Redo(); else editor.Undo(); e.Handled = true; }
        else if (control && e.Key == Key.Y) { editor.Redo(); e.Handled = true; }
        else if (control && e.Key == Key.S) { SaveAs(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Confirm(); e.Handled = true; }
        else if (!selected.IsEmpty && editor.Tool == AnnotationTool.Select && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var dx = (e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0) * canvas.ActualWidth / snapshot.PixelWidth;
            var dy = (e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0) * canvas.ActualHeight / snapshot.PixelHeight;
            selected = new Rect(Math.Clamp(selected.X + dx, 0, Math.Max(0, canvas.ActualWidth - selected.Width)), Math.Clamp(selected.Y + dy, 0, Math.Max(0, canvas.ActualHeight - selected.Height)), selected.Width, selected.Height);
            Redraw(); e.Handled = true;
        }
    }
    private void SelectFullScreen()
    {
        editor.CommitText(); editor.SetTool(AnnotationTool.Select);
        selected = MonitorRect(Native.CursorScreen); Redraw();
    }
    private Rect MonitorRect(Forms.Screen screen) => new(
        (screen.Bounds.Left - desktop.Left) * canvas.ActualWidth / snapshot.PixelWidth,
        (screen.Bounds.Top - desktop.Top) * canvas.ActualHeight / snapshot.PixelHeight,
        screen.Bounds.Width * canvas.ActualWidth / snapshot.PixelWidth,
        screen.Bounds.Height * canvas.ActualHeight / snapshot.PixelHeight);
    private Rect PhysicalRect(System.Drawing.Rectangle bounds) => new(
        (bounds.Left - desktop.Left) * canvas.ActualWidth / snapshot.PixelWidth,
        (bounds.Top - desktop.Top) * canvas.ActualHeight / snapshot.PixelHeight,
        bounds.Width * canvas.ActualWidth / snapshot.PixelWidth, bounds.Height * canvas.ActualHeight / snapshot.PixelHeight);
    private Forms.Screen SelectionScreen()
    {
        if (selected.IsEmpty) return Native.CursorScreen;
        var x = desktop.Left + (int)((selected.X + selected.Width / 2) * snapshot.PixelWidth / canvas.ActualWidth);
        var y = desktop.Top + (int)((selected.Y + selected.Height / 2) * snapshot.PixelHeight / canvas.ActualHeight);
        return Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
    }
    private void UpdateMagnifier()
    {
        var point = editor.Pixel(lastPointer);
        const int width = 33, height = 24;
        var x = Math.Clamp((int)point.X - width / 2, 0, Math.Max(0, snapshot.PixelWidth - width));
        var y = Math.Clamp((int)point.Y - height / 2, 0, Math.Max(0, snapshot.PixelHeight - height));
        zoom.Source = new CroppedBitmap(snapshot, new Int32Rect(x, y, Math.Min(width, snapshot.PixelWidth), Math.Min(height, snapshot.PixelHeight)));
        coordinate.Text = $"{Math.Clamp((int)point.X, 0, snapshot.PixelWidth - 1)}, {Math.Clamp((int)point.Y, 0, snapshot.PixelHeight - 1)} px";
        magnifier.Visibility = Visibility.Visible;
        var monitor = MonitorRect(Native.CursorScreen);
        Canvas.SetLeft(magnifier, Math.Clamp(lastPointer.X + 165 < monitor.Right ? lastPointer.X + 22 : lastPointer.X - 165, monitor.Left, Math.Max(monitor.Left, monitor.Right - 150)));
        Canvas.SetTop(magnifier, Math.Clamp(lastPointer.Y + 155 < monitor.Bottom ? lastPointer.Y + 24 : lastPointer.Y - 155, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - 140)));
    }
    private void Redraw()
    {
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
        shade.Width = canvas.ActualWidth; shade.Height = canvas.ActualHeight;
        editor.Width = canvas.ActualWidth; editor.Height = canvas.ActualHeight;
        var activeMonitor = MonitorRect(SelectionScreen());
        var scale = Native.Dpi(SelectionScreen()) / VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (Math.Abs(scale - chromeScale) > .001)
        {
            chromeScale = scale;
            var transform = new ScaleTransform(scale, scale); transform.Freeze();
            toolbar.LayoutTransform = transform; hint.LayoutTransform = transform; magnifier.LayoutTransform = transform;
        }
        Canvas.SetLeft(hint, activeMonitor.Left + 20); Canvas.SetTop(hint, activeMonitor.Top + 20);
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight)));
        if (!selected.IsEmpty) geometry.Children.Add(new RectangleGeometry(selected));
        shade.Clip = geometry;
        var valid = !selected.IsEmpty && selected.Width >= 2 && selected.Height >= 2;
        selection.Visibility = sizeLabel.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        var handlePoints = HandlePoints();
        for (var index = 0; index < handles.Length; index++)
        {
            handles[index].Visibility = valid && editor.Tool == AnnotationTool.Select ? Visibility.Visible : Visibility.Collapsed;
            if (handlePoints.Length > 0) { Canvas.SetLeft(handles[index], handlePoints[index].X - 3.5); Canvas.SetTop(handles[index], handlePoints[index].Y - 3.5); }
        }
        if (!valid) { toolbar.Visibility = Visibility.Collapsed; editor.Bounds = Rect.Empty; editor.InvalidateVisual(); return; }
        selection.Width = selected.Width; selection.Height = selected.Height;
        Canvas.SetLeft(selection, selected.X); Canvas.SetTop(selection, selected.Y);
        var pixels = PixelRect();
        editor.Bounds = new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height); editor.InvalidateVisual();
        sizeLabel.Text = $"{pixels.Width} × {pixels.Height} px";
        Canvas.SetLeft(sizeLabel, Math.Clamp(selected.X, activeMonitor.Left, Math.Max(activeMonitor.Left, activeMonitor.Right - 180))); Canvas.SetTop(sizeLabel, Math.Max(activeMonitor.Top, selected.Y - 32));
        toolbar.Visibility = !drawing ? Visibility.Visible : Visibility.Collapsed;
        toolbar.MaxWidth = Math.Max(250, activeMonitor.Width - 20);
        toolbar.Measure(new System.Windows.Size(toolbar.MaxWidth, double.PositiveInfinity));
        Canvas.SetLeft(toolbar, Math.Clamp(selected.Right - toolbar.DesiredSize.Width, activeMonitor.Left + 10, Math.Max(activeMonitor.Left + 10, activeMonitor.Right - toolbar.DesiredSize.Width - 10)));
        var below = selected.Bottom + 10;
        var top = below + toolbar.DesiredSize.Height <= activeMonitor.Bottom ? below : selected.Top - toolbar.DesiredSize.Height - 10;
        top = Math.Clamp(top, activeMonitor.Top + 10, Math.Max(activeMonitor.Top + 10, activeMonitor.Bottom - toolbar.DesiredSize.Height - 10));
        Canvas.SetTop(toolbar, top);
        if (editor.Tool != AnnotationTool.Select) magnifier.Visibility = Visibility.Collapsed;
    }
    private Int32Rect PixelRect()
    {
        var x = Math.Clamp((int)Math.Round(selected.X * snapshot.PixelWidth / canvas.ActualWidth), 0, snapshot.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Round(selected.Y * snapshot.PixelHeight / canvas.ActualHeight), 0, snapshot.PixelHeight - 1);
        var right = Math.Clamp((int)Math.Round(selected.Right * snapshot.PixelWidth / canvas.ActualWidth), x + 1, snapshot.PixelWidth);
        var bottom = Math.Clamp((int)Math.Round(selected.Bottom * snapshot.PixelHeight / canvas.ActualHeight), y + 1, snapshot.PixelHeight);
        return new Int32Rect(x, y, right - x, bottom - y);
    }
    private void SaveAs()
    {
        if (drawing || selected.IsEmpty || selected.Width < 2 || selected.Height < 2) return;
        editor.CommitText();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"随手挂-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        try { if (dialog.ShowDialog(this) == true) ImageStore.Write(editor.Export(PixelRect()).Flatten(), dialog.FileName); }
        catch (Exception ex) { App.Log(ex); MessageBox.Show(this, ex.Message, "另存为失败"); }
    }
    private void Confirm()
    {
        if (drawing || editor.IsDrawing || selected.IsEmpty || selected.Width < 2 || selected.Height < 2) return;
        Completed?.Invoke(editor.Export(PixelRect()), SelectionScreen());
    }
}
