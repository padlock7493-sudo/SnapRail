using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SnapRail;

internal static class ScrollLayoutProbe
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent,i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    internal static void Run(string output)
    {
        System.IO.Directory.CreateDirectory(output);
        var app = new Application(); Exception? failure = null;
        using (var source = typeof(ScrollLayoutProbe).Assembly.GetManifestResourceStream("SnapRail.LayoutResources.xaml")!)
        {
            var document = System.Xml.Linq.XDocument.Load(source);
            var resources = document.Root!.Elements().Single(element => element.Name.LocalName == "Application.Resources");
            var dictionary = new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("ResourceDictionary","http://schemas.microsoft.com/winfx/2006/xaml/presentation"),resources.Elements());
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
        }
        var screen = System.Windows.Forms.Screen.PrimaryScreen!;
        var window = new ScrollCaptureWindow(new System.Drawing.Rectangle(screen.Bounds.Left+100,screen.Bounds.Top+100,200,150));
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(500);
                foreach (var monitor in System.Windows.Forms.Screen.AllScreens)
                {
                    Native.SetWindowPos(new WindowInteropHelper(window).Handle,new IntPtr(-1),monitor.WorkingArea.Left+50,monitor.WorkingArea.Top+50,0,0,0x51);
                    await Task.Delay(500); window.UpdateLayout(); Check(window);
                    ImageStore.Write(CaptureWindow.Take(Native.Bounds(window)),System.IO.Path.Combine(output,$"layout-{Native.Dpi(monitor)*100:0}.png"));
                    Console.WriteLine($"PASS rendered scroll controls fit at {Native.Dpi(monitor)*100:0}%");
                }
                var status = Descendants(window).OfType<TextBlock>().First(element => AutomationProperties.GetAutomationId(element)=="scroll-status");
                status.Text = string.Concat(Enumerable.Repeat("状态说明测试，长文字不会遮挡按钮。",250));
                await Task.Delay(300);
                var view = (ScrollViewer)window.Content; view.ScrollToEnd(); await Task.Delay(200); Check(window);
                Console.WriteLine("PASS long status remains scrollable to every action");
                window.SizeToContent = SizeToContent.Manual; window.Width = 420; window.Height = 240;
                await Task.Delay(200); view.ScrollToEnd(); await Task.Delay(200); Check(window);
                Console.WriteLine("PASS resized window can scroll to every action");
            }
            catch(Exception ex) { failure = ex; }
            finally { window.Close(); }
        };
        app.Run(window); if (failure != null) throw failure;
    }
    private static void Check(Window window)
    {
        var view = (ScrollViewer)window.Content;
        var buttons = Descendants(view).OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button).StartsWith("scroll-",StringComparison.Ordinal)).ToArray();
        if (buttons.Length != 5) throw new Exception("Expected five scroll capture actions.");
        foreach(var button in buttons)
        {
            var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
            if (bounds.Left < -1 || bounds.Top < -1 || bounds.Right > view.ActualWidth+1 || bounds.Bottom > view.ActualHeight+1)
                throw new Exception($"Clipped {AutomationProperties.GetAutomationId(button)}: {bounds}, viewport {view.ActualWidth}x{view.ActualHeight}");
        }
    }
}
