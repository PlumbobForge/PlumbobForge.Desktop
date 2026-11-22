using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PlumbobForge.Desktop.Views.Controls;

// Floating "Top" / "Bottom" buttons for a long scrolling list. They fade in once the list is scrolled down;
// "Bottom" fades out again at the end of the list while "Top" stays. Source is the control whose
// ScrollViewer they drive (a ListBox, or the ScrollViewer itself); they find it from its scroll events.
public partial class ScrollJumpButtons : UserControl
{
    public static readonly StyledProperty<Control?> SourceProperty =
        AvaloniaProperty.Register<ScrollJumpButtons, Control?>(nameof(Source));

    public Control? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    // Scrolled at least this far before the buttons show, so a small nudge doesn't bring them up.
    private const double ShowAfter = 300;
    private const double EndTolerance = 4;

    private ScrollViewer? _scrollViewer;

    public ScrollJumpButtons()
    {
        InitializeComponent();
    }

    public bool IsTopShown => TopSlot.Classes.Contains("shown");
    public bool IsBottomShown => BottomSlot.Classes.Contains("shown");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SourceProperty) return;
        if (change.OldValue is Control old) old.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        _scrollViewer = change.NewValue as ScrollViewer;
        if (change.NewValue is Control source) source.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        Update();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is ScrollViewer sv) _scrollViewer = sv;
        Update();
    }

    private void Update()
    {
        var sv = _scrollViewer;
        double offset = sv?.Offset.Y ?? 0;
        double maxOffset = sv == null ? 0 : Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        bool scrolledDown = offset > ShowAfter;
        bool atBottom = offset >= maxOffset - EndTolerance;
        TopSlot.Classes.Set("shown", scrolledDown);
        BottomSlot.Classes.Set("shown", scrolledDown && !atBottom);
    }

    private void OnTopClick(object? sender, RoutedEventArgs e)
    {
        _scrollViewer?.ScrollToHome();
    }

    // A virtualized list only knows its real height once the last rows are laid out, so land on the end again after that.
    private void OnBottomClick(object? sender, RoutedEventArgs e)
    {
        var sv = _scrollViewer;
        if (sv == null) return;
        sv.ScrollToEnd();
        Dispatcher.UIThread.Post(sv.ScrollToEnd, DispatcherPriority.Background);
    }
}
