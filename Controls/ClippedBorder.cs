using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PlumbobForge.Desktop.Controls;

/// <summary>
/// A Border subclass that sets Visual.Clip to a true rounded-rectangle
/// StreamGeometry matching its CornerRadius, so child content (images,
/// gradients, etc.) is genuinely clipped to the rounded corners rather
/// than only to a rectangle.
/// </summary>
public class ClippedBorder : Border
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);

        // Apply the clip after the base arrange has set the final size.
        ApplyRoundedClip(finalSize);

        return result;
    }

    private void ApplyRoundedClip(Size size)
    {
        double w = size.Width;
        double h = size.Height;

        if (w <= 0 || h <= 0)
        {
            Clip = null;
            return;
        }

        var cr = CornerRadius;
        double maxR = Math.Max(
            Math.Max(cr.TopLeft, cr.TopRight),
            Math.Max(cr.BottomLeft, cr.BottomRight));

        if (maxR <= 0)
        {
            Clip = null;
            return;
        }

        // Clamp each radius so it never exceeds half the width or height.
        double half = Math.Min(w / 2, h / 2);
        double tl = Math.Min(cr.TopLeft, half);
        double tr = Math.Min(cr.TopRight, half);
        double br = Math.Min(cr.BottomRight, half);
        double bl = Math.Min(cr.BottomLeft, half);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            // Start just after the top-left arc
            ctx.BeginFigure(new Point(tl, 0), true);

            // ── Top edge → top-right arc ──
            ctx.LineTo(new Point(w - tr, 0));
            if (tr > 0)
                ctx.ArcTo(new Point(w, tr),
                           new Size(tr, tr), 0, false, SweepDirection.Clockwise);

            // ── Right edge → bottom-right arc ──
            ctx.LineTo(new Point(w, h - br));
            if (br > 0)
                ctx.ArcTo(new Point(w - br, h),
                           new Size(br, br), 0, false, SweepDirection.Clockwise);

            // ── Bottom edge → bottom-left arc ──
            ctx.LineTo(new Point(bl, h));
            if (bl > 0)
                ctx.ArcTo(new Point(0, h - bl),
                           new Size(bl, bl), 0, false, SweepDirection.Clockwise);

            // ── Left edge → top-left arc ──
            ctx.LineTo(new Point(0, tl));
            if (tl > 0)
                ctx.ArcTo(new Point(tl, 0),
                           new Size(tl, tl), 0, false, SweepDirection.Clockwise);

            ctx.EndFigure(true);
        }

        Clip = geo;
    }
}
