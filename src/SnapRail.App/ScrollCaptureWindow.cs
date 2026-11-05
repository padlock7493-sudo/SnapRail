using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SnapRail;

internal sealed class ScrollCaptureWindow : Window
{
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private readonly Rectangle region;
    private readonly TextBlock status;
    private readonly Button automatic;
    private readonly Button append;
    private readonly Button finish;
    private readonly Button stop;
    private ScrollStitcher? stitcher;
    private CancellationTokenSource? running;
    private bool sampling;
    private bool closing;
    internal event Action<BitmapSource>? Completed;
    internal ScrollCaptureWindow(Rectangle region)
    {
        this.region = region;
        Title = "随手挂 · 滚动长截图"; Width = 600; MinWidth = 420; MinHeight = 240;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = true; ShowInTaskbar = true; ShowActivated = false;
        Background = RailWindow.Brush("#F5FAF8"); FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = "滚动长截图", FontSize = 21, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = "框选滚动正文并避开固定栏。自动向下滚动；也可手动滚动后追加。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) });
        status = new TextBlock { Text = "准备首帧…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(status,"scroll-status"); root.Children.Add(status);
        var actions = new WrapPanel();
        automatic = Button("自动向下滚动", "scroll-start", async () => await Auto());
        append = Button("追加当前画面", "scroll-append", async () => await Add());
        stop = Button("停止", "scroll-stop", () => { running?.Cancel(); return Task.CompletedTask; }); stop.IsEnabled = false;
        finish = Button("预览并编辑", "scroll-finish", () => { if (stitcher != null) { Completed?.Invoke(stitcher.Render()); Close(); } return Task.CompletedTask; });
        var cancel = Button("取消", "scroll-cancel", () => { Close(); return Task.CompletedTask; });
        foreach (var button in new[] { automatic,append,stop,finish,cancel }) actions.Children.Add(button);
        root.Children.Add(actions);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += async (_, _) =>
        {
            var screen = System.Windows.Forms.Screen.FromRectangle(region); var work = screen.WorkingArea;
            Constrain(screen); UpdateLayout();
            var bounds = Native.Bounds(this);
            // WPF measures height in DIPs, including the wrapped controls and title bar.
            // Position only: never overwrite that measured size with physical pixels.
            Native.SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),work.Left+Math.Max(0,(work.Width-bounds.Width)/2),work.Top+20,0,0,0x51);
            await Add();
        };
        Closed += (_, _) => { closing = true; running?.Cancel(); };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { if (running != null) running.Cancel(); else Close(); e.Handled = true; } };
    }
    private void Constrain(System.Windows.Forms.Screen screen)
    {
        var dpi = Native.Dpi(screen); var work = screen.WorkingArea;
        MinWidth = Math.Min(420,Math.Max(160,work.Width/dpi-32));
        MaxWidth = Math.Max(MinWidth,work.Width/dpi-32);
        MinHeight = Math.Min(240,Math.Max(120,work.Height/dpi-32));
        MaxHeight = Math.Max(MinHeight,work.Height/dpi-32);
    }
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi,newDpi);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(() =>
        {
            if (!closing) Constrain(System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle));
        }));
    }
    private static Button Button(string title,string id,Func<Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(3), Padding = new Thickness(9,7,9,7) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button,id);
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { App.Log(ex); MessageBox.Show(ex.Message,"长截图未完成"); } }; return button;
    }
    private async Task<BitmapSource> Sample()
    {
        Hide();
        try { await Task.Delay(130); Native.DwmFlush(); return CaptureWindow.Take(region); }
        finally { if (!closing) Show(); }
    }
    private void Controls(bool busy)
    { automatic.IsEnabled = append.IsEnabled = finish.IsEnabled = !busy; stop.IsEnabled = running != null; }
    private async Task<StitchStatus> Add()
    {
        if (sampling || closing) return StitchStatus.NoMatch;
        sampling = true; Controls(true);
        try
        {
            var frame = await Sample(); if (closing) return StitchStatus.NoMatch;
            var result = StitchStatus.Added;
            if (stitcher == null) stitcher = new ScrollStitcher(frame);
            else result = await Task.Run(() => stitcher.Append(frame));
            status.Text = $"{stitcher.Frames} 帧 · {stitcher.Width}×{stitcher.Height} 像素 · " + (result switch
            { StitchStatus.Added => "已追加", StitchStatus.Unchanged => "画面未变化 / 已到底", StitchStatus.Limit => "已达到长度或内存上限，请预览保存", _ => "无法可靠对齐，已保留之前内容；请少滚动一些，避开固定栏后重试" });
            return result;
        }
        finally { sampling = false; Controls(running != null); }
    }
    private async Task Auto()
    {
        if (running != null || stitcher == null) return;
        var center = new System.Drawing.Point(region.Left+region.Width/2,region.Top+region.Height/2);
        var target = System.Linq.Enumerable.FirstOrDefault(WindowTargets.Snapshot(), window => window.Bounds.Contains(center));
        if (target == null) { status.Text = "没有找到可滚动的目标窗口，请手动滚动后追加。"; return; }
        running = new CancellationTokenSource(); Controls(true);
        try
        {
            var activated = Native.SetForegroundWindow(target.Handle);
            status.Text = $"正在滚动：{target.Title}";
            System.Diagnostics.Debug.WriteLine($"Scroll target {target.Title}: {target.Bounds}, activated={activated}");
            await Task.Delay(180,running.Token);
            while (!running.IsCancellationRequested && !closing)
            {
                if ((GetAsyncKeyState(27) & 0x8000) != 0) break;
                System.Windows.Forms.Cursor.Position = center;
                mouse_event(0x0800,0,0,region.Height >= 500 ? -360 : -120,UIntPtr.Zero);
                await Task.Delay(650,running.Token);
                if (await Add() != StitchStatus.Added) break;
            }
        }
        catch (OperationCanceledException) { }
        finally { running.Dispose(); running = null; if (!closing) { Controls(false); status.Text += " · 已停止"; } }
    }
}
