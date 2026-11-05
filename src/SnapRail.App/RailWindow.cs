using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using System.Threading.Tasks;
using System.Windows.Media.Effects;

namespace SnapRail;

internal sealed class RailWindow : Window
{
    private readonly App app;
    private readonly Canvas photos = new() { Margin = new Thickness(24, 0, 24, 12), ClipToBounds = true };
    private readonly TextBlock status = new() { Foreground = Brush("#53686B"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 12, 0) };
    internal Forms.Screen Screen { get; private set; } = Native.CursorScreen;
    internal DateTime KeepUntil { get; set; }
    internal bool MenuOpen { get; private set; }
    internal System.Drawing.Rectangle PhysicalBounds { get; private set; }
    internal bool IsDragging { get; private set; }
    private bool refreshPending;
    private DateTime expandAnimationUntil;
    private int expandAnimationVersion;
    private FrameworkElement? movingCard;
    private Button? movingButton;
    private System.Collections.Generic.List<string>? movingOrder;
    private string[] originalOrder = Array.Empty<string>();
    internal void StartDrag() { IsDragging = true; }
    internal void EndDrag() { IsDragging = false; KeepUntil = DateTime.Now.AddSeconds(2); if (refreshPending) { refreshPending = false; Refresh(); } }

    internal RailWindow(App app)
    {
        this.app = app;
        Title = "SnapRail · 随手挂";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = 1040; Height = 300;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.RowDefinitions.Add(new RowDefinition());
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = "随手挂", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brush("#253A40"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        header.Children.Add(status);
        var capture = new Button { Content = "截图  " + app.Configuration.Capture, Margin = new Thickness(0, 0, 8, 0), Background = Brush("#D7EAE2") };
        capture.Click += async (_, _) => await app.BeginCapture();
        header.Children.Add(capture);
        var folder = new Button { Content = "历史", Margin = new Thickness(0, 0, 8, 0), ToolTip = "浏览、挂回或管理历史截图" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(folder, "open-history");
        folder.Click += (_, _) => app.OpenHistory();
        header.Children.Add(folder);
        var settings = new Button { Content = "设置", Margin = new Thickness(0,0,8,0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(settings, "open-settings");
        settings.Click += (_, _) => app.OpenSettings(); header.Children.Add(settings);
        var hide = new Button { Content = "收起", ToolTip = app.Configuration.Toggle + " 显示或收起" };
        hide.Click += (_, _) => { KeepUntil = DateTime.MinValue; Hide(); };
        header.Children.Add(hide);
        var pill = new Border { Child = header, Background = Brush("#FAFDFC"), BorderBrush = Brush("#CDDDD7"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 8, 8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        root.Children.Add(pill);
        var body = new Grid();
        Grid.SetRow(body, 1);
        body.Children.Add(new Cord());
        body.Children.Add(photos);
        photos.SizeChanged += (_, _) => Refresh();
        root.Children.Add(body);
        Content = root;
        MouseEnter += (_, _) => KeepUntil = DateTime.Now.AddMilliseconds(app.Configuration.HideDelayMs);
        MouseLeave += (_, _) => KeepUntil = DateTime.Now.AddMilliseconds(app.Configuration.HideDelayMs);
        Loaded += (_, _) => Position();
        PreviewKeyDown += (_, e) => { if(e.Key==Key.Escape && movingCard!=null) { FinishMove(false); e.Handled=true; } };
        Refresh();
    }

    internal void Reveal(Forms.Screen? screen = null, int holdMilliseconds = 2500)
    {
        var requested = app.RailTarget(screen ?? Native.CursorScreen);
        var opening=!IsVisible || Screen.DeviceName!=requested.DeviceName;
        if(opening) expandAnimationUntil=DateTime.Now.AddMilliseconds(1600);
        Screen = Forms.Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == requested.DeviceName) ?? Forms.Screen.PrimaryScreen!;
        KeepUntil = DateTime.Now.AddMilliseconds(holdMilliseconds);
        Show();
        Position();
        if(opening) QueueExpandAnimation();
    }
    private void Position()
    {
        var bounds = Screen.WorkingArea;
        var dpi = Native.Dpi(Screen);
        var width = Math.Max(1, bounds.Width - 32);
        var height = 160 + app.Configuration.PhotoWidth * 214d / 196;
        PhysicalBounds = new System.Drawing.Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (int)(app.Configuration.RailTopMargin * dpi), width, Math.Min(bounds.Height,(int)(height * dpi)));
        Native.Place(this, PhysicalBounds);
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (IsVisible) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(Position));
    }

    internal void SetStatus(string text) => status.Text = text;
    internal void FavoriteLimitReached()
    {
        Reveal(holdMilliseconds:5000);
        status.Text="最多收藏 3 张，请先取消一张";
        var shake=new TranslateTransform();status.RenderTransform=shake;
        var motion=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromMilliseconds(430),FillBehavior=FillBehavior.Stop };
        foreach(var frame in new[]{(0d,0d),(.15,-6d),(.3,6d),(.48,-4d),(.65,4d),(.82,-2d),(1d,0d)})
            motion.KeyFrames.Add(new EasingDoubleKeyFrame(frame.Item2,KeyTime.FromPercent(frame.Item1)));
        shake.BeginAnimation(TranslateTransform.XProperty,motion);
        foreach(var card in photos.Children.OfType<FrameworkElement>().Where(child=>child.Tag is string id && app.Store.Items.Any(item=>item.Id==id && item.State.Favorite)))
        {
            var rotation=(RotateTransform)((TransformGroup)card.RenderTransform).Children[0];
            var rest=(double)rotation.GetAnimationBaseValue(RotateTransform.AngleProperty);
            var wobble=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromMilliseconds(430),FillBehavior=FillBehavior.Stop };
            foreach(var frame in new[]{(0d,rest),(.2,rest-4),(.45,rest+4),(.7,rest-2),(1d,rest)})
                wobble.KeyFrames.Add(new EasingDoubleKeyFrame(frame.Item2,KeyTime.FromPercent(frame.Item1)));
            rotation.BeginAnimation(RotateTransform.AngleProperty,wobble);
        }
    }
    internal void Refresh(bool animate = false)
    {
        if (IsDragging) { refreshPending = true; return; }
        photos.Children.Clear();
        var visible = RailPolicy.Visible(app.Store.Items);
        if(app.ShowWelcome || (visible.Length>0 && visible.All(WelcomeSeed.IsWelcome)))
        {
            status.Text="截一张，随手挂一下！";
            WelcomeCards();QueueExpandAnimation();return;
        }
        status.Text = visible.Length == 0 ? "截一张，随手挂起来" : $"挂图 {visible.Length} · 贴图 {app.PinCount}";
        if (visible.Length == 0)
        {
            var empty = new Button { Content = app.PinCount > 0 ? "图片正在桌面上\n\n点击挂回所有贴图" : "第一张截图，从这里开始\n\n" + app.Configuration.Capture, Width = 270, Height = 142, Margin = new Thickness(20, 38, 0, 0), Background = Brush("#F5FAF8") };
            empty.Click += async (_, _) => { if (app.PinCount > 0) app.ReturnAllPins(); else await app.BeginCapture(); };
            photos.Children.Add(empty);
            return;
        }
        var capacity = Math.Max(1,(int)(Math.Max(1,photos.ActualWidth)/(app.Configuration.PhotoWidth+24)));
        var count = visible.Length > capacity ? Math.Max(0,capacity-1) : visible.Length;
        var slots = count+(visible.Length>count?1:0);
        var index = 0;
        foreach (var item in visible.Take(count))
        {
            var card = Card(item, index++);
            card.Tag = item.Id;
            photos.Children.Add(card);
            PlaceCard(card,index-1,slots);
        }
        if (visible.Length>count)
        {
            var more = new Button { Content = "＋", Width = 56, Height = 56, FontSize = 30, Background = Brush("#E4F1E9"), ToolTip = "查看其余图片" };
            more.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='#77958B' BorderThickness='2' CornerRadius='28'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
            System.Windows.Automation.AutomationProperties.SetAutomationId(more,"rail-more");
            more.Click += (_, _) => app.OpenHistory(); photos.Children.Add(more); PlaceCard(more,count,slots);
            Canvas.SetLeft(more,photos.ActualWidth*(count+.5)/slots-28);
        }
        QueueExpandAnimation();
    }
    private void QueueExpandAnimation()
    {
        if(DateTime.Now>=expandAnimationUntil || !app.Configuration.AnimationEnabled || !SystemParameters.ClientAreaAnimation) return;
        var version=++expandAnimationVersion;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(() =>
        {
            if(version!=expandAnimationVersion || !IsVisible || IsDragging) return;
            var index=0;
            foreach(var card in photos.Children.OfType<FrameworkElement>().Where(child=>child.Tag is string || child is WelcomeCard))
                Swing(card,index++);
        }));
    }
    private static void Swing(FrameworkElement card,int index)
    {
        var rotation=card.RenderTransform is TransformGroup group?(RotateTransform)group.Children[0]:card.RenderTransform as RotateTransform;
        if(rotation==null) return;
        var rest=rotation.GetAnimationBaseValue(RotateTransform.AngleProperty) is double angle?angle:0;
        var direction=index%2==0?1:-1;
        var swing=new DoubleAnimationUsingKeyFrames { BeginTime=TimeSpan.FromMilliseconds(index*35),Duration=TimeSpan.FromMilliseconds(1150),FillBehavior=FillBehavior.Stop };
        foreach(var frame in new[]{(0d,rest),(.15,-10d*direction),(.4,7d*direction),(.65,-4d*direction),(.83,2d*direction),(1d,rest)})
            swing.KeyFrames.Add(new EasingDoubleKeyFrame(frame.Item2,KeyTime.FromPercent(frame.Item1),new SineEase { EasingMode=EasingMode.EaseInOut }));
        rotation.BeginAnimation(RotateTransform.AngleProperty,swing);
    }
    internal void PlayHangAnimation(string id)
    {
        if(DateTime.Now<expandAnimationUntil) { QueueExpandAnimation();return; }
        if(!app.Configuration.AnimationEnabled || !SystemParameters.ClientAreaAnimation) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(() =>
        {
            var card=photos.Children.OfType<FrameworkElement>().FirstOrDefault(child=>child.Tag is string value && value==id);
            if(card==null || IsDragging || !IsVisible) return;
            var rotation=(RotateTransform)((TransformGroup)card.RenderTransform).Children[0];
            var rest=rotation.Angle;
            var swing=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromMilliseconds(1150),FillBehavior=FillBehavior.Stop };
            foreach(var frame in new[]{(0d,rest),(.15,-12d),(.4,9d),(.65,-5d),(.83,2.5),(1d,rest)})
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(frame.Item2,KeyTime.FromPercent(frame.Item1),new SineEase { EasingMode=EasingMode.EaseInOut }));
            rotation.BeginAnimation(RotateTransform.AngleProperty,swing);
        }));
    }
    private void PlaceCard(FrameworkElement card,int index,int count)
    {
        var center = photos.ActualWidth*(index+.5)/Math.Max(1,count);
        var t = (center+24)/Math.Max(1,photos.ActualWidth+48);
        var scale = app.Configuration.PhotoWidth/196d;
        Canvas.SetLeft(card,center-(app.Configuration.PhotoWidth+24)/2);
        Canvas.SetTop(card,19+264*t*(1-t)-20*scale);
    }
    private void WelcomeCards()
    {
        var letters="截一张，随手挂一下！";
        var colors=new[]{"#D96563","#CE9438","#4B95B0","#9974B3","#559C79","#DA778E","#6E83C1","#D48A53","#A279BD","#DD7961"};
        var available=Math.Max(1,photos.ActualWidth);
        var width=Math.Max(20,Math.Min(app.Configuration.PhotoWidth,available/letters.Length-10));
        var scale=width/196;
        for(var i=0;i<letters.Length;i++)
        {
            var card=new WelcomeCard { Width=196,Height=206,LayoutTransform=new ScaleTransform(scale,scale),RenderTransformOrigin=new Point(.5,.08),RenderTransform=new RotateTransform(i%2==0?-2:2) };
            var panel=new Border { Background=Brush("#FFF8E7"),BorderBrush=Brush("#A6BFAF"),BorderThickness=new Thickness(2),CornerRadius=new CornerRadius(13),Margin=new Thickness(0,27,0,0),Height=158,VerticalAlignment=VerticalAlignment.Top,
                Child=new WelcomeLetter(letters[i].ToString(),colors[i]) { Margin=new Thickness(14) } };
            card.Children.Add(panel);
            var peg=new Button { Width=28,Height=44,MinHeight=0,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,ToolTip="单击开始截图" };
            var template=new ControlTemplate(typeof(Button));template.VisualTree=new FrameworkElementFactory(typeof(ContentPresenter));peg.Template=template;
            peg.Content=new CartoonPeg(i%2==0?"#A9D8C6":"#F2B5A7") { Width=28,Height=44 };
            peg.Click+=async (_,_)=>await app.BeginCapture();card.Children.Add(peg);
            System.Windows.Automation.AutomationProperties.SetAutomationId(card,"welcome-letter-"+i);
            System.Windows.Automation.AutomationProperties.SetName(card,letters[i].ToString());
            photos.Children.Add(card);
            var center=available*(i+.5)/letters.Length;var t=(center+24)/(available+48);
            Canvas.SetLeft(card,center-width/2);Canvas.SetTop(card,19+264*t*(1-t)-20*scale);
        }
    }
    private void Lift(FrameworkElement? selected)
    {
        foreach(var card in photos.Children.OfType<FrameworkElement>().Where(child=>child.Tag is string))
        {
            var transforms=(TransformGroup)card.RenderTransform;
            var scale=(ScaleTransform)transforms.Children[1];var offset=(TranslateTransform)transforms.Children[2];
            var active=card==selected;var size=selected==null?1:active?1.08:.92;
            Animate(scale,ScaleTransform.ScaleXProperty,size);Animate(scale,ScaleTransform.ScaleYProperty,size);
            Animate(offset,TranslateTransform.YProperty,selected==null?0:active?-16:12);
            Animate(card,OpacityProperty,selected==null||active?1:.78);
            Animate((DropShadowEffect)card.Effect,DropShadowEffect.OpacityProperty,active?.24:0);
            Panel.SetZIndex(card,active?10:0);
        }
    }
    private static void Animate(Animatable target,DependencyProperty property,double value)
        => target.BeginAnimation(property,new DoubleAnimation(value,TimeSpan.FromMilliseconds(190)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
    private static void Animate(FrameworkElement target,DependencyProperty property,double value)
        => target.BeginAnimation(property,new DoubleAnimation(value,TimeSpan.FromMilliseconds(190)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
    private void MoveAlongRope(Point point)
    {
        if(movingCard==null || movingOrder==null) return;
        var center = Math.Clamp(point.X,app.Configuration.PhotoWidth/2d,Math.Max(app.Configuration.PhotoWidth/2d,photos.ActualWidth-app.Configuration.PhotoWidth/2d));
        var t = (center+24)/Math.Max(1,photos.ActualWidth+48);
        Canvas.SetLeft(movingCard,center-(app.Configuration.PhotoWidth+24)/2);
        Canvas.SetTop(movingCard,19+264*t*(1-t)-20*app.Configuration.PhotoWidth/196d);
        var slots=photos.Children.Count;
        var destination=Math.Clamp((int)(center/Math.Max(1,photos.ActualWidth)*slots),0,movingOrder.Count-1);
        var id=(string)movingCard.Tag;
        // Keep the existing favorite section at the left.
        var favorites=RailPolicy.Visible(app.Store.Items).Take(movingOrder.Count).Count(item=>item.State.Favorite);
        var favorite=app.Store.Items.First(item=>item.Id==id).State.Favorite;
        destination=favorite?Math.Clamp(destination,0,Math.Max(0,favorites-1)):Math.Clamp(destination,favorites,movingOrder.Count-1);
        var old=movingOrder.IndexOf(id); if(old==destination) return;
        movingOrder.RemoveAt(old);movingOrder.Insert(destination,id);
        foreach(var sibling in photos.Children.OfType<FrameworkElement>().Where(child=>child!=movingCard && child.Tag is string))
        {
            var fromX=Canvas.GetLeft(sibling);var fromY=Canvas.GetTop(sibling);
            sibling.BeginAnimation(Canvas.LeftProperty,null);sibling.BeginAnimation(Canvas.TopProperty,null);
            PlaceCard(sibling,movingOrder.IndexOf((string)sibling.Tag),slots);
            sibling.BeginAnimation(Canvas.LeftProperty,new DoubleAnimation(fromX,Canvas.GetLeft(sibling),TimeSpan.FromMilliseconds(140)));
            sibling.BeginAnimation(Canvas.TopProperty,new DoubleAnimation(fromY,Canvas.GetTop(sibling),TimeSpan.FromMilliseconds(140)));
        }
    }
    private async void FinishMove(bool commit)
    {
        if(movingCard==null) return;
        var order=commit?movingOrder!.ToArray():originalOrder;
        var button=movingButton;movingCard=null;movingButton=null;movingOrder=null;
        button?.ReleaseMouseCapture();
        if(commit) app.ReorderRail(order);
        foreach(var sibling in photos.Children.OfType<FrameworkElement>().Where(child=>child.Tag is string))
        {
            var x=Canvas.GetLeft(sibling);var y=Canvas.GetTop(sibling);
            sibling.BeginAnimation(Canvas.LeftProperty,null);sibling.BeginAnimation(Canvas.TopProperty,null);
            PlaceCard(sibling,Array.IndexOf(order,(string)sibling.Tag),photos.Children.Count);
            sibling.BeginAnimation(Canvas.LeftProperty,new DoubleAnimation(x,Canvas.GetLeft(sibling),TimeSpan.FromMilliseconds(190)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
            sibling.BeginAnimation(Canvas.TopProperty,new DoubleAnimation(y,Canvas.GetTop(sibling),TimeSpan.FromMilliseconds(190)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
        }
        Lift(null);await Task.Delay(210);
        refreshPending=true;EndDrag();
        SetStatus(commit?"已调整挂图顺序":"已取消移动");
    }
    private FrameworkElement Card(CaptureItem item, int index)
    {
        var card = new Grid { Width = 196, Height = 206, Margin = new Thickness(12, 0, 12, 8), RenderTransformOrigin = new Point(.5, .08), RenderTransform = new RotateTransform(index % 2 == 0 ? -1.5 : 1.5) };
        var transforms=new TransformGroup();transforms.Children.Add(card.RenderTransform);transforms.Children.Add(new ScaleTransform(1,1));transforms.Children.Add(new TranslateTransform());card.RenderTransform=transforms;
        card.Effect=new DropShadowEffect { BlurRadius=18,ShadowDepth=6,Color=Colors.Black,Opacity=0 };
        card.LayoutTransform = new ScaleTransform(app.Configuration.PhotoWidth / 196d,app.Configuration.PhotoWidth / 196d);
        var content = new StackPanel();
        content.Children.Add(new Image { Source = item.Thumbnail, Height = 132, Width = 174, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 8), SnapsToDevicePixels = true });
        content.Children.Add(new TextBlock { Text = $"{(item.State.Favorite ? "收藏 · " : "")}{item.Created:HH:mm:ss}  ·  {item.Status}", FontSize = 11, Foreground = Brush("#52646A"), HorizontalAlignment = HorizontalAlignment.Center });
        var button = new Button { Content = content, Background = Brushes.White, BorderBrush = Brush("#CAD8D4"), Padding = new Thickness(9, 12, 9, 9), Margin = new Thickness(0, 27, 0, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = "单击复制；双击查看大图；拖动导出；右键更多操作" };
        System.Windows.Automation.AutomationProperties.SetName(button, $"截图 {item.Created:HH:mm:ss}，单击复制，双击查看大图");
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "image-" + item.Id);
        var pressPoint = new Point();
        var suppressClick = false;
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            pressPoint = e.GetPosition(button); suppressClick = false;
            if(!IsDragging) Lift(card);
            if (e.ClickCount != 2) return;
            suppressClick = true; button.ReleaseMouseCapture(); e.Handled = true; app.Preview(item);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(() => suppressClick = false));
        };
        button.PreviewMouseMove += async (_, e) =>
        {
            if(movingCard==card)
            {
                e.Handled=true;
                var railPoint=e.GetPosition(this);
                if(railPoint.X<0 || railPoint.X>ActualWidth || railPoint.Y<0 || railPoint.Y>ActualHeight)
                {
                    Lift(null);movingCard=null;movingButton=null;movingOrder=null;
                    foreach(var sibling in photos.Children.OfType<FrameworkElement>().Where(child=>child.Tag is string))
                    {
                        sibling.BeginAnimation(Canvas.LeftProperty,null);sibling.BeginAnimation(Canvas.TopProperty,null);
                        PlaceCard(sibling,Array.IndexOf(originalOrder,(string)sibling.Tag),photos.Children.Count);
                    }
                    button.ReleaseMouseCapture();
                    await app.DragImage(button,item,true);
                    await Dispatcher.InvokeAsync(()=>suppressClick=false,System.Windows.Threading.DispatcherPriority.Background);
                }
                else MoveAlongRope(e.GetPosition(photos));
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed || suppressClick || IsDragging) return;
            var current = e.GetPosition(button);
            if (Math.Abs(current.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            suppressClick=true;e.Handled=true;StartDrag();movingCard=card;movingButton=button;
            originalOrder=photos.Children.OfType<FrameworkElement>().Where(child=>child.Tag is string).Select(child=>(string)child.Tag).ToArray();
            movingOrder=originalOrder.ToList();Lift(card);button.CaptureMouse();button.Focus();
            MoveAlongRope(e.GetPosition(photos));
        };
        button.PreviewMouseLeftButtonUp += (_, e) => { if(movingCard==card) { e.Handled=true;FinishMove(true); } else if(!IsDragging) Lift(null); };
        button.LostMouseCapture += (_, _) => { if(movingCard==card) FinishMove(false); else if(!IsDragging) Lift(null); };
        button.Click += async (_, _) => { if (!suppressClick) await app.Copy(item); };
        var menu = new ContextMenu();
        AddMenu(menu, "复制图片", async () => await app.Copy(item));
        AddMenu(menu, "查看大图…", () => app.Preview(item));
        AddMenu(menu, "编辑副本…", () => app.Edit(item));
        AddMenu(menu, "识别文字…", () => app.Ocr(item));
        AddMenu(menu, "贴到桌面", () => app.Pin(item));
        var favorite = new MenuItem { Header = item.State.Favorite ? "取消固定收藏" : "固定收藏", IsCheckable = true, IsChecked = item.State.Favorite };
        void SyncFavoriteMenu()
        {
            favorite.IsChecked=item.State.Favorite;
            favorite.Header=item.State.Favorite?"取消固定收藏":"固定收藏";
        }
        favorite.Click += (_, _) => { app.ToggleFavorite(item);SyncFavoriteMenu(); }; menu.Items.Add(favorite);
        AddMenu(menu, "另存为…", () => app.SaveAs(item));
        if (item.PendingImage != null) AddMenu(menu, "重试保存", async () => await app.Persist(item));
        AddMenu(menu, "从绳子取下", () => app.HideItems(new[] { item }));
        AddMenu(menu, "打开截图文件夹", () => app.OpenImageFolder());
        menu.Opened += (_, _) => { SyncFavoriteMenu();MenuOpen = true; };
        menu.Closed += (_, _) => { MenuOpen = false; KeepUntil = DateTime.Now.AddSeconds(1); };
        button.ContextMenu = menu;
        card.Children.Add(button);
        var peg = new Button { Width = 28, Height = 44, MinHeight = 0, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "取下为桌面贴图" };
        var pegTemplate = new ControlTemplate(typeof(Button));
        pegTemplate.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)); peg.Template = pegTemplate;
        peg.Content = new CartoonPeg(item.State.Favorite ? "#F5C64D" : index%2==0 ? "#A9D8C6" : "#F2B5A7",item.State.Favorite) { Width = 28, Height = 44 };
        var favoriteState = item.State.Favorite ? "已固定收藏 · 金色星标夹" : "未固定收藏 · 普通夹";
        peg.ToolTip = favoriteState + "\n点击取下为桌面贴图；右键图片可切换收藏";
        System.Windows.Automation.AutomationProperties.SetName(peg, favoriteState + "，取下截图为桌面贴图");
        System.Windows.Automation.AutomationProperties.SetItemStatus(peg,favoriteState);
        System.Windows.Automation.AutomationProperties.SetAutomationId(peg, "pin-" + item.Id);
        peg.Click += (_, _) => app.Pin(item);
        card.Children.Add(peg);
        if(item.State.Favorite)
        {
            var badge = new Border { Background=Brush("#F5C64D"),BorderBrush=Brush("#9C641B"),BorderThickness=new Thickness(1.5),CornerRadius=new CornerRadius(8),Padding=new Thickness(7,3,7,3),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,RenderTransform=new TranslateTransform(47,3),IsHitTestVisible=false,
                Child=new TextBlock { Text="收藏",FontSize=13,FontWeight=FontWeights.Bold,Foreground=Brush("#624014") } };
            card.Children.Add(badge);
        }
        return card;
    }
    private static void AddMenu(ContextMenu menu, string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
    internal static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!app.IsExiting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}

internal sealed class Cord : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var curve = new StreamGeometry();
        using (var context = curve.Open())
        {
            context.BeginFigure(new Point(12, 19), false, false);
            context.BezierTo(new Point(ActualWidth/3, 107), new Point(ActualWidth*2/3, 107), new Point(ActualWidth - 12, 19), true, false);
        }
        dc.DrawGeometry(null, new Pen(RailWindow.Brush("#617D78"), 5), curve);
        dc.DrawGeometry(null, new Pen(RailWindow.Brush("#E9D4A9"), 2.5), curve);
        dc.DrawEllipse(RailWindow.Brush("#E9D4A9"), new Pen(RailWindow.Brush("#617D78"),1.5), new Point(12, 19), 5, 5);
        dc.DrawEllipse(RailWindow.Brush("#E9D4A9"), new Pen(RailWindow.Brush("#617D78"),1.5), new Point(ActualWidth - 12, 19), 5, 5);
    }
}

internal sealed class CartoonPeg : FrameworkElement
{
    private readonly string color;
    private readonly bool favorite;
    internal CartoonPeg(string color,bool favorite = false) { this.color = color; this.favorite = favorite; }
    protected override void OnRender(DrawingContext dc)
    {
        var ink = RailWindow.Brush("#57766C"); var pen = new Pen(ink,1.7) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round };
        dc.DrawRoundedRectangle(RailWindow.Brush(color),pen,new Rect(5,1,18,41),8,8);
        dc.DrawLine(pen,new Point(14,6),new Point(14,14));
        if(favorite)
        {
            dc.DrawEllipse(RailWindow.Brush("#FFF5CF"),new Pen(RailWindow.Brush("#9C641B"),2),new Point(14,10),11,11);
            var star = new StreamGeometry();
            using(var context=star.Open())
            {
                for(var i=0;i<10;i++)
                {
                    var angle=-Math.PI/2+i*Math.PI/5;var radius=i%2==0?8:3.6;
                    var point=new Point(14+Math.Cos(angle)*radius,10+Math.Sin(angle)*radius);
                    if(i==0) context.BeginFigure(point,true,true); else context.LineTo(point,true,false);
                }
            }
            dc.DrawGeometry(RailWindow.Brush("#90631F"),null,star);
        }
        else dc.DrawEllipse(RailWindow.Brush("#FFF8E7"),pen,new Point(14,20),3.5,3.5);
        dc.DrawLine(pen,new Point(14,27),new Point(14,36));
    }
}

internal sealed class WelcomeLetter : FrameworkElement
{
    private readonly string text;
    private readonly string color;
    internal WelcomeLetter(string text,string color) { this.text=text;this.color=color; }
    protected override void OnRender(DrawingContext dc)
    {
        if(ActualWidth<=0 || ActualHeight<=0) return;
        var punctuation=text=="，" || text=="！";
        var size=Math.Min(ActualWidth,ActualHeight)*.66*(punctuation?.8:1);
        var formatted=new FormattedText(text,System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,
            new Typeface(new FontFamily("YouYuan, Microsoft YaHei UI"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),size,Brushes.Black,1);
        // Use one em-based size: do not enlarge narrow glyphs or punctuation to fill the card.
        var glyph=formatted.BuildGeometry(new Point((ActualWidth-formatted.WidthIncludingTrailingWhitespace)/2,(ActualHeight-formatted.Height)/2));
        var ink=(Color)ColorConverter.ConvertFromString(color);
        var light=Color.FromRgb((byte)(ink.R+(255-ink.R)*.15),(byte)(ink.G+(255-ink.G)*.15),(byte)(ink.B+(255-ink.B)*.15));
        dc.PushTransform(new TranslateTransform(0,1.5));dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(28,50,70,55)),null,glyph);dc.Pop();
        dc.DrawGeometry(new LinearGradientBrush(light,ink,90),null,glyph);
    }
}

internal sealed class WelcomeCard : Grid
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
}
