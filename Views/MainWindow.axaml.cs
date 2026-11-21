using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Desktop.Controls.Icons;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _isClosingForced;
    private bool _playStarting;

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

    // Starts the active configuration. With none active there is nothing to start, so it opens the list.
    private async void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        // A double click must not start the game twice.
        if (_playStarting || DataContext is not ViewModels.MainViewModel vm) return;

        _playStarting = true;
        try
        {
            if (vm.ConfigurationsVm.Configurations.Count == 0) await vm.ConfigurationsVm.LoadDataAsync();
            var active = vm.ConfigurationsVm.Configurations.FirstOrDefault(c => c.Active);
            if (active != null) await vm.PlayAsync(this, active.Id);
            else ShowConfigMenu(vm);
        }
        catch (Exception ex)
        {
            await DialogHelper.ShowErrorAsync(LocalizationManager.Instance.GetString("play.title"), ex.Message, this);
        }
        finally
        {
            _playStarting = false;
        }
    }

    private async void OnConfigMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;

        try
        {
            if (vm.ConfigurationsVm.Configurations.Count == 0) await vm.ConfigurationsVm.LoadDataAsync();
            ShowConfigMenu(vm);
        }
        catch (Exception ex)
        {
            await DialogHelper.ShowErrorAsync(LocalizationManager.Instance.GetString("play.title"), ex.Message, this);
        }
    }

    private void ShowConfigMenu(ViewModels.MainViewModel vm)
    {
        FlyoutBase.SetAttachedFlyout(ConfigMenuButton, BuildConfigMenu(vm));
        FlyoutBase.ShowAttachedFlyout(ConfigMenuButton);
    }

    // Picking a configuration only activates it; the game starts from the Play button.
    private MenuFlyout BuildConfigMenu(ViewModels.MainViewModel vm)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };

        foreach (var config in vm.ConfigurationsVm.Configurations)
        {
            long id = config.Id;
            var item = new MenuItem
            {
                Header = config.Active ? LocalizationManager.Instance.GetString("play.active_suffix", config.Name) : config.Name,
                FontWeight = config.Active ? FontWeight.SemiBold : FontWeight.Normal,
                Icon = new PackIconBoxIcons { Kind = config.IconKind, Foreground = config.IconBrush, Width = 15, Height = 15 }
            };
            item.Click += async (_, _) => await SelectConfigurationAsync(vm, id);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = LocalizationManager.Instance.GetString("play.program_settings") };
        settings.Click += (_, _) => vm.NavigateToSettingsSection("Storage");
        menu.Items.Add(settings);
        return menu;
    }

    private async Task SelectConfigurationAsync(ViewModels.MainViewModel vm, long configurationId)
    {
        try
        {
            await vm.SelectConfigurationAsync(this, configurationId);
        }
        catch (Exception ex)
        {
            await DialogHelper.ShowErrorAsync(LocalizationManager.Instance.GetString("play.title"), ex.Message, this);
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
