using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace PlumbobForge.Desktop.Controls;

public class AdaptiveTabPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        StackPanel.SpacingProperty.AddOwner<AdaptiveTabPanel>();

    // Gap between a tab's icon and its label; added back for each hidden label.
    public static readonly StyledProperty<double> LabelSpacingProperty =
        AvaloniaProperty.Register<AdaptiveTabPanel, double>(nameof(LabelSpacing), 8);

    public static readonly DirectProperty<AdaptiveTabPanel, bool> IsCompactProperty =
        AvaloniaProperty.RegisterDirect<AdaptiveTabPanel, bool>(nameof(IsCompact), p => p.IsCompact);

    private bool _isCompact;

    static AdaptiveTabPanel()
    {
        AffectsMeasure<AdaptiveTabPanel>(SpacingProperty, LabelSpacingProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double LabelSpacing
    {
        get => GetValue(LabelSpacingProperty);
        set => SetValue(LabelSpacingProperty, value);
    }

    public bool IsCompact
    {
        get => _isCompact;
        private set
        {
            if (SetAndRaise(IsCompactProperty, ref _isCompact, value))
            {
                PseudoClasses.Set(":compact", value);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        var width = MeasureRow(visible);

        // Width the hidden labels would add back.
        double labels = 0;
        if (_isCompact)
        {
            foreach (var child in visible) labels += LabelWidth(child);
        }
        double fullWidth = width + labels;

        bool compact = fullWidth > availableSize.Width;
        if (compact != _isCompact)
        {
            IsCompact = compact;
            width = MeasureRow(visible);
        }

        double height = visible.Count == 0 ? 0 : visible.Max(c => c.DesiredSize.Height);
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    private double MeasureRow(System.Collections.Generic.List<Control> visible)
    {
        double width = 0;
        foreach (var child in visible)
        {
            child.Measure(Size.Infinity);
            width += child.DesiredSize.Width;
        }
        return width + Math.Max(0, visible.Count - 1) * Spacing;
    }

    private double LabelWidth(Control tab)
    {
        var label = tab.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Classes.Contains("tab-label"));
        if (label == null || string.IsNullOrEmpty(label.Text)) return 0;

        using var layout = new TextLayout(
            label.Text,
            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
            label.FontSize,
            null);
        return Math.Ceiling(layout.WidthIncludingTrailingWhitespace) + LabelSpacing;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var w = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w + Spacing;
        }
        return finalSize;
    }
}
