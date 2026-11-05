using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapRail;

internal sealed class ImagePreviewWindow : Window
{
    internal string ItemId { get; }
    private readonly BitmapSource bitmap;
    private readonly Image image;
    private readonly ScrollViewer view;
    private readonly TextBlock info;
    private double zoom = 1;
    private bool fitting = true;
    internal ImagePreviewWindow(App app,CaptureItem item,BitmapSource bitmap)
    {
        this.bitmap = bitmap; ItemId = item.Id;
        Title = "随手挂 · 查看图片"; Width = 1040; Height = 780; MinWidth = 480; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = RailWindow.Brush("#203238");
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new DockPanel();
        var header = new WrapPanel { Margin = new Thickness(12) };
        Add(header,"适应窗口","preview-fit",() => { fitting = true; Fit(); });
        Add(header,"实际大小","preview-actual",() => SetZoom(1));
        Add(header,"−","preview-zoom-out",() => SetZoom(zoom/1.25));
        Add(header,"＋","preview-zoom-in",() => SetZoom(zoom*1.25));
        var copy = Add(header,"复制","preview-copy",async () => await app.Copy(item));
        Add(header,"另存为","preview-save",() => app.SaveAs(item));
        Add(header,"编辑副本","preview-edit",() => app.Edit(item));
        Add(header,"关闭  Esc","preview-close",Close);
        DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
        info = new TextBlock { Foreground = RailWindow.Brush("#D6E5E0"),Margin = new Thickness(16,8,16,12),TextWrapping = TextWrapping.Wrap };
        System.Windows.Automation.AutomationProperties.SetAutomationId(info,"preview-info");
        DockPanel.SetDock(info,Dock.Bottom); root.Children.Add(info);
        image = new Image { Source = bitmap,Stretch = Stretch.Fill,HorizontalAlignment = HorizontalAlignment.Left,VerticalAlignment = VerticalAlignment.Top,SnapsToDevicePixels = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(image,"preview-image");
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
        view = new ScrollViewer { Content = image,HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,VerticalScrollBarVisibility = ScrollBarVisibility.Auto,Margin = new Thickness(12,0,12,0) };
        root.Children.Add(view); Content = root;
        view.SizeChanged += (_, _) => { if (fitting) Fit(); };
        view.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SetZoom(zoom*(e.Delta>0?1.25:.8)); e.Handled = true; } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.C) { _ = app.Copy(item); e.Handled = true; }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.S) { app.SaveAs(item); e.Handled = true; }
        };
        Loaded += (_, _) => Fit();
    }
    private static Button Add(Panel panel,string label,string id,Action action)
    {
        var button = new Button { Content = label,Margin = new Thickness(3),Padding = new Thickness(10,7,10,7) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button,id); button.Click += (_, _) => action(); panel.Children.Add(button); return button;
    }
    private void Fit()
    {
        if (view.ActualWidth<=20 || view.ActualHeight<=20) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        zoom = Math.Min(1,Math.Min((view.ActualWidth-20)*dpi.DpiScaleX/bitmap.PixelWidth,(view.ActualHeight-20)*dpi.DpiScaleY/bitmap.PixelHeight));
        ApplyZoom();
    }
    private void SetZoom(double value)
    {
        fitting = false; var ratio = Math.Clamp(value,.01,8)/zoom;
        var x = (view.HorizontalOffset+view.ViewportWidth/2)*ratio-view.ViewportWidth/2;
        var y = (view.VerticalOffset+view.ViewportHeight/2)*ratio-view.ViewportHeight/2;
        zoom = Math.Clamp(value,.01,8); ApplyZoom(); view.UpdateLayout(); view.ScrollToHorizontalOffset(Math.Max(0,x)); view.ScrollToVerticalOffset(Math.Max(0,y));
    }
    private void ApplyZoom()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        image.Width = bitmap.PixelWidth*zoom/dpi.DpiScaleX; image.Height = bitmap.PixelHeight*zoom/dpi.DpiScaleY;
        info.Text = $"{bitmap.PixelWidth}×{bitmap.PixelHeight} 像素 · {zoom:P0} · Ctrl+滚轮缩放，滚轮滚动查看长图";
    }
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi,newDpi);
        if (view != null) Dispatcher.BeginInvoke(new Action(() => { if (fitting) Fit(); else ApplyZoom(); }));
    }
}
