using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapRail;

internal static class WelcomeSeed
{
    internal static bool IsWelcome(CaptureItem item) => Path.GetFileName(item.Path).StartsWith("welcome-",StringComparison.OrdinalIgnoreCase);
    internal static void Ensure(string root)
    {
        var done=Path.Combine(root,"welcome-seeded");var pending=Path.Combine(root,"welcome-seed.pending");
        var images=Path.Combine(root,"images");
        if(File.Exists(done)) return;
        Directory.CreateDirectory(images);
        var existing=Directory.GetFiles(images,"*.png");
        if(existing.Any(path=>!Path.GetFileName(path).StartsWith("welcome-",StringComparison.OrdinalIgnoreCase))) return;
        if(!File.Exists(pending))
        {
            if(existing.Length>0) return;
            var library=Path.Combine(root,"library.json");
            if(File.Exists(library))
            {
                try
                {
                    var previous=System.Text.Json.JsonSerializer.Deserialize<LibraryDocument>(File.ReadAllText(library),ImageStore.JsonOptions);
                    if(previous==null || previous.Version!=1 || previous.Items==null || previous.Items.Count!=0) return;
                }
                catch(System.Text.Json.JsonException) { return; }
            }
        }
        File.WriteAllText(pending,"Welcome cards initialization");
        var letters="截一张，随手挂一下！";
        var colors=new[]{"#D96563","#CE9438","#4B95B0","#9974B3","#559C79","#DA778E","#6E83C1","#D48A53","#A279BD","#DD7961"};
        var created=DateTime.UtcNow;
        for(var i=0;i<letters.Length;i++)
        {
            var path=Path.Combine(images,$"welcome-{i+1:00}-welcome{i+1:00}.png");
            if(File.Exists(path)) continue;
            var panel=new Border { Width=196,Height=158,Background=RailWindow.Brush("#FFF8E7"),CornerRadius=new CornerRadius(13),
                Child=new WelcomeLetter(letters[i].ToString(),colors[i]) { Margin=new Thickness(14) } };
            panel.Measure(new Size(196,158));panel.Arrange(new Rect(0,0,196,158));panel.UpdateLayout();
            var bitmap=new RenderTargetBitmap(196,158,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);bitmap.Freeze();
            var temporary=path+".tmp";
            try { ImageStore.Write(bitmap,temporary);File.Move(temporary,path);File.SetCreationTimeUtc(path,created.AddSeconds(-i)); }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
        File.WriteAllText(done,"Welcome cards created once. Screenshots remain in images.");File.Delete(pending);
    }
}
