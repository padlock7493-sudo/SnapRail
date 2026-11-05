using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnapRail;

internal static class DropTarget
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var app = new Application();
        var label = new TextBlock { Text = "SnapRail 拖图测试\n\n把挂图拖到这里\n接收文件副本和位图，不发送到网络", Foreground = Brushes.White, FontSize = 24, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var window = new Window { Title = "SnapRail QA Drop Target", Width = 700, Height = 400, Left = 400, Top = 500, Background = new SolidColorBrush(Color.FromRgb(18, 107, 100)), Content = label, AllowDrop = true };
        window.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        window.Drop += (_, e) =>
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            foreach (var file in files) File.Copy(file, Path.Combine(output, Path.GetFileName(file)), true);
            File.WriteAllText(Path.Combine(output, "drop.json"), JsonSerializer.Serialize(new { Files = files, HasBitmap = e.Data.GetDataPresent(DataFormats.Bitmap), Effects = e.AllowedEffects.ToString() }));
            label.Text = "已接收 PNG 副本";
            e.Effects = DragDropEffects.Copy; e.Handled = true;
        };
        window.Loaded += (_, _) =>
        {
            var rectangle = Native.Bounds(window);
            File.WriteAllText(Path.Combine(output, "ready.json"), JsonSerializer.Serialize(new { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height }));
        };
        app.Run(window);
    }
}
