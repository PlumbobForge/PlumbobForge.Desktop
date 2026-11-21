using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _isClosingForced;

    // Last size and position while the window was neither maximized nor minimized.
    private Size _normalSize;
    private PixelPoint _normalPosition;
    private bool _hasNormalBounds;

    private ViewModels.MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        RestorePlacement();
        Opened += (_, _) => CaptureNormalBoundsLater();
        PositionChanged += (_, _) => CaptureNormalBoundsLater();
    }

    // Opens with the size, place and maximized state the window was closed with.
    private void RestorePlacement()
    {
        var saved = Services.WindowPlacementStore.Load();
        if (saved == null || saved.Width <= 0 || saved.Height <= 0) return;

        var position = new PixelPoint(saved.X, saved.Y);
        var screen = Screens?.All.FirstOrDefault(s => s.WorkingArea.Contains(position + new PixelPoint(40, 20)));
        double width = Math.Max(MinWidth, saved.Width);
        double height = Math.Max(MinHeight, saved.Height);

        if (screen != null)
        {
            // Never larger than the screen it opens on.
            width = Math.Min(width, screen.WorkingArea.Width / screen.Scaling);
            height = Math.Min(height, screen.WorkingArea.Height / screen.Scaling);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
        }
        // Otherwise that screen is gone: keep the size and open centred as usual.

        Width = width;
        Height = height;
        if (saved.IsMaximized) WindowState = WindowState.Maximized;
    }

    // Moving into maximized or minimized reports a position before the state changes,
    // so the bounds are read once the state has settled.
    private void CaptureNormalBoundsLater()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (WindowState != WindowState.Normal) return;
            _normalSize = ClientSize;
            _normalPosition = Position;
            _hasNormalBounds = true;
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void SavePlacement()
    {
        if (!_hasNormalBounds) return;
        Services.WindowPlacementStore.Save(new Services.WindowPlacement
        {
            Width = _normalSize.Width,
            Height = _normalSize.Height,
            X = _normalPosition.X,
            Y = _normalPosition.Y,
            IsMaximized = WindowState == WindowState.Maximized
        });
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.GuideHighlightRequested -= OnGuideHighlightRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = DataContext as ViewModels.MainViewModel;
        if (_vm != null)
        {
            _vm.GuideHighlightRequested += OnGuideHighlightRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.IsGuideOpen) && _vm?.IsGuideOpen == true)
            GuidePanel.FocusSearch();
    }

    private void OnGuideHighlightRequested(string id)
    {
        // Give a tab that just became visible a moment to lay out before looking for the control.
        Avalonia.Threading.DispatcherTimer.RunOnce(
            () => PlumbobForge.Desktop.Controls.GuideTarget.Highlight(this, id),
            System.TimeSpan.FromMilliseconds(180));
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty) CaptureNormalBoundsLater();
        if (change.Property == WindowStateProperty && MaximizeIcon != null)
        {
            MaximizeIcon.Kind = WindowState == WindowState.Maximized 
                ? PlumbobForge.Desktop.Controls.Icons.PackIconBoxIconsKind.RegularCopy 
                : PlumbobForge.Desktop.Controls.Icons.PackIconBoxIconsKind.RegularSquare;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && vm.ActiveProgressModal?.IsRunning == true)
        {
            return;
        }
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && vm.ActiveProgressModal?.IsRunning == true)
        {
            e.Cancel = true;
            return;
        }

        // Read the live flag: HasDirtySets can be stale right after switching to vault mode.
        bool vaultMode = App.Services?.GetService<Microsoft.Extensions.Options.IOptionsMonitor<PlumbobForge.Backend.Configuration.PlumbobForgeOptions>>()?.CurrentValue.EnableSingleSourceVault ?? false;
        if (!_isClosingForced && !vaultMode && DataContext is ViewModels.MainViewModel mainVm && mainVm.HasDirtySets)
        {
            e.Cancel = true;
            var closeAnyway = await DialogHelper.ShowPendingCacheWarningAsync(this);
            if (closeAnyway)
            {
                _isClosingForced = true;
                Close();
            }
            return;
        }

        SavePlacement();
        base.OnClosing(e);
    }
}
