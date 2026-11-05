using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace SnapRail;

internal sealed class SettingsWindow : Window
{
    private readonly App app;
    private readonly AppSettings draft;
    private readonly TextBox directory;
    private readonly CheckBox hover;
    private readonly CheckBox animation;
    private readonly CheckBox startup;
    private readonly ComboBox screen;
    private readonly TextBlock status;
    private readonly Slider photoWidth;
    private readonly Slider railTop;
    private readonly Slider hoverDelay;
    private readonly Slider hideDelay;
    private HotkeyRecorder? recorder;
    private readonly Dictionary<TextBox, Action<HotkeySpec>> keyInputs = new();
    internal SettingsWindow(App app)
    {
        this.app = app; draft = app.Configuration.Copy();
        Title = "随手挂 · 设置"; Width = 920; Height = 660; MinWidth = 860; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = RailWindow.Brush("#F5FAF8"); FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        var root=new DockPanel { Margin=new Thickness(24) };
        var title=new TextBlock { Text="随手挂设置",FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,18) };
        DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var columns=new Grid();columns.ColumnDefinitions.Add(new ColumnDefinition());columns.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(28) });columns.ColumnDefinitions.Add(new ColumnDefinition());
        var stack=new StackPanel();var adjustments=new StackPanel();Grid.SetColumn(adjustments,2);columns.Children.Add(stack);columns.Children.Add(adjustments);root.Children.Add(columns);
        stack.Children.Add(new TextBlock { Text="快捷键与保存",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
        adjustments.Children.Add(new TextBlock { Text="挂图与交互",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
        Label(stack, "截图快捷键（点击后按下组合键）"); stack.Children.Add(KeyBox(draft.Capture, "settings-capture", key => draft.Capture = key));
        Label(stack, "显示 / 收起挂图快捷键"); stack.Children.Add(KeyBox(draft.Toggle, "settings-toggle", key => draft.Toggle = key));
        Label(stack, "截图与标注保存目录");
        var folder = new DockPanel(); var browse = new Button { Content = "选择…", Margin = new Thickness(6,0,0,0) };
        DockPanel.SetDock(browse, Dock.Right); folder.Children.Add(browse);
        directory = new TextBox { Text = draft.DataRoot, Padding = new Thickness(8) }; System.Windows.Automation.AutomationProperties.SetAutomationId(directory, "settings-directory"); folder.Children.Add(directory); stack.Children.Add(folder);
        if(app.IsPortable) { directory.IsReadOnly=true; browse.IsEnabled=false; directory.ToolTip="便携模式：资料固定保存在程序文件夹，移动时请复制整个文件夹。"; }
        browse.Click += (_, _) => { using var dialog = new Forms.FolderBrowserDialog { Description = "选择空目录。原目录会保留，图片与标注将复制并校验。", UseDescriptionForTitle = true }; if (dialog.ShowDialog() == Forms.DialogResult.OK) directory.Text = dialog.SelectedPath; };
        Label(stack, "挂图显示屏幕");
        var screens = new List<ScreenChoice> { new("cursor", "跟随鼠标 / 截图所在屏幕") };
        foreach (var monitor in Forms.Screen.AllScreens) screens.Add(new ScreenChoice(monitor.DeviceName, $"{(monitor.Primary ? "主屏" : "副屏")} {monitor.DeviceName} · {monitor.Bounds.Width}×{monitor.Bounds.Height}"));
        screen = new ComboBox { ItemsSource = screens, DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = draft.RailScreen, Padding = new Thickness(6) }; stack.Children.Add(screen);
        System.Windows.Automation.AutomationProperties.SetAutomationId(screen, "settings-screen");
        hover = Check(adjustments, "鼠标移到屏幕顶部时展开", draft.HoverEnabled, "settings-hover");
        animation = Check(adjustments, "展开挂图栏、新截图挂上时轻摆", draft.AnimationEnabled, "settings-animation");
        startup = Check(stack, "登录 Windows 后启动随手挂", draft.StartWithWindows, "settings-startup");
        photoWidth = SliderSetting(adjustments,"挂图尺寸",150,240,draft.PhotoWidth,"settings-photo-width","px");
        railTop = SliderSetting(adjustments,"挂图栏距工作区顶部",0,100,draft.RailTopMargin,"settings-top","px");
        hoverDelay = SliderSetting(adjustments,"顶部悬停多久展开",100,1500,draft.HoverDelayMs,"settings-hover-delay","ms");
        hideDelay = SliderSetting(adjustments,"离开多久收起",300,5000,draft.HideDelayMs,"settings-hide-delay","ms");
        status = new TextBlock { Text = app.IsPortable?"便携模式：资料随程序文件夹保存。调整设置后，保存并重启生效。":"更换目录会复制并校验图片、标注及状态，保留原目录；保存后重启生效。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,16,0,12), Foreground = RailWindow.Brush("#53686B") }; System.Windows.Automation.AutomationProperties.SetAutomationId(status,"settings-status"); footer.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Margin = new Thickness(4) }; cancel.Click += (_, _) => Close(); actions.Children.Add(cancel);
        var save = new Button { Content = "保存并重启", Margin = new Thickness(4), Background = RailWindow.Brush("#CDE8DE") }; System.Windows.Automation.AutomationProperties.SetAutomationId(save,"settings-save");
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false; cancel.IsEnabled = false;
            try
            {
                draft.DataRoot = directory.Text; draft.RailScreen = screen.SelectedValue?.ToString() ?? "cursor";
                draft.HoverEnabled = hover.IsChecked == true; draft.AnimationEnabled = animation.IsChecked == true; draft.StartWithWindows = startup.IsChecked == true;
                draft.PhotoWidth = (int)photoWidth.Value; draft.RailTopMargin = (int)railTop.Value; draft.HoverDelayMs = (int)hoverDelay.Value; draft.HideDelayMs = (int)hideDelay.Value;
                status.Text = "正在保存设置；更换目录时将复制并校验文件…";
                await app.ApplySettings(draft);
            }
            catch (Exception ex) { App.Log(ex); status.Text = "未应用：" + ex.Message; }
            finally { save.IsEnabled = true; cancel.IsEnabled = true; }
        }; actions.Children.Add(save); footer.Children.Add(actions);
        Content = root;
        Deactivated += (_, _) => StopRecording(); Closed += (_, _) => StopRecording();
        Activated += (_, _) => { foreach (var pair in keyInputs) if (pair.Key.IsKeyboardFocusWithin) StartRecording(pair.Key, pair.Value); };
    }
    private static void Label(StackPanel stack, string text) => stack.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0,8,0,5) });
    private static Slider SliderSetting(StackPanel stack,string label,int minimum,int maximum,int value,string id,string unit)
    {
        var caption = new TextBlock { Text = $"{label} · {value} {unit}", Margin = new Thickness(0,14,0,6) }; stack.Children.Add(caption);
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1, LargeChange = 10 };
        slider.Style=(Style)Application.Current.FindResource("SettingsSliderStyle");
        System.Windows.Automation.AutomationProperties.SetAutomationId(slider,id); System.Windows.Automation.AutomationProperties.SetName(slider,label);
        slider.ValueChanged += (_, _) => caption.Text = $"{label} · {(int)slider.Value} {unit}"; stack.Children.Add(slider); return slider;
    }
    private static CheckBox Check(StackPanel stack, string text, bool value, string id)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0,12,0,0) }; System.Windows.Automation.AutomationProperties.SetAutomationId(box,id); stack.Children.Add(box); return box;
    }
    private TextBox KeyBox(HotkeySpec value, string id, Action<HotkeySpec> change)
    {
        var box = new TextBox { Text = value.ToString(), IsReadOnly = true, Padding = new Thickness(8) }; System.Windows.Automation.AutomationProperties.SetAutomationId(box,id);
        keyInputs.Add(box, change);
        box.GotKeyboardFocus += (_, _) => StartRecording(box, change);
        box.LostKeyboardFocus += (_, _) => StopRecording();
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift)) return;
            if (key is Key.Escape or Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { StopRecording(); box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var modifiers = Keyboard.Modifiers;
            var spec = new HotkeySpec { Key = (uint)KeyInterop.VirtualKeyFromKey(key), Modifiers = (uint)((modifiers.HasFlag(ModifierKeys.Alt) ? 1 : 0) | (modifiers.HasFlag(ModifierKeys.Control) ? 2 : 0) | (modifiers.HasFlag(ModifierKeys.Shift) ? 4 : 0) | (modifiers.HasFlag(ModifierKeys.Windows) ? 8 : 0)) };
            try { spec.Validate(); change(spec); box.Text = spec.ToString(); } catch { }
            e.Handled = true;
        }; return box;
    }
    private void StopRecording() { recorder?.Dispose(); recorder = null; }
    private void StartRecording(TextBox box, Action<HotkeySpec> change)
    {
        StopRecording();
        try
        {
            recorder = new HotkeyRecorder(spec =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!box.IsKeyboardFocusWithin || !IsActive) return;
                    try { spec.Validate(); change(spec); box.Text = spec.ToString(); }
                    catch (Exception ex) { status.Text = ex.Message; }
                }));
            });
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private sealed record ScreenChoice(string Id, string Label);
}
