using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SnapRail;

internal sealed class PinWindow : Window
{
    internal CaptureItem Item { get; }
    private readonly App app;
    private readonly BitmapSource bitmap;
    private readonly DispatcherTimer positionTimer;
    private readonly TextBlock label;
    private bool placing;
    private System.Drawing.Rectangle? requestedPlacement;

    internal PinWindow(App app, CaptureItem item)
    {
        this.app = app; Item = item; bitmap = ImageStore.Original(item);
        Title = "随手挂 · 桌面贴图 " + item.Id;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        Background = Brushes.White;
        BorderBrush = RailWindow.Brush("#91AAA3"); BorderThickness = new Thickness(1);
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
        var header = new DockPanel { Background = RailWindow.Brush("#EAF3EF"), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        var copy = Action("复制", "pin-copy"); copy.Click += async (_, _) => await app.Copy(item);
        var edit = Action("编辑", "pin-edit"); edit.Click += (_, _) => app.Edit(item);
        var ocr = Action("识字", "pin-ocr"); ocr.Click += (_, _) => app.Ocr(item);
        var back = Action("挂回", "pin-return"); back.ToolTip = "关闭贴图并挂回绳子，图片保留"; back.Click += (_, _) => Close();
        actions.Children.Add(copy); actions.Children.Add(edit); actions.Children.Add(ocr); actions.Children.Add(back);
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        label = new TextBlock { Text = "置顶贴图", Foreground = RailWindow.Brush("#253A40"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
        header.Children.Add(label);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ResetSize(); e.Handled = true; }
            else if (!InsideButton(e.OriginalSource as DependencyObject)) { DragMove(); Remember(); e.Handled = true; }
        };
        layout.Children.Add(header);
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(3), ToolTip = "滚轮缩放；Ctrl+滚轮调整透明度；拖动上沿移动；Alt+F4 挂回" };
        Grid.SetRow(image, 1); layout.Children.Add(image); Content = layout;
        PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Opacity = Math.Clamp(Opacity + Math.Sign(e.Delta) * .1, .2, 1);
            else Zoom(e.Delta > 0 ? 1.12 : 1 / 1.12);
            e.Handled = true;
        };
        positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        positionTimer.Tick += (_, _) => { positionTimer.Stop(); Remember(); };
        LocationChanged += (_, _) => ScheduleRemember(); SizeChanged += (_, _) => ScheduleRemember();
        Loaded += (_, _) =>
        {
            var requested = item.State.Placement?.Rectangle ?? DefaultBounds();
            Place(requested); Remember();
        };
        Closed += (_, _) => { positionTimer.Stop(); app.PinClosed(this); };
    }
    private static Button Action(string name, string id)
    {
        var button = new Button { Content = name, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(2), MinHeight = 30, FontSize = 12 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, id); return button;
    }
    private static bool InsideButton(DependencyObject? source)
    {
        while (source is Visual)
        {
            if (source is ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
    private System.Drawing.Rectangle DefaultBounds()
    {
        var work = Native.CursorScreen.WorkingArea;
        var scale = Math.Min(1, Math.Min(work.Width * .65 / bitmap.PixelWidth, work.Height * .65 / bitmap.PixelHeight));
        var width = Math.Max(300, (int)(bitmap.PixelWidth * scale));
        var height = Math.Max(180, (int)(bitmap.PixelHeight * scale) + 60);
        return new System.Drawing.Rectangle(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
    }
    private void ResetSize() => Place(DefaultBounds());
    private void Zoom(double factor)
    {
        var bounds = Native.Bounds(this);
        var width = (int)(bounds.Width * factor); var height = (int)(bounds.Height * factor);
        Place(new System.Drawing.Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height));
        Remember();
    }
    internal void EnsureVisible()
    {
        var bounds = Native.Bounds(this);
        if (!bounds.IsEmpty) { Place(bounds); Remember(); }
    }
    private void Place(System.Drawing.Rectangle requested)
    {
        placing = true;
        try
        {
            requestedPlacement = PinLayout.Clamp(requested, Forms.Screen.FromRectangle(requested).WorkingArea);
            Native.Place(this, requestedPlacement.Value);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new System.Action(() => requestedPlacement = null));
        }
        finally { placing = false; }
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        var requested = requestedPlacement;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new System.Action(() =>
        {
            if (requested != null) Native.Place(this, requested.Value);
            else EnsureVisible();
        }));
    }
    private void ScheduleRemember()
    {
        if (!IsLoaded || placing || !IsVisible || app.IsExiting) return;
        positionTimer.Stop(); positionTimer.Start();
    }
    private void Remember()
    {
        if (app.IsExiting) return;
        var bounds = Native.Bounds(this);
        if (bounds.IsEmpty) return;
        Item.State.Placement = PinPlacement.From(bounds);
        label.Text = $"置顶 · {Math.Round(100d * bounds.Width / bitmap.PixelWidth)}%";
        app.ScheduleStateSave();
    }
}
