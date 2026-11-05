using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SnapRail;

public sealed class PinPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    internal Rectangle Rectangle => new(X, Y, Width, Height);
    internal static PinPlacement From(Rectangle bounds) => new() { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height };
}
public sealed class ItemState
{
    public bool Favorite { get; set; }
    public bool IsPinned { get; set; }
    public long LastHungUtcTicks { get; set; }
    public long RailOrderUtcTicks { get; set; }
    public bool Hidden { get; set; }
    public bool Deleted { get; set; }
    public string? TrashBundle { get; set; }
    public string? DeletionGroup { get; set; }
    public long DeletedUtcTicks { get; set; }
    public string? TrashOriginRoot { get; set; }
    public PinPlacement? Placement { get; set; }
}
public sealed class LibraryDocument
{
    public int Version { get; set; } = 1;
    public Dictionary<string, ItemState> Items { get; set; } = new();
}

internal sealed class LibraryStateStore
{
    private readonly string path;
    private LibraryDocument document = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    internal Exception? LoadError { get; private set; }
    internal LibraryStateStore(string root) => path = Path.Combine(root, "library.json");
    internal void Load()
    {
        if (!File.Exists(path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<LibraryDocument>(File.ReadAllText(path), ImageStore.JsonOptions);
            if (loaded == null || loaded.Version != 1 || loaded.Items == null || loaded.Items.Values.Any(value => value == null))
                throw new IOException("无法识别收藏和贴图状态文件。");
            document = loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Never overwrite unreadable metadata with a fresh empty library.
            LoadError = ex;
        }
    }
    internal ItemState Get(string id)
    {
        if (!document.Items.TryGetValue(id, out var state)) { state = new ItemState(); document.Items.Add(id, state); }
        return state;
    }
    internal KeyValuePair<string, ItemState>[] Entries => document.Items.ToArray();
    internal async Task SaveAsync()
    {
        if (LoadError != null) throw new IOException("状态文件未能读取，已保留原文件，未覆盖收藏记录。", LoadError);
        // Snapshot on the UI thread; workers write immutable strings, never live dictionaries.
        var json = JsonSerializer.Serialize(document, ImageStore.JsonOptions);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                try
                {
                    File.WriteAllText(temporary, json);
                    if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                    File.Move(temporary, path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}

internal static class RailPolicy
{
    internal const int FavoriteLimit = 3;
    internal const int RecentLimit = 30;
    internal static CaptureItem[] Visible(System.Collections.Generic.IEnumerable<CaptureItem> items)
    {
        var available = items.Where(item => !item.State.IsPinned && !item.State.Hidden && !item.State.Deleted)
            .OrderByDescending(item => Math.Max(item.State.RailOrderUtcTicks,item.State.LastHungUtcTicks > 0 ? item.State.LastHungUtcTicks : item.Created.ToUniversalTime().Ticks)).ToArray();
        return available.Where(item => item.State.Favorite)
            .Concat(available.Where(item => !item.State.Favorite && item.PendingImage != null))
            .Concat(available.Where(item => !item.State.Favorite && item.PendingImage == null).Take(RecentLimit)).ToArray();
    }
}

internal static class PinLayout
{
    internal static Rectangle Clamp(Rectangle requested, Rectangle workArea)
    {
        var width = Math.Clamp(requested.Width, Math.Min(340, workArea.Width), Math.Max(1, workArea.Width));
        var height = Math.Clamp(requested.Height, Math.Min(180, workArea.Height), Math.Max(1, workArea.Height));
        return new Rectangle(Math.Clamp(requested.X, workArea.Left, workArea.Right - width),
            Math.Clamp(requested.Y, workArea.Top, workArea.Bottom - height), width, height);
    }
}
