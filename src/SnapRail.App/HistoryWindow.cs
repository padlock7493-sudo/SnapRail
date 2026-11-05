using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SnapRail;

internal sealed class HistoryRow
{
    internal CaptureItem Item { get; }
    internal HistoryRow(CaptureItem item) => Item = item;
    public BitmapSource Thumbnail => Item.Thumbnail;
    public string Title => Item.Created.ToString("yyyy-MM-dd HH:mm:ss");
    public string Detail => (Item.State.Favorite ? "收藏 · " : "") + (Item.State.IsPinned ? "桌面贴图" : Item.State.Hidden ? "已取下" : "本地截图") + " · " + System.IO.Path.GetFileName(Item.Path);
    public string AutomationId => "history-" + Item.Id;
}

internal sealed class HistoryWindow : Window
{
    private readonly App app;
    private readonly TextBox search = new() { MinWidth = 260, Margin = new Thickness(4), Padding = new Thickness(8), ToolTip = "搜索日期、时间或文件名" };
    private string selectedFilter = "全部";
    private readonly ListBox list = new() { SelectionMode = SelectionMode.Extended, Margin = new Thickness(12), BorderThickness = new Thickness(0) };
    private readonly TextBlock summary = new() { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { Margin = new Thickness(14, 4, 14, 12), TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel actions = new() { Margin = new Thickness(8) };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private int page;
    private int pages = 1;
    private bool busy;
    internal HistoryWindow(App app)
    {
        this.app = app; Title = "随手挂 · 历史"; Width = 1000; Height = 760; MinWidth = 650; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = RailWindow.Brush("#F5FAF8"); FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new DockPanel();
        var header = new WrapPanel { Margin = new Thickness(10) };
        header.Children.Add(new TextBlock { Text = "截图历史", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 6, 14, 0) });
        System.Windows.Automation.AutomationProperties.SetAutomationId(search, "history-search"); header.Children.Add(search);
        var filters=new StackPanel { Orientation=Orientation.Horizontal };
        foreach(var label in new[]{"全部","收藏","挂图","待挂"})
        {
            var option=new RadioButton { Content=label,GroupName="history-filter",IsChecked=label==selectedFilter,Style=(Style)FindResource("HistoryFilterStyle") };
            System.Windows.Automation.AutomationProperties.SetAutomationId(option,"history-filter-"+label);
            option.Checked+=(_,_)=> { selectedFilter=label;page=0;Refresh(); };
            filters.Children.Add(option);
        }
        header.Children.Add(new Border { Child=filters,Background=RailWindow.Brush("#EAF2EE"),BorderBrush=RailWindow.Brush("#D0DEDA"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(3),Margin=new Thickness(6,4,8,4),VerticalAlignment=VerticalAlignment.Center });
        var refresh = Button("刷新", "history-refresh", () => Refresh()); header.Children.Add(refresh);
        header.Children.Add(Button("文件夹", "history-folder", app.OpenImageFolder));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        AddAction("重新挂图", "history-hang", async () => { app.Hang(Selected()); await Task.CompletedTask; });
        AddAction("复制", "history-copy", async () => { if (Selected().Length == 1) await app.Copy(Selected()[0]); });
        AddAction("另存为", "history-save-as", async () => { if (Selected().Length == 1) app.SaveAs(Selected()[0]); await Task.CompletedTask; });
        AddAction("贴到桌面", "history-pin", async () => { foreach (var item in Selected()) app.Pin(item); await Task.CompletedTask; });
        AddAction("固定/取消收藏", "history-favorite", async () => { var selected = Selected(); var value = !selected.All(item => item.State.Favorite); app.SetFavorites(selected,value); await Task.CompletedTask; });
        AddAction("从绳子取下", "history-hide", async () => { app.HideItems(Selected()); await Task.CompletedTask; });
        AddAction("编辑副本", "history-edit", async () => { if (Selected().Length == 1) app.Edit(Selected()[0]); await Task.CompletedTask; });
        AddAction("识别文字", "history-ocr", async () => { if (Selected().Length == 1) app.Ocr(Selected()[0]); await Task.CompletedTask; });
        AddAction("删除到回收站", "history-delete", async () =>
        {
            var selected = Selected(); if (selected.Length == 0) return;
            var favorites = selected.Count(item => item.State.Favorite);
            if (MessageBox.Show(this, $"将 {selected.Length} 张图片及其标注移到系统回收站？" + (favorites > 0 ? $"\n其中含 {favorites} 张收藏。" : "") + "\n可通过“撤销上次删除”恢复。", "删除截图", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            await app.RecycleItems(selected);
        });
        AddAction("撤销上次删除", "history-undo-delete", app.UndoDelete);
        DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "history-status");
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        footer.Children.Add(Button("上一页", "history-prev", () => { if (page > 0) { page--; Refresh(); } }));
        footer.Children.Add(summary);
        footer.Children.Add(Button("下一页", "history-next", () => { if (page + 1 < pages) { page++; Refresh(); } }));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        list.ItemTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Orientation='Horizontal' Margin='6'><Image Source='{Binding Thumbnail}' Width='130' Height='80' Stretch='Uniform'/><StackPanel Margin='16,12,8,0'><TextBlock Text='{Binding Title}' FontSize='16'/><TextBlock Text='{Binding Detail}' Margin='0,8,0,0' Width='620' TextTrimming='CharacterEllipsis'/></StackPanel></StackPanel></DataTemplate>");
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.AutomationIdProperty, new Binding("AutomationId"))); list.ItemContainerStyle = style;
        root.Children.Add(list); Content = root;
        search.TextChanged += (_, _) => { page = 0; searchTimer.Stop(); searchTimer.Start(); };
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); Refresh(); };
        Loaded += (_, _) => Refresh(); Closed += (_, _) => searchTimer.Stop();
    }
    private CaptureItem[] Selected() => list.SelectedItems.Cast<HistoryRow>().Select(row => row.Item).ToArray();
    private static Button Button(string name, string id, Action action)
    {
        var button = new Button { Content = name, Margin = new Thickness(4) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, id); button.Click += (_, _) => action(); return button;
    }
    private void AddAction(string name, string id, Func<Task> action)
    {
        var button = Button(name, id, async () =>
        {
            if (busy) return;
            if (id != "history-undo-delete" && Selected().Length == 0) { status.Text = "请先选择图片"; return; }
            busy = true; actions.IsEnabled = false;
            try { await action(); status.Text = "操作已完成"; }
            catch (Exception ex) { App.Log(ex); status.Text = "操作未完成：" + ex.Message; }
            finally { busy = false; actions.IsEnabled = true; Refresh(); }
        });
        actions.Children.Add(button);
    }
    internal void Refresh()
    {
        if (!IsLoaded || busy) return;
        try
        {
            app.RecoverRestoredImages();
            var data = app.Store.History(search.Text, selectedFilter, page);
            page = data.Page; pages = data.Pages; list.ItemsSource = data.Items.Select(item => new HistoryRow(item)).ToArray();
            summary.Text = $"第 {page + 1}/{pages} 页 · 共 {data.Total} 张";
            System.Windows.Automation.AutomationProperties.SetAutomationId(summary, "history-count");
        }
        catch (Exception ex) { App.Log(ex); status.Text = ex.Message; }
    }
}
