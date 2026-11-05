using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;

namespace SnapRail;

public sealed class HotkeySpec
{
    public uint Modifiers { get; set; } = 3;
    public uint Key { get; set; } = 0x41;
    public override string ToString() => ((Modifiers & 2) != 0 ? "Ctrl+" : "") + ((Modifiers & 1) != 0 ? "Alt+" : "") + ((Modifiers & 4) != 0 ? "Shift+" : "") + ((Modifiers & 8) != 0 ? "Win+" : "") + KeyInterop.KeyFromVirtualKey((int)Key);
    internal void Validate()
    {
        if ((Modifiers & ~15u) != 0 || Key == 0 || Key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C || Key > 0xFE ||
            (Modifiers == 0 && (Key < 0x70 || Key > 0x87)) || (Modifiers == 1 && Key == 0x73))
            throw new IOException("请使用修饰键与普通按键组合，或单独使用 F1–F24；Alt+F4 保留给系统。");
    }
}
public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public HotkeySpec Capture { get; set; } = new();
    public HotkeySpec Toggle { get; set; } = new() { Key = 0x54 };
    public string DataRoot { get; set; } = SettingsStore.DefaultRoot;
    public string RailScreen { get; set; } = "cursor";
    public bool HoverEnabled { get; set; } = true;
    public bool AnimationEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public int PhotoWidth { get; set; } = 150;
    public int RailTopMargin { get; set; } = 10;
    public int HoverDelayMs { get; set; } = 250;
    public int HideDelayMs { get; set; } = 700;
    internal AppSettings Copy() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, ImageStore.JsonOptions), ImageStore.JsonOptions)!;
    internal void Validate()
    {
        if (Version != 1 || Capture == null || Toggle == null) throw new IOException("设置版本无法识别。");
        Capture.Validate(); Toggle.Validate();
        if (PhotoWidth < 150 || PhotoWidth > 240 || RailTopMargin < 0 || RailTopMargin > 100 || HoverDelayMs < 100 || HoverDelayMs > 1500 || HideDelayMs < 300 || HideDelayMs > 5000) throw new IOException("挂图尺寸或延迟超出允许范围。");
        if (Capture.Key == Toggle.Key && Capture.Modifiers == Toggle.Modifiers) throw new IOException("截图和显示挂图不能使用同一个快捷键。");
        DataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DataRoot));
    }
}

internal sealed class SettingsStore
{
    internal static string? PortableRoot => File.Exists(Path.Combine(AppContext.BaseDirectory,"SnapRail.portable")) ? Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) : null;
    internal static string DefaultRoot => PortableRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapRail");
    private readonly string path;
    private readonly string root;
    private readonly bool portable;
    internal SettingsStore(string? configurationRoot = null,bool portable = false) { this.portable=portable ||(configurationRoot==null && PortableRoot!=null); root = configurationRoot ?? DefaultRoot; path = Path.Combine(root, "settings.json"); }
    internal AppSettings Load()
    {
        if (!File.Exists(path)) return new AppSettings { DataRoot = root };
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), ImageStore.JsonOptions) ?? throw new IOException("无法读取设置。");
        if(portable) settings.DataRoot=root;
        settings.Validate(); return settings;
    }
    internal void Save(AppSettings settings)
    {
        if(portable) settings.DataRoot=root;
        settings.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        var stored=settings.Copy();if(portable) stored.DataRoot=".";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(stored, ImageStore.JsonOptions)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal static class DataMigration
{
    internal static async Task Copy(string source, string target)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)); target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) return;
        if (target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("新目录不能包含当前数据目录，也不能位于当前目录内部。");
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("请选择一个空目录，避免覆盖已有文件。");
        Directory.CreateDirectory(target);
        await Task.Run(() =>
        {
            foreach (var name in new[] { "images", "documents", "recovery" })
                if (Directory.Exists(Path.Combine(source, name))) CopyDirectory(Path.Combine(source, name), Path.Combine(target, name));
            foreach (var name in new[] { "library.json", "library.json.bak" })
                if (File.Exists(Path.Combine(source, name))) CopyFile(Path.Combine(source, name), Path.Combine(target, name));
        });
    }
    internal static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in new DirectoryInfo(source).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var path = Path.Combine(target, Path.GetRelativePath(source, file.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); CopyFile(file.FullName, path);
        }
    }
    internal static void CopyFile(string source, string target)
    {
        // Stream bytes rather than copying EFS attributes to a different volume.
        using (var input = File.OpenRead(source))
        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
        using (var a = File.OpenRead(source))
        using (var b = File.OpenRead(target))
            if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new IOException("目录复制校验失败，原数据仍保留。");
        File.SetCreationTimeUtc(target, File.GetCreationTimeUtc(source)); File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
    }
}

internal static class Autostart
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string? Current()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue("SnapRail") as string;
    }
    internal static void Set(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (command == null) key.DeleteValue("SnapRail", false); else key.SetValue("SnapRail", command, RegistryValueKind.String);
    }
    internal static string Command(string? profile = null)
    {
        var executable = Environment.ProcessPath!;
        var command = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{executable}\" \"{Path.Combine(AppContext.BaseDirectory, "SnapRail.dll")}\"" : $"\"{executable}\"";
        return profile == null ? command : command + " --profile \"" + profile + "\"";
    }
}
