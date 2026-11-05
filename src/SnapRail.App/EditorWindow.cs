using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapRail;

internal sealed class EditorWindow : Window
{
    private readonly AnnotationCanvas editor;
    private bool completed;
    private readonly Annotation[] initialMarks;
    private readonly bool unsavedCapture;
    internal event Action<CaptureResult>? Saved;

    internal EditorWindow(CaptureResult source, bool unsavedCapture = false)
    {
        this.unsavedCapture = unsavedCapture;
        Title = "随手挂 · 编辑截图";
        Width = 1060; Height = 760; MinWidth = 680; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = RailWindow.Brush("#203238");
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor = new AnnotationCanvas(source.Original, source.Document) { ShowImage = true, Width = source.Original.PixelWidth, Height = source.Original.PixelHeight };
        editor.SetTool(AnnotationTool.Select);
        initialMarks = editor.History.Marks.ToArray();
        var toolbar = new AnnotationToolbar(editor, false) { Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center };
        toolbar.Confirmed += Save; toolbar.Cancelled += Close; toolbar.SaveRequested += SaveAs;
        toolbar.OcrRequested += () => new OcrWindow(Result().Flatten()) { Owner = this }.Show();
        layout.Children.Add(toolbar);
        if (source.Original.PixelHeight > source.Original.PixelWidth * 2)
        {
            var scale = Math.Min(1, 850d / source.Original.PixelWidth);
            editor.Width = source.Original.PixelWidth * scale; editor.Height = source.Original.PixelHeight * scale;
            var scroller = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18) };
            Grid.SetRow(scroller,1); layout.Children.Add(scroller);
        }
        else
        {
            var viewbox = new Viewbox { Child = editor, Stretch = Stretch.Uniform, Margin = new Thickness(24) };
            Grid.SetRow(viewbox, 1); layout.Children.Add(viewbox);
        }
        var hint = new TextBlock { Text = "原图和旧版本会保留  ·  Ctrl+Z 撤销 / Ctrl+Y 重做  ·  文字：Enter 换行，Ctrl+Enter 完成", Foreground = RailWindow.Brush("#D6E5E0"), Margin = new Thickness(18, 8, 18, 14), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(hint, 2); layout.Children.Add(hint);
        Content = layout;
        PreviewKeyDown += KeyPressed;
    }
    private CaptureResult Result() => editor.Export(new Int32Rect(0, 0, editor.Original.PixelWidth, editor.Original.PixelHeight));
    private void Save()
    {
        if (editor.IsDrawing) return;
        var result = Result();
        // Notify first: if rendering fails, keep the editable window alive.
        Saved?.Invoke(result);
        completed = true; Close();
    }
    private void SaveAs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"随手挂-编辑-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        try { if (dialog.ShowDialog(this) == true) ImageStore.Write(Result().Flatten(), dialog.FileName); }
        catch (Exception ex) { App.Log(ex); MessageBox.Show(this, ex.Message, "另存为失败"); }
    }
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (!editor.CancelDraft()) Close(); e.Handled = true; return; }
        if (editor.HasTextEditor)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { editor.CommitText(); editor.Focus(); e.Handled = true; }
            return;
        }
        if (editor.ObjectKey(e.Key, Keyboard.Modifiers)) { e.Handled = true; return; }
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (e.Key == Key.Z) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) editor.Redo(); else editor.Undo(); e.Handled = true; }
        else if (e.Key == Key.Y) { editor.Redo(); e.Handled = true; }
        else if (e.Key == Key.S) { SaveAs(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Save(); e.Handled = true; }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!completed && (((App)Application.Current).IsExiting == false) &&
            (unsavedCapture || editor.HasTextEditor || !editor.History.Marks.SequenceEqual(initialMarks)) &&
            MessageBox.Show(this, unsavedCapture ? "放弃这张尚未保存的长截图？" : "放弃当前编辑？原图和之前的版本不会改变。", "尚未保存的标注", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            e.Cancel = true;
        base.OnClosing(e);
    }
}
