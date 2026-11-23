using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _isClosingForced;

    private ViewModels.MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
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

        // A delete waiting behind Undo runs before the app goes, so closing never undoes it.
        if (App.Services?.GetService<Services.ActionToastService>() is { HasPendingCommit: true } toast)
        {
            e.Cancel = true;
            await toast.CommitPendingAsync();
            Close();
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

        base.OnClosing(e);
    }
}
