using System;
using Avalonia;
using Avalonia.Controls;

namespace PlumbobForge.Desktop.Controls;

public class AdaptiveWrapPanel : Panel
{
    public static readonly StyledProperty<double> ItemMinWidthProperty =
        AvaloniaProperty.Register<AdaptiveWrapPanel, double>(nameof(ItemMinWidth), 210.0);

    public double ItemMinWidth
    {
        get => GetValue(ItemMinWidthProperty);
        set => SetValue(ItemMinWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        if (width <= 0) width = 1000;

        int cols = Math.Max(1, (int)Math.Floor(width / ItemMinWidth));
        double actualItemWidth = width / cols;

        Size childConstraint = new Size(actualItemWidth, double.PositiveInfinity);

        double totalHeight = 0;
        double currentRowMaxHeight = 0;

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(childConstraint);
            currentRowMaxHeight = Math.Max(currentRowMaxHeight, child.DesiredSize.Height);

            if ((i + 1) % cols == 0 || i == Children.Count - 1)
            {
                totalHeight += currentRowMaxHeight;
                currentRowMaxHeight = 0;
            }
        }

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = finalSize.Width;
        if (width <= 0) return finalSize;

        int cols = Math.Max(1, (int)Math.Floor(width / ItemMinWidth));
        double actualItemWidth = width / cols;

        double currentY = 0;
        int count = Children.Count;

        for (int i = 0; i < count; i += cols)
        {
            int rowEnd = Math.Min(i + cols, count);
            double rowHeight = 0;

            for (int j = i; j < rowEnd; j++)
            {
                rowHeight = Math.Max(rowHeight, Children[j].DesiredSize.Height);
            }

            for (int j = i; j < rowEnd; j++)
            {
                int col = j - i;
                Rect rect = new Rect(col * actualItemWidth, currentY, actualItemWidth, rowHeight);
                Children[j].Arrange(rect);
            }

            currentY += rowHeight;
        }

        return new Size(width, currentY);
    }
}
