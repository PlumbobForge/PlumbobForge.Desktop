using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class NewUserWalkthroughWindow : Window
{
    private const double PageOffset = 28;

    private CancellationTokenSource? _pageAnimation;
    private int _shownStep = -1;

    public NewUserWalkthroughWindow()
    {
        InitializeComponent();
    }

    public NewUserWalkthroughWindow(NewUserWalkthroughViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.DialogOwner = this;
        viewModel.RequestClose += success => Close(success);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        foreach (var page in PageHost.Children) page.IsVisible = false;
        Opened += (_, _) => ShowStep(viewModel.StepIndex);
    }

    protected override void OnClosed(EventArgs e)
    {
        _pageAnimation?.Cancel();
        if (DataContext is NewUserWalkthroughViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.Detach();
            vm.DialogOwner = null;
        }
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewUserWalkthroughViewModel.StepIndex) && sender is NewUserWalkthroughViewModel vm)
        {
            ShowStep(vm.StepIndex);
        }
    }


    private async void ShowStep(int step)
    {
        var pages = PageHost.Children;
        if (step < 0 || step >= pages.Count || step == _shownStep) return;

        _pageAnimation?.Cancel();
        var cts = _pageAnimation = new CancellationTokenSource();
        var token = cts.Token;

        var from = _shownStep >= 0 ? pages[_shownStep] : null;
        var to = pages[step];
        double direction = step > _shownStep ? 1 : -1;
        _shownStep = step;

        foreach (var page in pages)
        {
            if (page != from && page != to) page.IsVisible = false;
        }

        try
        {
            if (from != null)
            {
                await Fade(from, 1, 0, 0, -PageOffset * direction, TimeSpan.FromMilliseconds(150), new CubicEaseIn(), TimeSpan.Zero, token);
                if (token.IsCancellationRequested) return;
                from.IsVisible = false;
            }

            to.IsVisible = true;
            var animations = new List<Task>
            {
                Fade(to, 0, 1, PageOffset * direction, 0, TimeSpan.FromMilliseconds(320), new CubicEaseOut(), TimeSpan.Zero, token)
            };

            int index = 0;
            foreach (var control in Marked(to, "reveal"))
            {
                animations.Add(Rise(control, TimeSpan.FromMilliseconds(60 + 70 * index++), token));
            }

            index = 0;
            foreach (var control in Marked(to, "pop"))
            {
                animations.Add(Pop(control, TimeSpan.FromMilliseconds(300 + 35 * index++), token));
            }

            await Task.WhenAll(animations);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static IEnumerable<Control> Marked(Control page, string className) =>
        page.GetLogicalDescendants().OfType<Control>().Where(c => c.Classes.Contains(className));

    private static Task Fade(Control control, double fromOpacity, double toOpacity, double fromX, double toX,
        TimeSpan duration, Easing easing, TimeSpan delay, CancellationToken token)
    {
        return Build(duration, delay, easing,
            (Visual.OpacityProperty, fromOpacity, toOpacity),
            (TranslateTransform.XProperty, fromX, toX)).RunAsync(control, token);
    }

    private static Task Rise(Control control, TimeSpan delay, CancellationToken token)
    {
        return Build(TimeSpan.FromMilliseconds(420), delay, new CubicEaseOut(),
            (Visual.OpacityProperty, 0d, 1d),
            (TranslateTransform.YProperty, 14d, 0d)).RunAsync(control, token);
    }

    private static Task Pop(Control control, TimeSpan delay, CancellationToken token)
    {
        return Build(TimeSpan.FromMilliseconds(380), delay, new BackEaseOut(),
            (Visual.OpacityProperty, 0d, 1d),
            (ScaleTransform.ScaleXProperty, 0.6d, 1d),
            (ScaleTransform.ScaleYProperty, 0.6d, 1d)).RunAsync(control, token);
    }

    private static Animation Build(TimeSpan duration, TimeSpan delay, Easing easing, params (AvaloniaProperty Property, double From, double To)[] values)
    {
        var start = new KeyFrame { Cue = new Cue(0d) };
        var end = new KeyFrame { Cue = new Cue(1d) };
        foreach (var (property, fromValue, toValue) in values)
        {
            start.Setters.Add(new Setter(property, fromValue));
            end.Setters.Add(new Setter(property, toValue));
        }

        return new Animation
        {
            Duration = duration,
            Delay = delay,
            Easing = easing,
            // Hold the start values through the delay so staggered controls don't flash in first.
            FillMode = FillMode.Both,
            Children = { start, end }
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not NewUserWalkthroughViewModel vm) return;

        if (e.Key == Key.Right && !vm.IsLastStep)
        {
            vm.NextStepCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            vm.PreviousStepCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnStepClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WalkthroughStepItemViewModel step } && DataContext is NewUserWalkthroughViewModel vm)
        {
            vm.GoToStepCommand.Execute(step);
        }
    }

    private void OnLanguageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WalkthroughLanguageOptionViewModel choice } && DataContext is NewUserWalkthroughViewModel vm)
        {
            vm.SelectLanguageCommand.Execute(choice);
        }
    }

    private void OnSetAccentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string accentName } && DataContext is NewUserWalkthroughViewModel vm)
        {
            vm.Settings.SetAccentCommand.Execute(accentName);
        }
    }
}
