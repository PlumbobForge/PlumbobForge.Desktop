using System;
using Avalonia;
using Avalonia.Controls;

namespace PlumbobForge.Desktop.Controls;

// Lays its children out in one row, left to right. The first child (the nav tabs) only gets the width
// the later children (Play and the configuration picker) leave over, so the tabs drop their titles
// before Play is pushed out of view, and Play stays right after the tabs instead of at the far end.
public class TabsWithTrailingPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double trailing = 0, height = 0;
        for (int i = 1; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            trailing += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double width = trailing;
        if (Children.Count > 0)
        {
            var first = Children[0];
            double left = double.IsInfinity(availableSize.Width)
                ? double.PositiveInfinity
                : Math.Max(0, availableSize.Width - trailing);
            first.Measure(new Size(left, availableSize.Height));
            width += first.DesiredSize.Width;
            height = Math.Max(height, first.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double trailing = 0;
        for (int i = 1; i < Children.Count; i++) trailing += Children[i].DesiredSize.Width;

        double x = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            double w = i == 0
                ? Math.Min(child.DesiredSize.Width, Math.Max(0, finalSize.Width - trailing))
                : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }
        return finalSize;
    }
}
