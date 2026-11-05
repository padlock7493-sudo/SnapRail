using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;
using SearchOption = System.IO.SearchOption;

namespace SnapRail;

public sealed class RecoveryRecord
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public ItemState State { get; set; } = new();
}

internal sealed class RecycleService
{
    private readonly ImageStore store;
    internal RecycleService(ImageStore store) => this.store = store;
    internal string RecoveryRoot => Path.Combine(Directory.Exists(store.Root) && (File.GetAttributes(store.Root) & FileAttributes.Encrypted) != 0
        ? Path.Combine(Path.GetTempPath(), "SnapRail") : store.Root, "recovery");
    internal async Task Recycle(CaptureItem item, Action<string>? recycle = null, string? batch = null)
    {
        if (!Guid.TryParseExact(item.Id, "N", out _) || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(item.Path)), Path.GetFullPath(store.Images), StringComparison.OrdinalIgnoreCase))
            throw new IOException("只可删除随手挂图片目录中的本地副本。");
        if (item.PendingImage != null || item.IsSaving) throw new IOException("图片还在保存，请稍后重试。");
        if ((File.GetAttributes(item.Path) & FileAttributes.ReparsePoint) != 0) throw new IOException("不处理链接图片，避免影响外部文件。");
        var bundleName = "SnapRail-" + item.Id + "-" + Guid.NewGuid().ToString("N");
        var bundle = Path.Combine(RecoveryRoot, bundleName);
        var sourceDocument = Path.Combine(store.Documents, item.Id);
        Directory.CreateDirectory(bundle);
        var snapshot = JsonSerializer.Deserialize<ItemState>(JsonSerializer.Serialize(item.State, ImageStore.JsonOptions), ImageStore.JsonOptions)!;
        var record = new RecoveryRecord { Id = item.Id, FileName = Path.GetFileName(item.Path), State = snapshot };
        File.WriteAllText(Path.Combine(bundle, "record.json"), JsonSerializer.Serialize(record, ImageStore.JsonOptions));
        try
        {
            if (Directory.Exists(sourceDocument) && (File.GetAttributes(sourceDocument) & FileAttributes.ReparsePoint) != 0) throw new IOException("标注目录是链接，未删除任何文件。");
            DataMigration.CopyFile(item.Path, Path.Combine(bundle, "image.png"));
            if (Directory.Exists(sourceDocument)) DataMigration.CopyDirectory(sourceDocument, Path.Combine(bundle, "document"));
            // Only remove originals after all bundle bytes have been verified. On any subsequent
            // shell failure the catch block restores missing files from this complete bundle.
            File.Delete(item.Path);
            if (Directory.Exists(sourceDocument))
                foreach (var file in Directory.EnumerateFiles(sourceDocument, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    File.Delete(file);
            if (recycle != null) recycle(bundle);
            else await Sta(() => FileSystem.DeleteDirectory(bundle, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException));
        }
        catch
        {
            if (Directory.Exists(Path.Combine(bundle, "document")))
                foreach (var file in Directory.EnumerateFiles(Path.Combine(bundle, "document"), "*", SearchOption.AllDirectories))
                {
                    var destination = Path.Combine(sourceDocument, Path.GetRelativePath(Path.Combine(bundle, "document"), file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (!File.Exists(destination)) DataMigration.CopyFile(file, destination);
                }
            if (File.Exists(Path.Combine(bundle, "image.png")) && !File.Exists(item.Path)) DataMigration.CopyFile(Path.Combine(bundle, "image.png"), item.Path);
            throw;
        }
        item.State.Deleted = true; item.State.IsPinned = false; item.State.TrashBundle = bundleName;
        item.State.DeletionGroup = batch ?? Guid.NewGuid().ToString("N"); item.State.DeletedUtcTicks = DateTime.UtcNow.Ticks;
        item.State.TrashOriginRoot = Path.GetDirectoryName(RecoveryRoot);
    }
    internal async Task<CaptureItem> Restore(string id, Action<string>? shellRestore = null)
    {
        var state = store.Library.Get(id);
        if (string.IsNullOrEmpty(state.TrashBundle) || Path.GetFileName(state.TrashBundle) != state.TrashBundle)
            throw new IOException("没有可恢复的删除记录。");
        var bundle = Path.Combine(state.TrashOriginRoot ?? store.Root, "recovery", state.TrashBundle);
        if (!Directory.Exists(bundle))
        {
            if (shellRestore != null) shellRestore(state.TrashBundle);
            else await Sta(() => RestoreFromShell(state.TrashBundle, bundle));
        }
        return RecoverBundle(bundle);
    }
    internal CaptureItem RecoverBundle(string bundle)
    {
        var record = JsonSerializer.Deserialize<RecoveryRecord>(File.ReadAllText(Path.Combine(bundle, "record.json")), ImageStore.JsonOptions) ?? throw new IOException("无法读取恢复记录。");
        if (!Guid.TryParseExact(record.Id, "N", out _) || Path.GetFileName(record.FileName) != record.FileName || !record.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new IOException("恢复记录中的文件路径无效。");
        var path = Path.Combine(store.Images, record.FileName); var document = Path.Combine(store.Documents, record.Id);
        Directory.CreateDirectory(store.Images); Directory.CreateDirectory(store.Documents);
        var hasDocument = Directory.Exists(Path.Combine(bundle, "document"));
        var imageExists = File.Exists(path);
        if (imageExists && !SameFile(path, Path.Combine(bundle, "image.png"))) throw new IOException("原位置已有不同内容的图片，未覆盖任何文件。");
        if (Directory.Exists(document))
        {
            var destinationFiles = Directory.GetFiles(document, "*", SearchOption.AllDirectories);
            if (destinationFiles.Any(file => !hasDocument || !SameFile(file, Path.Combine(bundle, "document", Path.GetRelativePath(document, file)))))
                throw new IOException("原位置已有不同的标注，未覆盖任何文件。");
        }
        if (hasDocument)
        {
            Directory.CreateDirectory(document);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(bundle, "document"), "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(document, Path.GetRelativePath(Path.Combine(bundle, "document"), file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination)) DataMigration.CopyFile(file, destination);
            }
        }
        var temporaryImage = path + ".restore-" + Guid.NewGuid().ToString("N");
        if (!imageExists) DataMigration.CopyFile(Path.Combine(bundle, "image.png"), temporaryImage);
        if (!imageExists) File.Move(temporaryImage, path);
        var state = store.Library.Get(record.Id);
        state.Deleted = false; state.Hidden = false; state.IsPinned = false; state.TrashBundle = null;
        state.DeletionGroup = null; state.DeletedUtcTicks = 0;
        state.TrashOriginRoot = null;
        state.Favorite = record.State.Favorite && store.Library.Entries.Count(entry=>entry.Key!=record.Id && entry.Value.Favorite && !entry.Value.Deleted)<RailPolicy.FavoriteLimit;
        state.Placement = record.State.Placement; state.LastHungUtcTicks = DateTime.UtcNow.Ticks;
        store.Library.SaveAsync().GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(bundle, "restored.json"), JsonSerializer.Serialize(new { Target = store.Root, RestoredUtc = DateTime.UtcNow }));
        // Remove only known bundle payload files after verified restoration and committed state.
        foreach (var name in new[] { "image.png", Path.Combine("document", "original.png"), Path.Combine("document", "annotations.json") })
        {
            var payload = Path.GetFullPath(Path.Combine(bundle, name));
            if (payload.StartsWith(Path.GetFullPath(bundle) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                try { if (File.Exists(payload)) File.Delete(payload); } catch (IOException ex) { App.Log(ex); }
        }
        var restored=store.LoadItem(path);
        if(record.State.Favorite && !state.Favorite) restored.Status="已还原 · 收藏已满";
        return restored;
    }
    internal void RecoverManuallyRestored()
    {
        var roots = store.Library.Entries.Where(entry => entry.Value.Deleted && entry.Value.TrashOriginRoot != null).Select(entry => Path.Combine(entry.Value.TrashOriginRoot!, "recovery")).Append(RecoveryRoot).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
        if (!Directory.Exists(root)) continue;
        foreach (var bundle in Directory.EnumerateDirectories(root, "SnapRail-*"))
            if (File.Exists(Path.Combine(bundle, "image.png")) && File.Exists(Path.Combine(bundle, "record.json")) && !File.Exists(Path.Combine(bundle, "restored.json")))
                try { var item = RecoverBundle(bundle); store.Items.RemoveAll(existing => existing.Id == item.Id); store.Items.Add(item); } catch (Exception ex) { App.Log(ex); }
        }
    }
    private static bool SameFile(string first, string second)
    {
        if (!File.Exists(first) || !File.Exists(second)) return false;
        using var a = File.OpenRead(first); using var b = File.OpenRead(second);
        return System.Security.Cryptography.SHA256.HashData(a).SequenceEqual(System.Security.Cryptography.SHA256.HashData(b));
    }
    private static void RestoreFromShell(string name, string expected)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            dynamic folder = shell.NameSpace(10);
            foreach (dynamic item in folder.Items())
            {
                if (!string.Equals((string)item.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                item.InvokeVerb("undelete");
                for (var attempt = 0; attempt < 100 && !Directory.Exists(expected); attempt++) Thread.Sleep(50);
                if (Directory.Exists(expected)) return;
                throw new IOException("回收站尚未完成恢复，请稍后重试或在系统回收站还原。");
            }
            throw new IOException("回收站中未找到该图片，可能已被清空或手动还原。");
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
    private static Task Sta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.SetResult(); } catch (Exception ex) { completion.SetException(ex); } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start(); return completion.Task;
    }
}
