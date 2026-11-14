using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PlumbobForge.Desktop.Controls;

public class GuideTarget : AvaloniaObject
{
    public static readonly AttachedProperty<string?> IdProperty =
        AvaloniaProperty.RegisterAttached<GuideTarget, Control, string?>("Id");

    public static string? GetId(Control control) => control.GetValue(IdProperty);
    public static void SetId(Control control, string? value) => control.SetValue(IdProperty, value);

    public static bool Highlight(Visual root, string id)
    {
        var target = root.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => GetId(c) == id && c.IsEffectivelyVisible && c.Bounds.Width > 0);
        if (target == null) return false;

        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer == null) return false;

        target.BringIntoView();

        var accent = target.FindResource("AccentBrush") as ISolidColorBrush;
        var color = accent?.Color ?? Colors.MediumSeaGreen;
        var outline = new Border
        {
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(2.5),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(color, 0.12),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Spread = 2, Color = Color.FromArgb(150, color.R, color.G, color.B) }),
            Margin = new Thickness(-4),
            IsHitTestVisible = false,
        };
        AdornerLayer.SetAdorner(target, outline);

        // Pulse a few times, then fade away.
        int ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            ticks++;
            double t = ticks * 0.06;
            outline.Opacity = t < 3.2 ? 0.55 + 0.45 * Math.Cos(t * Math.PI * 1.25) * 0.5 + 0.225 : Math.Max(0, 1 - (t - 3.2) * 2);
            if (t >= 3.7)
            {
                timer.Stop();
                if (AdornerLayer.GetAdorner(target) == outline) AdornerLayer.SetAdorner(target, null);
            }
        };
        timer.Start();
        return true;
    }
}
