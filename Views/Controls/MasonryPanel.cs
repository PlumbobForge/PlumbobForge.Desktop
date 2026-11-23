using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace PlumbobForge.Desktop.Views.Controls;

// Cards in columns of equal width, dealt out in turn (card i goes to column i % Columns), so each
// keeps its own height and expanding one card never moves the others to another column.
// One column is a plain stack.
public class MasonryPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<MasonryPanel, int>(nameof(Columns), 1);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<MasonryPanel, double>(nameof(ColumnSpacing), 12);

    static MasonryPanel()
    {
        AffectsMeasure<MasonryPanel>(ColumnsProperty, ColumnSpacingProperty);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    private int ColumnCount => Math.Max(1, Columns);

    private double ColumnWidth(double width)
    {
        if (double.IsInfinity(width)) return width;
        return Math.Max(0, (width - ColumnSpacing * (ColumnCount - 1)) / ColumnCount);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double columnWidth = ColumnWidth(availableSize.Width);
        var heights = new double[ColumnCount];
        double widest = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            int column = i % ColumnCount;
            heights[column] += child.DesiredSize.Height;
            widest = Math.Max(widest, child.DesiredSize.Width);
        }
        double width = double.IsInfinity(availableSize.Width)
            ? widest * ColumnCount + ColumnSpacing * (ColumnCount - 1)
            : availableSize.Width;
        return new Size(width, heights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double columnWidth = ColumnWidth(finalSize.Width);
        var heights = new double[ColumnCount];
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            int column = i % ColumnCount;
            double x = column * (columnWidth + ColumnSpacing);
            child.Arrange(new Rect(x, heights[column], columnWidth, child.DesiredSize.Height));
            heights[column] += child.DesiredSize.Height;
        }
        return finalSize;
    }
}
