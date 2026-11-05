using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapRail;

internal sealed class AnnotationToolbar : Border
{
    private readonly AnnotationCanvas editor;
    private readonly Dictionary<AnnotationTool, Button> tools = new();
    private readonly Button undo;
    private readonly Button redo;
    private readonly Button delete;
    private readonly Button thickness;
    private readonly Button fontSize;
    private readonly Dictionary<string, Button> colors = new();
    internal event Action? Confirmed;
    internal event Action? Cancelled;
    internal event Action? SaveRequested;
    internal event Action? FullScreenRequested;
    internal event Action? OcrRequested;
    internal event Action? ScrollRequested;

    internal AnnotationToolbar(AnnotationCanvas editor, bool capture)
    {
        this.editor = editor;
        Background = Brushes.White;
        BorderBrush = RailWindow.Brush("#C7D9D3"); BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12); Padding = new Thickness(10, 8, 10, 8);
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "annotation-toolbar");
        var rows = new StackPanel();
        var toolsRow = new WrapPanel();
        var drawingTools = new WrapPanel { Margin = new Thickness(0, 0, 10, 0) };
        foreach (var (tool, label) in new[]
        {
            (AnnotationTool.Select, "选区"), (AnnotationTool.Object, "对象"), (AnnotationTool.Rectangle, "矩形"), (AnnotationTool.Ellipse, "椭圆"),
            (AnnotationTool.Arrow, "箭头"), (AnnotationTool.Pen, "画笔"), (AnnotationTool.Text, "文字"), (AnnotationTool.Mosaic, "马赛克")
        })
        {
            var button = Make(label, "tool-" + tool.ToString().ToLowerInvariant());
            button.ToolTip = tool == AnnotationTool.Select ? "移动、调整或重新框选区域" : "在选区内绘制" + label;
            if (tool == AnnotationTool.Object) button.ToolTip = "选择并拖动标注；Delete 删除；双击文字修改；方向键微调";
            button.Click += (_, _) => editor.SetTool(tool);
            tools.Add(tool, button); drawingTools.Children.Add(button);
        }
        toolsRow.Children.Add(drawingTools);
        var palette = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var color in new[] { "#E34B4B", "#126B64", "#2675D8", "#F4BD50", "#FFFFFF", "#20282F" })
        {
            var swatch = Make(new Border { Width = 14, Height = 14, Background = RailWindow.Brush(color), BorderBrush = RailWindow.Brush("#899B98"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) }, "color-" + color[1..]);
            swatch.Padding = new Thickness(5); swatch.Width = 28; swatch.MinHeight = 30; swatch.ToolTip = "颜色 " + color;
            System.Windows.Automation.AutomationProperties.SetName(swatch, "标注颜色 " + color);
            swatch.Click += (_, _) => editor.StyleSelection(color: color);
            colors.Add(color, swatch); palette.Children.Add(swatch);
        }
        toolsRow.Children.Add(palette);
        var optionsRow = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        var styleGroup = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        thickness = Choice("线宽", "stroke-width", new[] { 2d, 4d, 6d, 10d }, value => editor.StyleSelection(thickness: value));
        fontSize = Choice("字号", "font-size", new[] { 16d, 24d, 32d, 48d }, value => editor.StyleSelection(fontSize: value));
        styleGroup.Children.Add(thickness); styleGroup.Children.Add(fontSize); optionsRow.Children.Add(styleGroup);
        var editing = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        undo = Make("↶", "undo"); undo.ToolTip = "撤销 · Ctrl+Z"; undo.Width = 32;
        System.Windows.Automation.AutomationProperties.SetName(undo, "撤销");
        undo.Click += (_, _) => editor.Undo(); editing.Children.Add(undo);
        redo = Make("↷", "redo"); redo.ToolTip = "重做 · Ctrl+Y / Ctrl+Shift+Z"; redo.Width = 32;
        System.Windows.Automation.AutomationProperties.SetName(redo, "重做");
        redo.Click += (_, _) => editor.Redo(); editing.Children.Add(redo);
        delete = Make("删除", "delete-mark"); delete.ToolTip = "删除选中的标注 · Delete";
        delete.Click += (_, _) => editor.DeleteSelection(); editing.Children.Add(delete); optionsRow.Children.Add(editing);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        var ocr = Make("识别文字", "ocr"); ocr.Click += (_, _) => OcrRequested?.Invoke(); actions.Children.Add(ocr);
        if (capture) { var scroll = Make("长截图", "scroll-capture"); scroll.Click += (_, _) => ScrollRequested?.Invoke(); actions.Children.Add(scroll); }
        var more = Make("更多 ···", "more-actions"); more.ToolTip = "另存为、全屏选择";
        var menu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var save = new MenuItem { Header = "另存为", InputGestureText = "Ctrl+S" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(save, "save-as");
        save.Click += (_, _) => SaveRequested?.Invoke(); menu.Items.Add(save);
        if (capture)
        {
            var full = new MenuItem { Header = "选择全屏", InputGestureText = "Ctrl+A" };
            System.Windows.Automation.AutomationProperties.SetAutomationId(full, "full-screen");
            full.Click += (_, _) => FullScreenRequested?.Invoke(); menu.Items.Add(full);
        }
        more.ContextMenu = menu; more.Click += (_, _) => menu.IsOpen = true; actions.Children.Add(more); optionsRow.Children.Add(actions);
        menu.Opened += (_, _) => ((MenuItem)menu.Items[0]).Focus();
        var finish = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = Make("取消", "cancel"); cancel.ToolTip = "Esc"; cancel.Click += (_, _) => Cancelled?.Invoke(); finish.Children.Add(cancel);
        var confirm = Make(capture ? "完成并挂上  ↵" : "保存为新挂图", "confirm"); confirm.Background = RailWindow.Brush("#CDE8DE"); confirm.BorderBrush = RailWindow.Brush("#7CB4A1"); confirm.FontWeight = FontWeights.SemiBold;
        confirm.ToolTip = "Enter"; confirm.Click += (_, _) => Confirmed?.Invoke(); finish.Children.Add(confirm); optionsRow.Children.Add(finish);
        rows.Children.Add(toolsRow); rows.Children.Add(optionsRow); Child = rows;
        editor.Changed += UpdateState;
        UpdateState();
    }
    private void UpdateState()
    {
        foreach (var pair in tools)
        {
            pair.Value.Background = RailWindow.Brush(pair.Key == editor.Tool ? "#CDE8DE" : "#F3F7F6");
            pair.Value.BorderBrush = RailWindow.Brush(pair.Key == editor.Tool ? "#126B64" : "#D0DEDA");
        }
        undo.IsEnabled = editor.History.CanUndo;
        redo.IsEnabled = editor.History.CanRedo;
        delete.IsEnabled = editor.HasSelection;
        thickness.Content = $"线宽  {editor.StrokeWidth:0}  ⌄";
        fontSize.Content = $"字号  {editor.TextSize:0}  ⌄";
        foreach (var pair in colors)
        {
            pair.Value.BorderBrush = RailWindow.Brush(pair.Key == editor.Ink ? "#126B64" : "#D0DEDA");
            pair.Value.Background = RailWindow.Brush(pair.Key == editor.Ink ? "#CDE8DE" : "#F3F7F6");
        }
    }
    private static Button Choice(string label, string id, double[] values, Action<double> changed)
    {
        var button = Make(label, id);
        System.Windows.Automation.AutomationProperties.SetName(button, "标注" + label);
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var value in values)
        {
            var item = new MenuItem { Header = value.ToString("0") };
            item.Click += (_, _) => changed(value); menu.Items.Add(item);
        }
        menu.Opened += (_, _) => ((MenuItem)menu.Items[0]).Focus();
        button.ContextMenu = menu; button.Click += (_, _) => menu.IsOpen = true; return button;
    }
    private static Button Make(object content, string id)
    {
        var button = new Button { Content = content, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(2), FontSize = 12 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, id);
        return button;
    }
}
