using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace SnapRail;

internal sealed class ExportService
{
    private readonly string root;
    internal ExportService(string dataRoot) => root = Path.Combine(dataRoot, "exports");
    internal async Task<DataObject> Prepare(CaptureItem item)
    {
        var bitmap = ImageStore.Original(item);
        var path = Path.Combine(root, $"SnapRail-{item.Created:yyyyMMdd-HHmmss}-{item.Id}.png");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(root);
            if (File.Exists(path)) return;
            var temporary = path + ".tmp";
            try { ImageStore.Write(bitmap, temporary); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        data.SetImage(bitmap);
        return data;
    }
}
