using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapRail;

internal sealed record OcrText(string Text, string Language);
internal static class OcrService
{
    internal static async Task<OcrText> Recognize(BitmapSource source, CancellationToken cancellation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapRail", "ocr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, "image.png"); var script = Path.Combine(directory, "worker.ps1");
        try
        {
            var scale = Math.Min(1, 4000d / Math.Max(source.PixelWidth, source.PixelHeight));
            var input = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
            input.Freeze();
            await Task.Run(() => ImageStore.Write(input, image), cancellation);
            using (var resource = typeof(App).Assembly.GetManifestResourceStream("SnapRail.OcrWorker.ps1") ?? throw new IOException("缺少本机 OCR 组件，请重新解压完整便携包。"))
            using (var file = File.Create(script)) await resource.CopyToAsync(file, cancellation);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ImagePath", image }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("无法启动本机文字识别。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
            var text = await output; var failure = await error;
            if (process.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(failure) ? "本机 OCR 未完成，请检查 Windows 文字识别语言组件。" : failure.Trim());
            return JsonSerializer.Deserialize<OcrText>(text.TrimStart('\uFEFF')) ?? throw new IOException("识别结果无法读取。");
        }
        finally { foreach (var file in new[] { image, script }) try { File.Delete(file); } catch (IOException) { } }
    }
}

internal sealed class OcrWindow : Window
{
    private readonly CancellationTokenSource cancellation = new();
    internal OcrWindow(BitmapSource bitmap)
    {
        Title = "随手挂 · 文字识别"; Width = 740; Height = 560; MinWidth = 440; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); Background = RailWindow.Brush("#F5FAF8");
        var root = new DockPanel { Margin = new Thickness(20) };
        var status = new TextBlock { Text = "正在本机识别文字…可关闭窗口取消。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,14) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "ocr-status"); DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        var copy = new Button { Content = "复制全部文字", IsEnabled = false, Margin = new Thickness(4) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(copy,"ocr-copy"); actions.Children.Add(copy);
        var close = new Button { Content = "关闭 / 取消", Margin = new Thickness(4) }; close.Click += (_, _) => Close(); actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 17, Padding = new Thickness(12), IsReadOnly = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(text,"ocr-text"); root.Children.Add(text); Content = root;
        copy.Click += async (_, _) =>
        {
            for (var i = 0; i < 5; i++) try { Clipboard.SetText(text.Text); status.Text = "已复制文字"; return; } catch (System.Runtime.InteropServices.COMException) { await Task.Delay(80 * (i + 1)); }
            status.Text = "剪贴板忙，请稍后重试。文字仍保留在窗口中。";
        };
        Loaded += async (_, _) =>
        {
            try
            {
                var result = await OcrService.Recognize(bitmap,cancellation.Token);
                if (cancellation.IsCancellationRequested) return;
                text.Text = result.Text; text.IsReadOnly = false; copy.IsEnabled = result.Text.Length > 0;
                status.Text = result.Text.Length == 0 ? "未识别到文字；可尝试更清晰的截图。" : $"本机识别完成 · {result.Language} · 可修改后复制" + (Math.Max(bitmap.PixelWidth,bitmap.PixelHeight)>4000 ? "（大图已缩小识别）" : "");
            }
            catch (OperationCanceledException) { if (!cancellation.IsCancellationRequested) status.Text = "识别超时，请关闭后重试。"; }
            catch (Exception ex) { App.Log(ex); if (!cancellation.IsCancellationRequested) status.Text = "未完成：" + ex.Message; }
        };
        Closed += (_, _) => cancellation.Cancel();
    }
}
