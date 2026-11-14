using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace PlumbobForge.Desktop.Controls;

// Lays tabs out in a row. When the row doesn't fit with its titles, every tab gets the "compact"
// class so styles can hide its TextBlock.tab-label and leave the icon. The class goes on the tab
// itself (and on the item container around it): items of an ItemsControl aren't logical children
// of its panel, so a ":compact" selector on the panel would never reach them.
// The full width is worked out from the label text even while labels are hidden, so the switch
// back happens exactly when the titles fit again.
public class AdaptiveTabPanel : Panel
{
    private const string CompactClass = "compact";

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
        private set => SetAndRaise(IsCompactProperty, ref _isCompact, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);

        // Tabs added since the last pass (a new pin) pick up the current mode.
        if (ApplyMode(_isCompact)) RemeasureAll();

        var shown = Shown();
        double width = RowWidth(shown);
        double fullWidth = _isCompact ? width + shown.Sum(LabelWidth) : width;

        bool compact = fullWidth > availableSize.Width;
        if (compact != _isCompact)
        {
            IsCompact = compact;
            ApplyMode(compact);
            RemeasureAll();
            width = RowWidth(shown);
        }

        double height = shown.Count == 0 ? 0 : shown.Max(c => c.DesiredSize.Height);
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (var child in Children)
        {
            if (!IsShown(child))
            {
                child.Arrange(default);
                continue;
            }
            var size = child.DesiredSize;
            child.Arrange(new Rect(x, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
            x += size.Width + Spacing;
        }
        return finalSize;
    }

    // Hiding or showing a label only invalidates the label's own parent, so the layers above it
    // (button, item container) would hand back their cached size from before the switch. Invalidate
    // the whole tab so it measures at its new size within this pass.
    private void RemeasureAll()
    {
        foreach (var child in Children)
        {
            foreach (var layoutable in child.GetSelfAndVisualDescendants().OfType<Layoutable>())
            {
                layoutable.InvalidateMeasure();
            }
            child.Measure(Size.Infinity);
        }
    }

    // A tab can be hidden inside its item container, which itself stays visible.
    private static bool IsShown(Control child) =>
        child.IsVisible && child is not ContentPresenter { Child: { IsVisible: false } };

    private List<Control> Shown() => Children.Where(IsShown).ToList();

    private double RowWidth(List<Control> shown) =>
        shown.Sum(c => c.DesiredSize.Width) + Math.Max(0, shown.Count - 1) * Spacing;

    // Returns true when any tab's class changed.
    private bool ApplyMode(bool compact)
    {
        bool changed = false;
        foreach (var child in Children)
        {
            changed |= SetClass(child, compact);
            if (child is ContentPresenter { Child: { } inner }) changed |= SetClass(inner, compact);
        }
        return changed;
    }

    private static bool SetClass(Control control, bool on)
    {
        if (control.Classes.Contains(CompactClass) == on) return false;
        control.Classes.Set(CompactClass, on);
        return true;
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
}
