using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SnapRail;

public partial class App : Application
{
    private static readonly object logLock = new();
    internal ImageStore Store { get; private set; } = new();
    internal AppSettings Configuration { get; private set; } = new();
    private SettingsStore settingsStore = new();
    private string? profileRoot;
    private readonly List<CaptureWindow> overlays = new();
    private Mutex? instance;
    private Hotkeys? hotkeys;
    private Forms.NotifyIcon? tray;
    private RailWindow rail = null!;
    private DispatcherTimer? timer;
    private bool capturing;
    private bool exiting;
    private DateTime? hoverSince;
    private bool wasVisible;
    private EditorWindow? imageEditor;
    private ImagePreviewWindow? imagePreview;
    private bool editorWasVisible;
    private readonly Dictionary<string, PinWindow> pins = new();
    private readonly List<PinWindow> hiddenPins = new();
    private DispatcherTimer? stateSaveTimer;
    private ExportService exports = null!;
    private EventWaitHandle? exitRequest;
    private EventWaitHandle? showRequest;
    private HistoryWindow? history;
    private bool maintenance;
    private SettingsWindow? settingsWindow;
    private bool historyWasVisible;
    private bool settingsWasVisible;
    private ScrollCaptureWindow? scrollCapture;
    private readonly List<Window> utilityWindows = new();
    private readonly List<Window> hiddenUtilities = new();
    internal bool IsExiting => exiting;
    internal bool IsPortable => SettingsStore.PortableRoot!=null && profileRoot==null;
    internal bool ShowWelcome { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShowWelcome = e.Args.Contains("--welcome");
        var restart = Array.IndexOf(e.Args, "--restart-after");
        if (restart >= 0 && restart + 1 < e.Args.Length && int.TryParse(e.Args[restart + 1], out var previousProcess) && previousProcess != Environment.ProcessId)
        {
            try { if (!Process.GetProcessById(previousProcess).WaitForExit(10000)) { MessageBox.Show("等待旧实例退出超时，请稍后重新启动。", "随手挂"); Shutdown(); return; } }
            catch (ArgumentException) { }
        }
        instance = new Mutex(true, @"Local\SnapRail.App", out var isNew);
        if (!isNew)
        {
            var request = e.Args.Contains("--exit") ? "ExitRequest" : "ShowRequest";
            if (EventWaitHandle.TryOpenExisting(@"Local\SnapRail." + request, out var signal))
                using (signal) signal.Set();
            Shutdown(); return;
        }
        if (e.Args.Contains("--exit")) { Shutdown(); return; }
        exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SnapRail.ExitRequest");
        showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SnapRail.ShowRequest");
        var profileIndex = Array.IndexOf(e.Args, "--profile");
        if (profileIndex >= 0 && profileIndex + 1 < e.Args.Length)
        {
            profileRoot = Path.GetFullPath(e.Args[profileIndex + 1]); settingsStore = new SettingsStore(profileRoot);
        }
        try { Configuration = settingsStore.Load(); Store = new ImageStore(Configuration.DataRoot); }
        catch (Exception ex) { Log(ex); MessageBox.Show("设置无法读取，已保留原文件：\n" + ex.Message, "随手挂"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
            Notify("操作未完成", args.Exception.Message);
        };
        try { if(profileRoot==null) WelcomeSeed.Ensure(Store.Root); Store.Load(); RecoverRestoredImages(); } catch (Exception ex) { Log(ex); MessageBox.Show($"截图目录暂不可用，新截图将保留在内存，可另存为。\n{ex.Message}", "随手挂"); }
        rail = new RailWindow(this);
        // A transfer spool avoids carrying an encrypted library's EFS attributes across drives.
        exports = new ExportService(Path.Combine(Path.GetTempPath(), "SnapRail"));
        stateSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        stateSaveTimer.Tick += async (_, _) => { stateSaveTimer.Stop(); await SaveState(); };
        MainWindow = rail;
        hotkeys = new Hotkeys();
        hotkeys.Pressed += async id => { if (id == 1) await BeginCapture(); else if (id == 2 && !capturing) Toggle(); };
        var configured = hotkeys.Configure(Configuration.Capture, Configuration.Toggle, out var hotkeyError);
        tray = new Forms.NotifyIcon { Text = "SnapRail · 随手挂", Visible = true };
        using (var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/SnapRail-mist-blue-v1.ico")).Stream)
            tray.Icon = new System.Drawing.Icon(stream);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("截图  " + Configuration.Capture, null, async (_, _) => await BeginCapture());
        menu.Items.Add("显示 / 收起  " + Configuration.Toggle, null, (_, _) => Toggle());
        menu.Items.Add("打开截图历史", null, (_, _) => OpenHistory());
        menu.Items.Add("挂回所有贴图", null, (_, _) => ReturnAllPins());
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add("使用说明", null, (_, _) => MessageBox.Show($"{Configuration.Capture}：截图，确认后自动挂图。\n{Configuration.Toggle}：显示或收起挂图。\n单击选择窗口，拖动框选；Ctrl+A 选择鼠标所在屏幕。\n\n对象工具：拖动标注、改色、Delete 删除、双击文字修改。\n识别文字：使用本机 OCR，可修改结果后复制。\n长截图：框选滚动正文，自动滚动或手动追加，再预览编辑。\n\n单击挂图复制，拖图到应用或文件夹；点击夹子取下贴图。\n历史支持重新挂图、删除到回收站与撤销删除。\n设置可调整挂图尺寸、位置、延迟、快捷键与数据目录。\nCtrl+Z 撤销最近 100 次操作，Ctrl+Y 重做。", "SnapRail · 随手挂 " + typeof(App).Assembly.GetName().Version!.ToString(3)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出随手挂", null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left && !capturing) Toggle(); };
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
        foreach (var item in Store.Items.Where(item => item.State.IsPinned).ToArray()) Pin(item, true);
        rail.Refresh();
        rail.Reveal(holdMilliseconds: 7000);
        if (Store.Library.LoadError != null) { Log(Store.Library.LoadError); Notify("收藏和贴图状态未恢复", "状态文件未能读取，已保留原文件。截图仍可使用；详见日志。"); }
        if (!configured)
        {
            MessageBox.Show(hotkeyError + "\n可从托盘或设置更改快捷键。", "快捷键冲突");
        }
    }

    internal async Task BeginCapture()
    {
        if (capturing || exiting || rail.IsDragging || maintenance) return;
        capturing = true;
        wasVisible = rail.IsVisible;
        rail.Hide();
        editorWasVisible = imageEditor?.IsVisible == true;
        imageEditor?.Hide();
        historyWasVisible = history?.IsVisible == true; history?.Hide();
        settingsWasVisible = settingsWindow?.IsVisible == true; settingsWindow?.Hide();
        hiddenUtilities.Clear(); hiddenUtilities.AddRange(utilityWindows.Where(window => window.IsVisible));
        foreach (var window in hiddenUtilities) window.Hide();
        hiddenPins.Clear();
        hiddenPins.AddRange(pins.Values.Where(pin => pin.IsVisible));
        foreach (var pin in hiddenPins) pin.Hide();
        try
        {
            await Task.Delay(160);
            Native.DwmFlush();
            // A single physical-pixel desktop image permits selections across mixed-DPI monitors.
            var desktop = Forms.SystemInformation.VirtualScreen;
            var overlay = new CaptureWindow(desktop, CaptureWindow.Take(desktop));
                overlay.Completed += FinishCapture;
                overlay.Cancelled += CancelCapture;
                overlay.ScrollRequested += BeginScroll;
                overlay.Closed += (_, _) =>
                {
                    // Alt+F4 or a system close must not leave the capture coordinator stuck.
                    if (capturing) { overlays.Remove(overlay); CancelCapture(); }
                };
                overlays.Add(overlay);
            overlay.Show();
            overlay.Activate();
        }
        catch (Exception ex)
        {
            CancelCapture(); Log(ex); Notify("截图失败", ex.Message);
        }
    }
    private void CloseOverlays()
    {
        foreach (var window in overlays) window.Close();
        overlays.Clear();
    }
    private void CancelCapture()
    {
        capturing = false; CloseOverlays();
        if (wasVisible) rail.Reveal();
        RestoreCaptureWindows();
    }
    private void FinishCapture(CaptureResult result, Forms.Screen screen)
    {
        if (!capturing) return;
        // Render before closing overlays so failures do not destroy the user's annotations.
        var item = Store.Add(result);
        capturing = false; CloseOverlays();
        RestoreCaptureWindows();
        PublishItem(item, screen);
    }
    private void RestoreCaptureWindows()
    {
        foreach (var window in hiddenUtilities.ToArray()) if (utilityWindows.Contains(window)) window.Show();
        hiddenUtilities.Clear();
        if (editorWasVisible) imageEditor?.Show();
        if (historyWasVisible) history?.Show();
        if (settingsWasVisible) settingsWindow?.Show();
        foreach (var pin in hiddenPins) if (pins.ContainsKey(pin.Item.Id)) pin.Show();
        hiddenPins.Clear();
    }
    private void BeginScroll(System.Drawing.Rectangle region)
    {
        if (!capturing) return;
        if (region.Width < 80 || region.Height < 120) { MessageBox.Show("长截图区域至少为 80×120 像素。", "长截图"); return; }
        capturing = false; CloseOverlays(); capturing = true;
        scrollCapture = new ScrollCaptureWindow(region);
        scrollCapture.Completed += bitmap =>
        {
            var result = new CaptureResult(bitmap,new AnnotationDocument { Width=bitmap.PixelWidth,Height=bitmap.PixelHeight });
            var editor = new EditorWindow(result,true); utilityWindows.Add(editor);
            capturing = false; RestoreCaptureWindows();
            editor.Saved += capture => PublishItem(Store.Add(capture),Forms.Screen.FromRectangle(region));
            editor.Closed += (_, _) => utilityWindows.Remove(editor); editor.Show();
        };
        scrollCapture.Closed += (_, _) => { scrollCapture = null; if (capturing) CancelCapture(); };
        scrollCapture.Show();
    }
    internal void Ocr(CaptureItem item)
    {
        if (capturing || maintenance) return;
        try
        {
            var window = new OcrWindow(ImageStore.Original(item)); utilityWindows.Add(window);
            window.Closed += (_, _) => utilityWindows.Remove(window); window.Show();
        }
        catch (Exception ex) { Log(ex); Notify("无法识别文字",ex.Message); }
    }
    private async void PublishItem(CaptureItem item, Forms.Screen screen)
    {
        ShowWelcome = false;
        rail.Refresh();
        rail.Reveal(screen, 4000);
        var save = Persist(item);
        await Copy(item, false);
        await save;
        rail.PlayHangAnimation(item.Id);
    }
    internal async Task Persist(CaptureItem item)
    {
        if (item.IsSaving) return;
        item.IsSaving = true;
        item.Status = "正在重试…";
        try { await Store.Save(item); }
        catch (Exception ex)
        {
            item.Status = "保存失败 · 右键重试";
            Log(ex);
            Notify("截图尚未保存", "图片仍在内存，请右键图片重试保存或另存为。" + ex.Message);
        }
        finally { item.IsSaving = false; }
        Store.TrimLiveItems(); rail.Refresh();
        ScheduleStateSave(); history?.Refresh();
    }
    internal void ScheduleStateSave()
    {
        if (exiting || stateSaveTimer == null) return;
        stateSaveTimer.Stop(); stateSaveTimer.Start();
    }
    private async Task SaveState()
    {
        try { await Store.Library.SaveAsync(); }
        catch (Exception ex) { Log(ex); Notify("收藏或贴图状态尚未保存", "截图文件仍保留。可重试操作或检查数据目录权限。" + ex.Message); }
    }
    internal void ToggleFavorite(CaptureItem item)
    {
        SetFavorites(new[]{item},!item.State.Favorite,false);
    }
    internal void SetFavorite(CaptureItem item, bool value)
    {
        SetFavorites(new[]{item},value,false);
    }
    internal void SetFavorites(CaptureItem[] items,bool value,bool reportError=true)
    {
        if(maintenance) return;
        var selected=items.DistinctBy(item=>item.Id).ToArray();
        var count=Store.Library.Entries.Count(entry=>entry.Value.Favorite && !entry.Value.Deleted);
        var additions=selected.Count(item=>!item.State.Favorite && !item.State.Deleted);
        if(value && additions>0 && count+additions>RailPolicy.FavoriteLimit)
        {
            rail.FavoriteLimitReached();
            if(reportError) throw new IOException("最多收藏 3 张，请先取消一张收藏。");
            return;
        }
        foreach(var item in selected)
        {
            item.State.Favorite=value;
            if(value && Store.Items.All(existing=>existing.Id!=item.Id)) Store.Items.Add(item);
        }
        ScheduleStateSave();rail.Refresh();
        rail.SetStatus(value?"已固定收藏，自动排在左侧":"已取消固定收藏");
    }
    internal void Hang(CaptureItem[] items)
    {
        if (maintenance) return;
        foreach (var item in items)
        {
            if (pins.TryGetValue(item.Id, out var pin)) pin.Close();
            item.State.Hidden = false; item.State.LastHungUtcTicks = DateTime.UtcNow.Ticks;
            if (Store.Items.All(existing => existing.Id != item.Id)) Store.Items.Add(item);
        }
        ScheduleStateSave(); rail.Refresh(); rail.Reveal();
    }
    internal void HideItems(CaptureItem[] items)
    {
        if (maintenance) return;
        foreach (var item in items) item.State.Hidden = true;
        ScheduleStateSave(); rail.Refresh();
    }
    internal void RecoverRestoredImages() { new RecycleService(Store).RecoverManuallyRestored(); rail?.Refresh(); }
    internal async Task RecycleItems(CaptureItem[] items)
    {
        if (maintenance || capturing || rail.IsDragging) throw new IOException("请先完成当前操作。");
        if (imageEditor != null) throw new IOException("请先完成或关闭图片编辑。");
        maintenance = true;
        var batch = Guid.NewGuid().ToString("N"); var completed = 0;
        try
        {
            foreach (var item in items)
            {
                pins.TryGetValue(item.Id, out var pin); pin?.Hide();
                try { await new RecycleService(Store).Recycle(item, batch: batch); }
                catch { pin?.Show(); throw; }
                pin?.Close(); Store.Items.RemoveAll(existing => existing.Id == item.Id);
                completed++; await Store.Library.SaveAsync();
            }
        }
        catch (Exception ex) { throw new IOException($"已完成 {completed}/{items.Length} 张，其余未完成：{ex.Message}", ex); }
        finally { maintenance = false; rail.Refresh(); }
    }
    internal async Task UndoDelete()
    {
        if (maintenance || capturing || rail.IsDragging) throw new IOException("请先完成当前操作。");
        var latest = Store.Library.Entries.Where(pair => pair.Value.Deleted).OrderByDescending(pair => pair.Value.DeletedUtcTicks).FirstOrDefault();
        if (latest.Value == null) throw new IOException("没有可恢复的删除记录。");
        var deleted = Store.Library.Entries.Where(pair => pair.Value.Deleted && pair.Value.DeletionGroup == latest.Value.DeletionGroup).ToArray();
        maintenance = true;
        try
        {
            foreach (var entry in deleted)
            {
                var item = await new RecycleService(Store).Restore(entry.Key);
                Store.Items.RemoveAll(existing => existing.Id == item.Id); Store.Items.Add(item);
                await Store.Library.SaveAsync();
            }
        }
        finally { maintenance = false; rail.Refresh(); rail.Reveal(); }
    }
    internal int PinCount => pins.Count;
    internal void Pin(CaptureItem item, bool restoring = false)
    {
        if (capturing || exiting || maintenance) return;
        if (pins.TryGetValue(item.Id, out var existing)) { existing.Show(); existing.Activate(); return; }
        try
        {
            var pin = new PinWindow(this, item);
            if (Store.Items.All(existing => existing.Id != item.Id)) Store.Items.Add(item);
            pins.Add(item.Id, pin); item.State.IsPinned = true;
            pin.Show(); rail.Refresh(); ScheduleStateSave();
            if (!restoring) rail.SetStatus("已取下为桌面贴图，点击贴图上的“挂回”返回");
        }
        catch (Exception ex) { item.State.IsPinned = false; pins.Remove(item.Id); Log(ex); Notify("贴图未能打开", ex.Message); }
    }
    internal void PinClosed(PinWindow pin)
    {
        pins.Remove(pin.Item.Id);
        if (exiting) return;
        pin.Item.State.IsPinned = false;
        pin.Item.State.Hidden = false;
        pin.Item.State.LastHungUtcTicks = DateTime.UtcNow.Ticks;
        ScheduleStateSave(); rail.Refresh();
        if (!capturing) rail.Reveal();
    }
    internal void ReturnAllPins()
    {
        if (maintenance) return;
        foreach (var pin in pins.Values.ToArray()) pin.Close();
    }
    internal async Task DragImage(DependencyObject source, CaptureItem item, bool continuing = false)
    {
        if (capturing || exiting || (rail.IsDragging && !continuing) || maintenance) return;
        rail.StartDrag();
        var result = DragDropEffects.None;
        try
        {
            var data = await exports.Prepare(item);
            if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                result = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
        }
        catch (Exception ex) { Log(ex); Notify("拖图未完成", ex.Message); }
        finally { rail.EndDrag(); }
        rail.SetStatus(result == DragDropEffects.Copy ? "已拖出副本，原图仍在绳子上" : "拖图已取消，原图保留");
    }
    internal void ReorderRail(string[] ids)
    {
        var ticks = DateTime.UtcNow.Ticks;
        for(var index=0;index<ids.Length;index++)
        {
            var item = Store.Items.FirstOrDefault(candidate => candidate.Id==ids[index]);
            if(item != null) item.State.RailOrderUtcTicks = ticks-index;
        }
        ScheduleStateSave();
    }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (exiting) return;
        if (capturing) { if (scrollCapture != null) scrollCapture.Close(); else CancelCapture(); Notify("屏幕布局已变化", "已取消当前捕获，请按新的屏幕布局重新截图。"); }
        if (rail.IsVisible) rail.Reveal(rail.Screen);
        foreach (var pin in pins.Values) pin.EnsureVisible();
    }));
    private void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) DisplayChanged(sender,EventArgs.Empty);
        else if (e.Mode == Microsoft.Win32.PowerModes.Suspend) Dispatcher.BeginInvoke(new Action(InterruptCapture));
    }
    private void SessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) Dispatcher.BeginInvoke(new Action(InterruptCapture));
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) DisplayChanged(sender,EventArgs.Empty);
    }
    private void InterruptCapture() { if (exiting || !capturing) return; if (scrollCapture != null) scrollCapture.Close(); else CancelCapture(); }
    internal void Edit(CaptureItem item)
    {
        if (capturing || maintenance) return;
        if (imageEditor != null) { imageEditor.Show(); imageEditor.Activate(); return; }
        try
        {
            imageEditor = new EditorWindow(Store.Editable(item));
            imageEditor.Saved += result => PublishItem(Store.Add(result), Native.CursorScreen);
            imageEditor.Closed += (_, _) => { imageEditor = null; rail.Reveal(); };
            imageEditor.Show(); rail.Hide();
        }
        catch (Exception ex) { imageEditor = null; Log(ex); Notify("无法编辑图片", ex.Message); }
    }
    internal void Preview(CaptureItem item)
    {
        if (capturing || maintenance || exiting) return;
        if (imagePreview?.ItemId == item.Id) { imagePreview.Show(); imagePreview.Activate(); rail.Hide(); return; }
        try
        {
            var window = new ImagePreviewWindow(this,item,ImageStore.Original(item));
            imagePreview?.Close(); imagePreview = window; utilityWindows.Add(window);
            window.Closed += (_, _) => { utilityWindows.Remove(window); if (ReferenceEquals(imagePreview,window)) imagePreview = null; if (!capturing && !exiting) rail.Reveal(); };
            window.Show(); rail.Hide();
        }
        catch (Exception ex) { Log(ex); Notify("无法查看图片",ex.Message); }
    }
    internal async Task Copy(CaptureItem item, bool feedback = true)
    {
        BitmapSource bitmap;
        try { bitmap = ImageStore.Original(item); }
        catch (Exception ex) { Log(ex); Notify("无法读取图片", ex.Message); return; }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                ImageClipboard.Set(bitmap, new System.Windows.Interop.WindowInteropHelper(rail).EnsureHandle());
                rail.SetStatus(feedback ? "已复制，可以粘贴" : "已挂上 · 已复制");
                rail.KeepUntil = DateTime.Now.AddSeconds(3);
                return;
            }
            catch (COMException) { await Task.Delay(80 * (attempt + 1)); }
        }
        rail.SetStatus("剪贴板忙，请单击图片重试");
        Notify("复制未完成", "截图仍在绳子上，稍后单击图片重试。未改变截图文件。");
    }
    internal void SaveAs(CaptureItem item)
    {
        rail.KeepUntil = DateTime.Now.AddMinutes(5);
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"随手挂-{item.Created:yyyyMMdd-HHmmss}.png" };
        try { if (dialog.ShowDialog() == true) ImageStore.Write(ImageStore.Original(item), dialog.FileName); }
        catch (Exception ex) { Log(ex); Notify("另存为失败", ex.Message); }
        finally { rail.KeepUntil = DateTime.Now.AddSeconds(2); }
    }
    internal void OpenHistory()
    {
        if (capturing || maintenance) return;
        if (history != null) { history.Show(); history.Activate(); return; }
        history = new HistoryWindow(this); history.Closed += (_, _) => history = null;
        history.Show(); rail.Hide();
    }
    internal void OpenImageFolder()
    {
        try { Directory.CreateDirectory(Store.Images); Process.Start(new ProcessStartInfo(Store.Images) { UseShellExecute = true }); }
        catch (Exception ex) { Log(ex); Notify("无法打开历史", ex.Message); }
    }
    private void Toggle()
    {
        if (capturing || maintenance) return;
        if (rail.IsVisible) rail.Hide(); else rail.Reveal(holdMilliseconds: 4000);
    }
    private void Tick()
    {
        if (exitRequest?.WaitOne(0) == true) { ExitApp(); return; }
        if (showRequest?.WaitOne(0) == true && !capturing && !exiting) rail.Reveal(holdMilliseconds: 5000);
        if (capturing || exiting) return;
        var cursor = Forms.Cursor.Position;
        var screen = Native.CursorScreen;
        var area = screen.Bounds;
        var inTrigger = cursor.Y >= area.Top && cursor.Y <= area.Top + 3 && Math.Abs(cursor.X - (area.Left + area.Width / 2)) < Math.Min(350, area.Width / 3);
        if (inTrigger && Configuration.HoverEnabled)
        {
            hoverSince ??= DateTime.Now;
            var target = RailTarget(screen);
            if ((DateTime.Now - hoverSince.Value).TotalMilliseconds >= Configuration.HoverDelayMs && (!rail.IsVisible || rail.Screen.DeviceName != target.DeviceName)) rail.Reveal(screen);
        }
        else hoverSince = null;
        if (rail.IsVisible && !rail.IsDragging && !rail.PhysicalBounds.Contains(cursor) && !rail.IsMouseOver && !rail.MenuOpen && !inTrigger && DateTime.Now > rail.KeepUntil) rail.Hide();
    }
    internal Forms.Screen RailTarget(Forms.Screen requested)
        => Configuration.RailScreen == "cursor" ? requested : Forms.Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == Configuration.RailScreen) ?? Forms.Screen.PrimaryScreen!;
    internal void OpenSettings()
    {
        if (capturing || maintenance) return;
        if (settingsWindow != null) { settingsWindow.Show(); settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(this); settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show(); rail.Hide();
    }
    internal async Task ApplySettings(AppSettings candidate)
    {
        candidate.Validate();
        if (maintenance || capturing || rail.IsDragging || imageEditor != null || utilityWindows.OfType<EditorWindow>().Any() || Store.Items.Any(item => item.PendingImage != null))
            throw new IOException("请先完成截图、保存与图片编辑，再应用设置。");
        maintenance = true;
        var oldRun = Autostart.Current(); var changedRun = false; var savedConfiguration = false;
        var visiblePins = pins.Values.Where(pin => pin.IsVisible).ToArray();
        try
        {
            if (!hotkeys!.Configure(candidate.Capture, candidate.Toggle, out var error)) throw new IOException(error);
            rail.Hide(); if (history != null) history.IsEnabled = false;
            foreach (var pin in visiblePins) { pin.Item.State.Placement = PinPlacement.From(Native.Bounds(pin)); pin.Hide(); }
            await Store.Library.SaveAsync();
            await DataMigration.Copy(Store.Root, candidate.DataRoot);
            Directory.CreateDirectory(candidate.DataRoot);
            var probe = Path.Combine(candidate.DataRoot, "SnapRail-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(probe, "test"); File.Delete(probe);
            Autostart.Set(candidate.StartWithWindows ? Autostart.Command(profileRoot) : null); changedRun = true;
            settingsStore.Save(candidate); savedConfiguration = true;
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SnapRail.dll"));
            if (profileRoot != null) { start.ArgumentList.Add("--profile"); start.ArgumentList.Add(profileRoot); }
            start.ArgumentList.Add("--restart-after"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(start);
            exiting = true; stateSaveTimer?.Stop(); Shutdown();
        }
        catch
        {
            hotkeys!.Configure(Configuration.Capture, Configuration.Toggle, out _);
            if (changedRun) Autostart.Set(oldRun);
            if (savedConfiguration) settingsStore.Save(Configuration);
            throw;
        }
        finally
        {
            maintenance = false;
            if (!exiting) { foreach (var pin in visiblePins) pin.Show(); if (history != null) history.IsEnabled = true; }
        }
    }
    private async void ExitApp()
    {
        if (exiting || rail.IsDragging || capturing || maintenance) return;
        if (imageEditor != null) { imageEditor.Close(); if (imageEditor != null) return; }
        foreach (var editor in utilityWindows.OfType<EditorWindow>().ToArray()) { editor.Close(); if (utilityWindows.Contains(editor)) return; }
        if (Store.Items.Any(item => item.PendingImage != null) && MessageBox.Show("有截图尚未保存，退出会丢失这些图片。\n选择“否”返回，右键图片重试保存或另存为。\n仍要退出吗？", "尚未保存的截图", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        exiting = true;
        stateSaveTimer?.Stop();
        try { await Store.Library.SaveAsync(); }
        catch (Exception ex)
        {
            Log(ex);
            if (MessageBox.Show("收藏和贴图状态未能保存，截图文件仍保留。仍要退出吗？", "状态未保存", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            { exiting = false; return; }
        }
        Shutdown();
    }
    private void Notify(string title, string text)
    {
        tray?.ShowBalloonTip(4000, title, text.Length > 220 ? text[..220] : text, Forms.ToolTipIcon.Info);
    }
    internal static void Log(Exception ex)
    {
        try
        {
            var root = Path.Combine(SettingsStore.DefaultRoot, "logs");
            Directory.CreateDirectory(root);
            lock (logLock)
            {
                var path = Path.Combine(root,"app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2*1024*1024) File.Move(path,path+".1",true);
                File.AppendAllText(path, $"{DateTime.Now:O} {ex}\n");
            }
        }
        catch { /* Logging must never prevent capture recovery. */ }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true; stateSaveTimer?.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch -= SessionChanged;
        timer?.Stop(); hotkeys?.Dispose(); tray?.Icon?.Dispose(); tray?.Dispose(); instance?.Dispose();
        exitRequest?.Dispose(); showRequest?.Dispose();
        base.OnExit(e);
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        exiting = true;
        try { Store.Library.SaveAsync().GetAwaiter().GetResult(); } catch (Exception ex) { Log(ex); }
        base.OnSessionEnding(e);
    }
}
