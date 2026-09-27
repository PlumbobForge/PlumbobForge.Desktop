using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _isClosingForced;

    public MainWindow()
    {
        InitializeComponent();
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
                ? IconPacks.Avalonia.BoxIcons.PackIconBoxIconsKind.RegularCopy 
                : IconPacks.Avalonia.BoxIcons.PackIconBoxIconsKind.RegularSquare;
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

        if (!_isClosingForced && DataContext is ViewModels.MainViewModel mainVm && mainVm.HasDirtySets)
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
