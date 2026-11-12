using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace PlumbobForge.Desktop.Controls;

public static class SmoothScrolling
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(SmoothScrolling), defaultValue: true);

    public static bool GetIsEnabled(ScrollViewer element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ScrollViewer element, bool value) => element.SetValue(IsEnabledProperty, value);

    private const double StepSize = 100;
    private const double TimeConstant = 0.07;

    private sealed class State
    {
        public Vector Target;
        public Vector LastSet;
        public bool Running;
        public TimeSpan? LastFrame;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, State> _states = new();
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        InputElement.PointerWheelChangedEvent.AddClassHandler<ScrollViewer>(OnWheel, RoutingStrategies.Tunnel);
    }

    private static void OnWheel(ScrollViewer sender, PointerWheelEventArgs e)
    {
        if (e.Handled || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) return;

        double dx = e.Delta.X, dy = e.Delta.Y;
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0 && dx == 0)
        {
            dx = dy;
            dy = 0;
        }
        if (dx == 0 && dy == 0) return;

        var (target, delta) = PickViewer(e.Source as Visual, dx, dy);
        if (target != sender) return;

        e.Handled = true;
        ScrollBy(sender, delta);
    }

    private static (ScrollViewer? Viewer, Vector Delta) PickViewer(Visual? source, double dx, double dy)
    {
        bool vertical = dy != 0;
        double amount = vertical ? dy : dx;

        ScrollViewer? sideways = null;
        bool anyVertical = false;

        for (var v = source; v != null; v = v.GetVisualParent())
        {
            if (v is ComboBox or NumericUpDown or Calendar && ((InputElement)v).IsKeyboardFocusWithin) return (null, default);
            if (v is not ScrollViewer sv) continue;
            if (!GetIsEnabled(sv)) return (null, default);
            if (sv.Content is ILogicalScrollable { IsLogicalScrollEnabled: true }) return (null, default);

            var max = MaxOffset(sv);
            var at = CurrentTarget(sv);

            if (vertical)
            {
                if (max.Y > 0.5)
                {
                    anyVertical = true;
                    if (CanMove(at.Y, max.Y, -amount)) return (sv, new Vector(0, -amount));
                }
                else if (max.X > 0.5 && CanMove(at.X, max.X, -amount))
                {
                    if (sv.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) return (sv, new Vector(-amount, 0));
                    sideways ??= sv;
                }
            }
            else if (max.X > 0.5 && CanMove(at.X, max.X, -amount))
            {
                return (sv, new Vector(-amount, 0));
            }
        }

        if (vertical && !anyVertical && sideways != null) return (sideways, new Vector(-amount, 0));
        return (null, default);
    }

    private static bool CanMove(double at, double max, double direction) =>
        direction < 0 ? at > 0.5 : at < max - 0.5;

    private static Vector MaxOffset(ScrollViewer sv) => new(
        Math.Max(0, sv.Extent.Width - sv.Viewport.Width),
        Math.Max(0, sv.Extent.Height - sv.Viewport.Height));

    private static Vector CurrentTarget(ScrollViewer sv) =>
        _states.TryGetValue(sv, out var st) && st.Running ? st.Target : sv.Offset;

    private static Vector Clamp(Vector v, Vector max) =>
        new(Math.Clamp(v.X, 0, max.X), Math.Clamp(v.Y, 0, max.Y));

    private static void ScrollBy(ScrollViewer sv, Vector notches)
    {
        var st = _states.GetValue(sv, _ => new State());

        if (!st.Running || Distance(sv.Offset, st.LastSet) > 1)
        {
            st.Target = sv.Offset;
            st.Running = false;
        }

        double stepX = Math.Min(StepSize, Math.Max(16, sv.Viewport.Width * 0.8));
        double stepY = Math.Min(StepSize, Math.Max(16, sv.Viewport.Height * 0.8));
        st.Target = Clamp(st.Target + new Vector(notches.X * stepX, notches.Y * stepY), MaxOffset(sv));

        if (st.Running) return;
        var topLevel = TopLevel.GetTopLevel(sv);
        if (topLevel == null)
        {
            sv.Offset = st.Target;
            return;
        }
        st.Running = true;
        st.LastSet = sv.Offset;
        st.LastFrame = null;
        topLevel.RequestAnimationFrame(t => Tick(sv, st, t));
    }

    private static void Tick(ScrollViewer sv, State st, TimeSpan now)
    {
        if (!st.Running) return;
        var topLevel = TopLevel.GetTopLevel(sv);
        if (topLevel == null || Distance(sv.Offset, st.LastSet) > 1)
        {
            st.Running = false;
            return;
        }

        double dt = st.LastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.1) : 1.0 / 60;
        st.LastFrame = now;

        st.Target = Clamp(st.Target, MaxOffset(sv));
        var current = sv.Offset;
        var remaining = st.Target - current;
        if (remaining.Length < 0.5)
        {
            sv.Offset = st.Target;
            st.LastSet = sv.Offset;
            st.Running = false;
            return;
        }

        double k = 1 - Math.Exp(-dt / TimeConstant);
        sv.Offset = current + remaining * k;
        st.LastSet = sv.Offset;

        if (Distance(sv.Offset, current) < 0.01)
        {
            st.Running = false;
            return;
        }
        topLevel.RequestAnimationFrame(t => Tick(sv, st, t));
    }

    private static double Distance(Vector a, Vector b) => (a - b).Length;
}
