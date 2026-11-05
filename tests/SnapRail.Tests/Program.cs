using System.Text.Json;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Windows.Threading;
using SnapRail;

internal static class Program
{
    private static int passed;
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--scroll-layout") { ScrollLayoutProbe.Run(args[1]); return; }
        if (args.Length == 2 && args[0] == "--drop-target") { DropTarget.Run(args[1]); return; }
        if (args.Length == 1 && args[0] == "--probe-gdi")
        {
            GdiProbe.Run(); return;
        }
        if (args.Length == 2 && args[0] == "--reserve-hotkey")
        {
            var app = new Application();
            using var keys = new Hotkeys();
            if (!keys.Configure(new HotkeySpec { Key = 0x51 }, new HotkeySpec { Key = 0x59 }, out var error)) throw new Exception(error);
            File.WriteAllText(args[1], "ready"); app.Run(); return;
        }
        Run("Undo, redo and branch discard", () =>
        {
            var history = new AnnotationHistory();
            var first = Mark(AnnotationTool.Rectangle, 10, 10, 50, 40);
            var second = Mark(AnnotationTool.Arrow, 30, 40, 80, 60);
            history.Add(first); history.Add(second); history.Undo();
            Check(history.Marks.Count == 1 && history.CanRedo, "undo");
            history.Redo(); Check(ReferenceEquals(history.Marks[1], second), "redo preserves mark");
            history.Undo(); history.Add(Mark(AnnotationTool.Ellipse, 5, 5, 20, 20));
            Check(!history.CanRedo, "new operation discards redo branch");
        });
        var source = Pattern(120, 100);
        Run("Welcome PNGs seed once, preserve order and skip existing screenshots", () =>
        {
            var root=Temp("welcome");WelcomeSeed.Ensure(root);
            var files=Directory.GetFiles(Path.Combine(root,"images"),"*.png");Check(files.Length==10,"expected ten welcome images");
            var store=new ImageStore(root);store.Load();Check(store.Items.Count==10 && RailPolicy.Visible(store.Items)[0].Id=="welcome01","welcome order");
            var removed=files[0];File.Delete(removed);WelcomeSeed.Ensure(root);Check(!File.Exists(removed),"deleted welcome reappeared");
            var populated=Temp("welcome-existing");Directory.CreateDirectory(Path.Combine(populated,"images"));ImageStore.Write(source,Path.Combine(populated,"images","existing.png"));
            WelcomeSeed.Ensure(populated);Check(Directory.GetFiles(Path.Combine(populated,"images"),"*.png").Length==1,"existing user got welcome screenshots");
        });
        Run("Welcome initialization preserves invalid existing library metadata", () =>
        {
            var root=Temp("welcome-corrupt");Directory.CreateDirectory(root);
            var path=Path.Combine(root,"library.json");var corrupt="{\"Version\":1,\"Items\":null}";File.WriteAllText(path,corrupt);
            WelcomeSeed.Ensure(root);var store=new ImageStore(root);store.Load();
            Check(store.Library.LoadError!=null && File.ReadAllText(path)==corrupt,"welcome initialization bypassed metadata protection");
            Check(Directory.GetFiles(store.Images,"*.png").Length==0,"invalid old metadata was treated as a new user");
        });
        Run("Portable settings follow moved folder and preserve preferences", () =>
        {
            var first=Temp("portable-first");var second=Temp("portable-moved");
            var settings=new SettingsStore(first,true);var preferences=settings.Load();preferences.PhotoWidth=170;settings.Save(preferences);
            Check(JsonDocument.Parse(File.ReadAllText(Path.Combine(first,"settings.json"))).RootElement.GetProperty("DataRoot").GetString()==".","portable config contains an absolute path");
            Directory.CreateDirectory(second);File.Copy(Path.Combine(first,"settings.json"),Path.Combine(second,"settings.json"));
            var moved=new SettingsStore(second,true).Load();Check(moved.DataRoot==second && moved.PhotoWidth==170,"portable path or preferences did not follow folder");
        });
        Run("Object edits, text changes and delete undo preserve old versions", () =>
        {
            var history = new AnnotationHistory(); var original = Mark(AnnotationTool.Rectangle,10,10,70,60); history.Add(original);
            var moved = original.Offset(12,8); moved.Color = "#2675D8"; history.Replace(0,moved);
            Check(original.Points[0].X == 10 && original.Color == "#E34B4B","original mark mutated");
            history.Undo(); Check(ReferenceEquals(history.Marks[0],original),"edit undo"); history.Redo(); Check(history.Marks[0].Points[0].X == 22,"edit redo");
            history.Remove(0); Check(history.Marks.Count == 0,"delete"); history.Undo(); Check(history.Marks[0].Color == "#2675D8","delete undo");
            var text = new Annotation { Tool = AnnotationTool.Text,Text="旧文字",Points=new(){new(2,2)} }; history.Add(text);
            var edited = text.Offset(0,0); edited.Text="新文字"; history.Replace(1,edited); history.Undo(); Check(history.Marks[1].Text=="旧文字","text undo");
        });
        Run("Scroll stitching removes exact overlap and rejects unsafe frames", () =>
        {
            var page = Pattern(120,600);
            BitmapSource Frame(int y) => ImageStore.Detach(new CroppedBitmap(page,new Int32Rect(0,y,120,240)));
            var stitcher = new ScrollStitcher(Frame(0));
            Check(stitcher.Append(Frame(80)) == StitchStatus.Added,"first overlap");
            Check(stitcher.Append(Frame(170)) == StitchStatus.Added,"variable overlap");
            Check(stitcher.Append(Frame(170)) == StitchStatus.Unchanged,"bottom unchanged");
            Check(stitcher.Append(Pattern(100,240)) == StitchStatus.NoMatch,"resized target");
            var image = stitcher.Render(); Check(image.PixelHeight==410,"stitched height");
            for (var y=0;y<410;y+=7) for(var x=0;x<120;x+=11) Check(Pixel(image,x,y)==Pixel(page,x,y),"seam changed pixels");
            var repeated = new ScrollStitcher(ImageStore.Detach(new CroppedBitmap(page,new Int32Rect(0,0,120,400))));
            Check(repeated.Append(ImageStore.Detach(new CroppedBitmap(page,new Int32Rect(0,16,120,400))))==StitchStatus.NoMatch,"ambiguous repeating image should not silently stitch");
        });
        Run("Undo cache is bounded and settings retain old defaults", () =>
        {
            var history = new AnnotationHistory(); for(var i=0;i<120;i++) history.Add(Mark(AnnotationTool.Arrow,i,0,i+10,10));
            for(var i=0;i<120;i++) history.Undo(); Check(history.Marks.Count==20,"only recent 100 operations retained");
            var settings=JsonSerializer.Deserialize<AppSettings>("{\"Version\":1}")!; settings.Validate();
            Check(settings.PhotoWidth==150 && settings.HoverDelayMs==250 && settings.HideDelayMs==700,"setting defaults changed");
            settings.PhotoWidth=1000; var rejected=false; try{settings.Validate();}catch(IOException){rejected=true;} Check(rejected,"unsafe photo size accepted");
        });
        Run("Pixel-aligned rectangle and clipping", () =>
        {
            var mark = Mark(AnnotationTool.Rectangle, 10, 10, 50, 40);
            var result = Render(source, mark);
            Check(Pixel(result, 10, 20) == (227, 75, 75, 255), "rectangle expected pixel");
            Check(Pixel(result, 80, 80) == Pixel(source, 80, 80), "unmarked pixels unchanged");
            var outside = Render(source, Mark(AnnotationTool.Rectangle, -10, -10, 150, 130));
            Check(Pixel(outside, 50, 50) == Pixel(source, 50, 50), "off-image marks clip without corrupting content");
        });
        Run("Local mosaic preserves every outside pixel", () =>
        {
            var mosaic = Mark(AnnotationTool.Mosaic, 20, 20, 60, 60);
            var result = Render(source, mosaic);
            var insideChanged = false;
            for (var y = 0; y < 100; y++)
            for (var x = 0; x < 120; x++)
            {
                var same = Pixel(result, x, y) == Pixel(source, x, y);
                if (x < 20 || x >= 60 || y < 20 || y >= 60) Check(same, "mosaic leaked outside region");
                else if (!same) insideChanged = true;
            }
            Check(insideChanged, "mosaic changes content");
            Check(Pixel(result, 21, 21) == Pixel(result, 30, 30), "block is averaged");
        });
        Run("All tools and Unicode text render", () =>
        {
            foreach (var tool in new[] { AnnotationTool.Rectangle, AnnotationTool.Ellipse, AnnotationTool.Arrow, AnnotationTool.Pen, AnnotationTool.Text, AnnotationTool.Mosaic })
            {
                var mark = Mark(tool, 10, 10, 80, 70); mark.Text = "随手挂\nSnapRail";
                var result = Render(source, mark);
                Check(result.PixelWidth == 120 && result.PixelHeight == 100, tool + " size");
                var changed = false;
                for (var y = 0; y < 100 && !changed; y++)
                for (var x = 0; x < 120; x++) if (Pixel(result, x, y) != Pixel(source, x, y)) { changed = true; break; }
                Check(changed, tool + " draws visible pixels");
            }
        });
        Run("Crop rebases marks and serializes editable data", () =>
        {
            var canvas = new AnnotationCanvas(source) { Width = 120, Height = 100 };
            canvas.History.Add(Mark(AnnotationTool.Arrow, 30, 30, 70, 60));
            var crop = canvas.Export(new Int32Rect(20, 20, 80, 70));
            Check(crop.Document.Marks[0].Points[0] == new PixelPoint(10, 10), "relative coordinate");
            var json = JsonSerializer.Serialize(crop.Document, ImageStore.JsonOptions);
            var loaded = JsonSerializer.Deserialize<AnnotationDocument>(json, ImageStore.JsonOptions)!;
            Check(loaded.Marks[0].Tool == AnnotationTool.Arrow && loaded.Width == 80 && loaded.Height == 70, "round trip");
            Check(Pixel(crop.Original, 0, 0) == Pixel(source, 20, 20), "original crop exact");
        });
        Run("Save, restore, re-edit, preserve old version", () =>
        {
            var root = Temp("store");
            var store = new ImageStore(root);
            var document = new AnnotationDocument { Width = 120, Height = 100, Marks = new() { Mark(AnnotationTool.Rectangle, 10, 10, 50, 40) } };
            var item = store.Add(new CaptureResult(source, document));
            store.Save(item).GetAwaiter().GetResult();
            Check(item.PendingImage == null && item.PendingSource == null && File.Exists(item.Path), "saved and released memory");
            Check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "no temporary files after success");
            var before = File.ReadAllBytes(item.Path);
            var restored = new ImageStore(root); restored.Load();
            Check(restored.Items.Count == 1, "restored item");
            var editable = restored.Editable(restored.Items[0]);
            Check(editable.Document.Marks.Count == 1 && Pixel(editable.Original, 10, 20) == Pixel(source, 10, 20), "unflattened source restored");
            editable.Document.Marks.Clear();
            var revision = restored.Add(editable); restored.Save(revision).GetAwaiter().GetResult();
            Check(item.Path != revision.Path && before.SequenceEqual(File.ReadAllBytes(item.Path)), "old version unchanged");
            Check(Pixel(ImageStore.Read(revision.Path), 10, 20) == Pixel(source, 10, 20), "annotation removal restores original pixels");
        });
        Run("Failed save retains original, annotations and export", () =>
        {
            var root = Temp("failure"); Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "images"), "blocked");
            var store = new ImageStore(root);
            var item = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 }));
            try { store.Save(item).GetAwaiter().GetResult(); throw new Exception("Expected failure"); }
            catch (IOException) { }
            Check(item.PendingImage != null && item.PendingSource != null && item.Path == "", "unsaved data retained");
        });
        Run("Legacy v0.1 PNG opens as editable source", () =>
        {
            var root = Temp("legacy"); var store = new ImageStore(root); Directory.CreateDirectory(store.Images);
            ImageStore.Write(source, Path.Combine(store.Images, "20261102-120000-000-" + Guid.NewGuid().ToString("N") + ".png"));
            store.Load(); var editable = store.Editable(store.Items[0]);
            Check(editable.Document.Marks.Count == 0 && editable.Original.PixelWidth == 120, "legacy fallback");
        });
        Run("Saved thumbnails do not retain full desktop snapshots", () =>
        {
            var retained = CroppedCapture();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!retained.Desktop.IsAlive, "thumbnail retains entire desktop source");
            Check(retained.Item.Thumbnail.PixelWidth <= 360 && retained.Item.PendingSource == null, "small independent thumbnail remains");
            GC.KeepAlive(retained.Item);
        });
        Run("Favorites and pinned images survive the recent-image limit", () =>
        {
            var root = Temp("library"); var store = new ImageStore(root);
            for (var i = 0; i < 36; i++)
            {
                var item = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 }));
                store.Save(item).GetAwaiter().GetResult();
            }
            var favorite = store.Items[^1]; favorite.State.Favorite = true;
            var pinned = store.Items[^2]; pinned.State.IsPinned = true; pinned.State.Placement = new PinPlacement { X = -1700, Y = 100, Width = 500, Height = 300 };
            store.Library.SaveAsync().GetAwaiter().GetResult();
            var loaded = new ImageStore(root); loaded.Load();
            Check(loaded.Items.Count == 32, "30 recent plus favorite and pin");
            Check(RailPolicy.Visible(loaded.Items).First().Id == favorite.Id, "favorite appears first");
            var restoredPin = loaded.Items.Single(item => item.Id == pinned.Id);
            Check(restoredPin.State.Placement?.X == -1700 && !RailPolicy.Visible(loaded.Items).Contains(restoredPin), "pin position restored and removed from rail");
            restoredPin.State.IsPinned = false; restoredPin.State.LastHungUtcTicks = DateTime.UtcNow.Ticks;
            loaded.Library.SaveAsync().GetAwaiter().GetResult();
            var returned = new ImageStore(root); returned.Load();
            Check(RailPolicy.Visible(returned.Items).Any(item => item.Id == pinned.Id), "old returned pin remains visible after restart");
        });
        Run("Unsaved images remain accessible beyond the rail limit", () =>
        {
            var items = Enumerable.Range(0, 40).Select(index => new CaptureItem { Created = DateTime.Now.AddMinutes(-index), State = new ItemState() }).ToList();
            items[^1].PendingImage = source;
            Check(RailPolicy.Visible(items).Contains(items[^1]) && RailPolicy.Visible(items).Length == 31, "unsaved oldest image retained");
        });
        Run("State writes are serialized and preserve the latest snapshot", () =>
        {
            var root = Temp("state-order"); var state = new LibraryStateStore(root);
            state.Get("capture").Favorite = true; var first = state.SaveAsync();
            state.Get("capture").Favorite = false; var second = state.SaveAsync();
            Task.WhenAll(first, second).GetAwaiter().GetResult();
            var loaded = new LibraryStateStore(root); loaded.Load();
            Check(!loaded.Get("capture").Favorite, "latest state wins");
            Check(File.Exists(Path.Combine(root, "library.json.bak")), "previous committed snapshot backed up");
        });
        Run("Corrupt state is not overwritten", () =>
        {
            var root = Temp("state-corrupt"); Directory.CreateDirectory(root);
            var path = Path.Combine(root, "library.json"); File.WriteAllText(path, "not-json");
            var state = new LibraryStateStore(root); state.Load(); Check(state.LoadError != null, "load error retained");
            state.Get("capture").Favorite = true;
            try { state.SaveAsync().GetAwaiter().GetResult(); throw new Exception("Expected protected write failure"); } catch (IOException) { }
            Check(File.ReadAllText(path) == "not-json", "corrupt original remains untouched");
        });
        Run("Pin positions recover from absent screens and negative coordinates", () =>
        {
            var work = new System.Drawing.Rectangle(-1920, 0, 1920, 1080);
            var recovered = PinLayout.Clamp(new System.Drawing.Rectangle(-5000, -500, 900, 700), work);
            Check(recovered.X == -1920 && recovered.Y == 0 && recovered.Width == 900, "negative monitor clamping");
            var oversized = PinLayout.Clamp(new System.Drawing.Rectangle(5000, 5000, 5000, 4000), work);
            Check(oversized == work, "oversized window fits screen");
        });
        Run("Drag exports contain an independent PNG and a bitmap", () =>
        {
            var root = Temp("export"); var store = new ImageStore(root);
            var item = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 }));
            store.Save(item).GetAwaiter().GetResult(); var original = File.ReadAllBytes(item.Path);
            var data = OnSta(() => new ExportService(root).Prepare(item));
            Check(data.ContainsFileDropList() && data.ContainsImage(), "file and bitmap representations");
            var exported = data.GetFileDropList()[0]!;
            Check(exported != item.Path && File.Exists(exported), "independent file");
            Check(ImageStore.Read(exported).PixelWidth == 120, "readable PNG");
            File.Delete(exported);
            Check(File.ReadAllBytes(item.Path).SequenceEqual(original), "consumer removing export does not affect original");
            var recreated = OnSta(() => new ExportService(root).Prepare(item));
            Check(File.Exists(recreated.GetFileDropList()[0]), "missing export recreated on next drag");
        });
        Run("Thin and small screenshots have bounded thumbnails without upscaling", () =>
        {
            var root = Temp("thumbnail"); var store = new ImageStore(root);
            var tall = Pattern(2, 2000);
            var item = store.Add(new CaptureResult(tall, new AnnotationDocument { Width = 2, Height = 2000 }));
            Check(item.Thumbnail.PixelWidth >= 1 && item.Thumbnail.PixelHeight <= 240, "new thin capture bounded");
            store.Save(item).GetAwaiter().GetResult();
            var thumbnail = ImageStore.Read(item.Path, 360);
            Check(thumbnail.PixelWidth >= 1 && thumbnail.PixelHeight <= 240, "loaded thin capture bounded");
            var small = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 }));
            store.Save(small).GetAwaiter().GetResult();
            var loaded = ImageStore.Read(small.Path, 360);
            Check(loaded.PixelWidth == 120 && loaded.PixelHeight == 100, "small image not upscaled");
        });
        Run("History paging, filtering and hidden images", () =>
        {
            var store = new ImageStore(Temp("history"));
            for (var i = 0; i < 27; i++) { var item = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 })); store.Save(item).GetAwaiter().GetResult(); }
            store.Items[0].State.Hidden = true; store.Items[1].State.Favorite = true;
            var first = store.History("", "全部", 0); var second = store.History("", "全部", 1);
            Check(first.Total == 27 && first.Items.Length == 24 && second.Items.Length == 3, "paged 24/3");
            Check(store.History("", "收藏", 0).Items.Single().Id == store.Items[1].Id, "favorite filter");
            Check(store.History("", "待挂", 0).Items.Any(item => item.Id == store.Items[0].Id), "hidden image in awaiting filter");
            Check(!RailPolicy.Visible(store.Items).Contains(store.Items[0]), "hidden excluded from rail");
            Check(store.History(store.Items[0].Id, "全部", 0).Total == 1, "filename search");
        });
        Run("Recycle bundle restores flattened PNG and editable annotations", () =>
        {
            var root = Temp("recycle"); var store = new ImageStore(root);
            var item = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100, Marks = new() { Mark(AnnotationTool.Arrow, 10, 10, 80, 70) } }));
            store.Save(item).GetAwaiter().GetResult(); item.State.Favorite = true;
            var bytes = File.ReadAllBytes(item.Path); var bin = Path.Combine(root, "fake-bin"); Directory.CreateDirectory(bin);
            var service = new RecycleService(store);
            service.Recycle(item, bundle => Directory.Move(bundle, Path.Combine(bin, Path.GetFileName(bundle)))).GetAwaiter().GetResult();
            Check(item.State.Deleted && !File.Exists(item.Path), "recycled");
            var restored = service.Restore(item.Id, name => Directory.Move(Path.Combine(bin, name), Path.Combine(service.RecoveryRoot, name))).GetAwaiter().GetResult();
            Check(File.ReadAllBytes(restored.Path).SequenceEqual(bytes) && restored.State.Favorite && !restored.State.Deleted, "image and state restored");
            Check(store.Editable(restored).Document.Marks.Count == 1, "editable annotations restored");
        });
        Run("Restoring a deleted favorite cannot exceed the three-favorite limit", () =>
        {
            var root=Temp("recycle-favorite-limit");var store=new ImageStore(root);
            var old=store.Add(new CaptureResult(source,new AnnotationDocument { Width=120,Height=100 }));store.Save(old).GetAwaiter().GetResult();old.State.Favorite=true;
            var bin=Path.Combine(root,"fake-bin");Directory.CreateDirectory(bin);var service=new RecycleService(store);
            service.Recycle(old,bundle=>Directory.Move(bundle,Path.Combine(bin,Path.GetFileName(bundle)))).GetAwaiter().GetResult();
            for(var i=0;i<3;i++) store.Add(new CaptureResult(source,new AnnotationDocument { Width=120,Height=100 })).State.Favorite=true;
            var restored=service.Restore(old.Id,name=>Directory.Move(Path.Combine(bin,name),Path.Combine(service.RecoveryRoot,name))).GetAwaiter().GetResult();
            Check(File.Exists(restored.Path) && !restored.State.Favorite && !restored.State.Deleted,"restoration lost image or exceeded favorite limit");
            Check(store.Library.Entries.Count(entry=>entry.Value.Favorite && !entry.Value.Deleted)==3,"favorite cap after undo");
        });
        Run("Recycle failure rolls image and annotation files back", () =>
        {
            var store = new ImageStore(Temp("recycle-failure"));
            var item = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 })); store.Save(item).GetAwaiter().GetResult();
            try { new RecycleService(store).Recycle(item, _ => throw new IOException("simulated shell failure")).GetAwaiter().GetResult(); throw new Exception("Expected failure"); } catch (IOException) { }
            Check(File.Exists(item.Path) && !item.State.Deleted && store.Editable(item).Original.PixelWidth == 120, "original files remain usable");
        });
        Run("Settings round-trip and conflicting key validation", () =>
        {
            var store = new SettingsStore(Temp("settings"));
            var settings = new AppSettings { Capture = new HotkeySpec { Key = 0x51 }, HoverEnabled = false };
            store.Save(settings); var loaded = store.Load();
            Check(loaded.Capture.Key == 0x51 && !loaded.HoverEnabled, "settings round-trip");
            loaded.Toggle = loaded.Capture;
            try { loaded.Validate(); throw new Exception("Expected conflicting keys rejected"); } catch (IOException) { }
            Check(store.Load().Toggle.Key == 0x54, "failed validation did not change file");
        });
        Run("Data migration verifies copies and preserves originals", () =>
        {
            var root = Temp("migration-source"); var store = new ImageStore(root);
            var item = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 })); store.Save(item).GetAwaiter().GetResult();
            store.Library.SaveAsync().GetAwaiter().GetResult();
            var target = Temp("migration-target"); DataMigration.Copy(root, target).GetAwaiter().GetResult();
            var loaded = new ImageStore(target); loaded.Load();
            Check(loaded.Items.Count == 1 && File.ReadAllBytes(item.Path).SequenceEqual(File.ReadAllBytes(loaded.Items[0].Path)), "verified image copy");
            Check(loaded.Editable(loaded.Items[0]).Original.PixelWidth == 120 && File.Exists(item.Path), "documents and source preserved");
            try { DataMigration.Copy(root, target).GetAwaiter().GetResult(); throw new Exception("Expected nonempty target rejected"); } catch (IOException) { }
            try { DataMigration.Copy(root, Path.Combine(root, "child")).GetAwaiter().GetResult(); throw new Exception("Expected nested target rejected"); } catch (IOException) { }
        });
        Run("Deleted image can be restored after changing the data root", () =>
        {
            var root = Temp("trash-source"); var store = new ImageStore(root);
            var item = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 })); store.Save(item).GetAwaiter().GetResult();
            var bin = Temp("trash-bin"); Directory.CreateDirectory(bin); var service = new RecycleService(store);
            service.Recycle(item, bundle => Directory.Move(bundle, Path.Combine(bin, Path.GetFileName(bundle)))).GetAwaiter().GetResult();
            store.Library.SaveAsync().GetAwaiter().GetResult(); var target = Temp("trash-new-root"); DataMigration.Copy(root, target).GetAwaiter().GetResult();
            var destination = new ImageStore(target); destination.Load();
            var restored = new RecycleService(destination).Restore(item.Id, name => Directory.Move(Path.Combine(bin, name), Path.Combine(service.RecoveryRoot, name))).GetAwaiter().GetResult();
            Check(restored.Path.StartsWith(destination.Images) && File.Exists(restored.Path) && destination.Editable(restored).Original.PixelWidth == 120, "restored to active root");
        });
        Run("Live thumbnail trimming keeps history files and pending captures", () =>
        {
            var store = new ImageStore(Temp("live-cache"));
            for (var i = 0; i < 45; i++)
            {
                var item = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 }));
                store.Save(item).GetAwaiter().GetResult(); store.TrimLiveItems();
            }
            Check(store.Items.Count == 30 && store.History("", "全部", 0).Total == 45, "history retained outside live cache");
            var pending = store.Add(new CaptureResult(source, new AnnotationDocument { Width = 120, Height = 100 })); store.TrimLiveItems();
            Check(store.Items.Contains(pending) && pending.PendingImage != null, "pending data retained");
        });
        Run("Native image clipboard round-trip preserves dimensions and pixel colors", () =>
        {
            using var owner = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("SnapRail.ClipboardTest") { ParentWindow = new IntPtr(-3) });
            ImageClipboard.Set(source, owner.Handle);
            var pasted = Clipboard.GetImage(); Check(pasted != null && pasted.PixelWidth == 120 && pasted.PixelHeight == 100, "clipboard bitmap dimensions");
            Check(Pixel(pasted!, 0, 0) == Pixel(source, 0, 0) && Pixel(pasted!, 119, 99) == Pixel(source, 119, 99), "clipboard orientation and colors");
        });
        Console.WriteLine($"PASS: {passed} behavioral checks");
    }
    private static T OnSta<T>(Func<Task<T>> action)
    {
        var previous = SynchronizationContext.Current;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            var task = action(); var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame); return task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CaptureItem Item, WeakReference Desktop) CroppedCapture()
    {
        var desktop = Pattern(1000, 800);
        var weak = new WeakReference(desktop);
        var canvas = new AnnotationCanvas(desktop);
        var crop = canvas.Export(new Int32Rect(200, 200, 500, 300));
        var store = new ImageStore(Temp("memory"));
        var item = store.Add(crop); store.Save(item).GetAwaiter().GetResult();
        return (item, weak);
    }
    private static string Temp(string suffix) => Path.Combine(Path.GetTempPath(), "SnapRail.Tests", Guid.NewGuid().ToString("N") + "-" + suffix);
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static Annotation Mark(AnnotationTool tool, double x1, double y1, double x2, double y2) => new() { Tool = tool, Points = new() { new(x1, y1), new(x2, y2) } };
    private static BitmapSource Render(BitmapSource source, Annotation mark) => AnnotationRenderer.Render(source, new AnnotationDocument { Width = source.PixelWidth, Height = source.PixelHeight, Marks = new() { mark } });
    private static BitmapSource Pattern(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            pixels[i] = (byte)((x + y) % 2 == 0 ? 20 : 240); pixels[i + 1] = (byte)(y * 2); pixels[i + 2] = (byte)(x * 2); pixels[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze(); return bitmap;
    }
    private static (byte R, byte G, byte B, byte A) Pixel(BitmapSource source, int x, int y)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4]; converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0], pixel[3]);
    }
}
