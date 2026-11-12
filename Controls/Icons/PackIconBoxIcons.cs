using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace PlumbobForge.Desktop.Controls.Icons;

public class PackIconBoxIcons : Control
{
    private const double DefaultSize = 16;

    public static readonly StyledProperty<PackIconBoxIconsKind> KindProperty =
        AvaloniaProperty.Register<PackIconBoxIcons, PackIconBoxIconsKind>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TemplatedControl.ForegroundProperty.AddOwner<PackIconBoxIcons>();

    private static readonly Dictionary<PackIconBoxIconsKind, Geometry?> _geometries = new();

    static PackIconBoxIcons()
    {
        AffectsRender<PackIconBoxIcons>(KindProperty, ForegroundProperty);
    }

    public PackIconBoxIconsKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private static Geometry? GetGeometry(PackIconBoxIconsKind kind)
    {
        if (_geometries.TryGetValue(kind, out var geometry))
        {
            return geometry;
        }

        var data = PackIconBoxIconsData.Get(kind);
        geometry = string.IsNullOrEmpty(data) ? null : Geometry.Parse(data);
        _geometries[kind] = geometry;
        return geometry;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? DefaultSize : System.Math.Min(DefaultSize, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? DefaultSize : System.Math.Min(DefaultSize, availableSize.Height));
    }

    public override void Render(DrawingContext context)
    {
        var geometry = GetGeometry(Kind);
        var brush = Foreground;
        if (geometry == null || brush == null)
        {
            return;
        }

        var bounds = geometry.Bounds;
        var size = Bounds.Size;
        if (bounds.Width <= 0 || bounds.Height <= 0 || size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var scale = System.Math.Min(size.Width / bounds.Width, size.Height / bounds.Height);
        var offsetX = (size.Width - bounds.Width * scale) / 2 - bounds.X * scale;
        var offsetY = (size.Height - bounds.Height * scale) / 2 - bounds.Y * scale;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            context.DrawGeometry(brush, null, geometry);
        }
    }
}
