using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Media;

namespace SnapRail;

internal sealed class CaptureItem
{
    internal string Id { get; init; } = Guid.NewGuid().ToString("N");
    internal string Path { get; set; } = "";
    internal BitmapSource Thumbnail { get; set; } = null!;
    internal BitmapSource? PendingImage { get; set; }
    internal CaptureResult? PendingSource { get; set; }
    internal bool IsSaving { get; set; }
    internal ItemState State { get; init; } = new();
    internal string Status { get; set; } = "保存中…";
    internal DateTime Created { get; init; } = DateTime.Now;
    internal int Width { get; init; }
    internal int Height { get; init; }
}

internal sealed class ImageStore
{
    internal string Root { get; }
    internal LibraryStateStore Library { get; }
    internal ImageStore(string? root = null)
    {
        Root = root ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapRail");
        Library = new LibraryStateStore(Root);
    }
    internal string Images => System.IO.Path.Combine(Root, "images");
    internal string Documents => System.IO.Path.Combine(Root, "documents");
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    internal readonly List<CaptureItem> Items = new();
    internal void TrimLiveItems()
    {
        var keep = RailPolicy.Visible(Items).Select(item => item.Id).ToHashSet();
        Items.RemoveAll(item => !keep.Contains(item.Id) && !item.State.IsPinned && item.PendingImage == null);
    }

    internal void Load()
    {
        Directory.CreateDirectory(Images);
        Library.Load();
        var ordinary = 0;
        var candidates = new DirectoryInfo(Images).EnumerateFiles("*.png")
            .Select(file => (File: file, Id: System.IO.Path.GetFileNameWithoutExtension(file.Name).Split('-').Last()))
            .Select(entry => (entry.File, entry.Id, State: Library.Get(entry.Id)))
            .OrderByDescending(entry => entry.State.LastHungUtcTicks > 0 ? entry.State.LastHungUtcTicks : entry.File.CreationTimeUtc.Ticks);
        foreach (var candidate in candidates)
        {
            try
            {
                var file = candidate.File.FullName;
                var id = candidate.Id;
                var state = candidate.State;
                if (state.Deleted || (state.Hidden && !state.IsPinned)) continue;
                if (!state.Favorite && !state.IsPinned && ordinary >= RailPolicy.RecentLimit) continue;
                var thumb = Read(file, 360);
                Items.Add(new CaptureItem { Id = id, State = state, Path = file, Thumbnail = thumb, Created = File.GetCreationTime(file), Status = "已保存" });
                if (!state.Favorite && !state.IsPinned) ordinary++;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or System.IO.FileFormatException or UnauthorizedAccessException)
            { App.Log(ex); }
        }
    }
    internal CaptureItem LoadItem(string path)
    {
        var id = System.IO.Path.GetFileNameWithoutExtension(path).Split('-').Last();
        return new CaptureItem { Id = id, State = Library.Get(id), Path = path, Thumbnail = Read(path, 360), Created = File.GetCreationTime(path), Status = "已保存" };
    }
    internal HistoryPage History(string query, string filter, int page)
    {
        var visible = RailPolicy.Visible(Items).Select(item => item.Id).ToHashSet();
        var files = new DirectoryInfo(Images).EnumerateFiles("*.png").OrderByDescending(file => file.CreationTimeUtc)
            .Where(file => string.IsNullOrWhiteSpace(query) || (file.Name + file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var id = System.IO.Path.GetFileNameWithoutExtension(file.Name).Split('-').Last(); var state = Library.Get(id);
                return !state.Deleted && (filter switch { "收藏" => state.Favorite, "挂图" => visible.Contains(id), "待挂" => !visible.Contains(id) && !state.IsPinned, _ => true });
            }).ToArray();
        var pages = Math.Max(1, (files.Length + 23) / 24); page = Math.Clamp(page, 0, pages - 1);
        var rows = new List<CaptureItem>();
        foreach (var file in files.Skip(page * 24).Take(24))
        {
            try { rows.Add(LoadItem(file.FullName)); } catch (Exception ex) { App.Log(ex); }
        }
        return new HistoryPage(rows.ToArray(), page, pages, files.Length);
    }

    internal CaptureItem Add(CaptureResult source)
    {
        var bitmap = source.Flatten();
        var id = Guid.NewGuid().ToString("N");
        var item = new CaptureItem { Id = id, State = Library.Get(id), Thumbnail = Thumbnail(bitmap), PendingImage = bitmap, PendingSource = source, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight };
        Items.Insert(0, item);
        return item;
    }

    internal async Task Save(CaptureItem item)
    {
        var bitmap = item.PendingImage;
        if (bitmap is null) return;
        var path = System.IO.Path.Combine(Images, $"{item.Created:yyyyMMdd-HHmmss-fff}-{item.Id}.png");
        var source = item.PendingSource;
        await Task.Run(() =>
        {
            Directory.CreateDirectory(Images);
            if (source != null)
            {
                var documentRoot = System.IO.Path.Combine(Documents, item.Id);
                Directory.CreateDirectory(documentRoot);
                AtomicWrite(System.IO.Path.Combine(documentRoot, "original.png"), file => Write(source.Original, file));
                AtomicWrite(System.IO.Path.Combine(documentRoot, "annotations.json"), file => File.WriteAllText(file, JsonSerializer.Serialize(source.Document, JsonOptions)));
            }
            var temporary = path + ".tmp";
            try
            {
                Write(bitmap, temporary);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
        item.Path = path;
        item.PendingImage = null;
        item.PendingSource = null;
        item.Status = "已保存";
    }

    internal static BitmapSource Original(CaptureItem item) => item.PendingImage ?? Read(item.Path);
    private static BitmapSource Thumbnail(BitmapSource source)
    {
        var scale = Math.Min(1, Math.Min(360d / source.PixelWidth, 240d / source.PixelHeight));
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawImage(source, new Rect(0, 0, width, height));
        var thumbnail = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        thumbnail.Render(visual); thumbnail.Freeze(); return thumbnail;
    }
    internal static BitmapSource Detach(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze(); return result;
    }
    internal CaptureResult Editable(CaptureItem item)
    {
        if (item.PendingSource != null) return item.PendingSource;
        var documentRoot = System.IO.Path.Combine(Documents, item.Id);
        var metadata = System.IO.Path.Combine(documentRoot, "annotations.json");
        if (!File.Exists(metadata))
        {
            var bitmap = Original(item);
            return new CaptureResult(bitmap, new AnnotationDocument { Width = bitmap.PixelWidth, Height = bitmap.PixelHeight });
        }
        var original = Read(System.IO.Path.Combine(documentRoot, "original.png"));
        var document = JsonSerializer.Deserialize<AnnotationDocument>(File.ReadAllText(metadata), JsonOptions)
            ?? throw new IOException("无法读取标注数据，原截图仍保留在历史中。");
        if (document.Version != 1 || document.Width != original.PixelWidth || document.Height != original.PixelHeight)
            throw new IOException("标注版本或尺寸不匹配，原截图仍保留在历史中。");
        return new CaptureResult(original, document);
    }
    private static void AtomicWrite(string path, Action<string> write)
    {
        var temporary = path + ".tmp";
        try { write(temporary); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static BitmapImage Read(string path, int decodeWidth = 0)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0)
        {
            var header = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            var scale = Math.Min(1, Math.Min((double)decodeWidth / header.PixelWidth, 240d / header.PixelHeight));
            image.DecodePixelWidth = Math.Max(1, (int)Math.Round(header.PixelWidth * scale));
            image.DecodePixelHeight = Math.Max(1, (int)Math.Round(header.PixelHeight * scale));
            stream.Position = 0;
        }
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
    internal static void Write(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }
}

internal sealed record HistoryPage(CaptureItem[] Items, int Page, int Pages, int Total);
