using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;

namespace PlumbobForge.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private ObservableObject _currentView = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentManagerActive))]
    [NotifyPropertyChangedFor(nameof(IsConfigurationsActive))]
    [NotifyPropertyChangedFor(nameof(IsHealthActive))]
    [NotifyPropertyChangedFor(nameof(IsToolsActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private string _activeTab = "ContentManager";

    public bool IsContentManagerActive => ActiveTab == "ContentManager";
    public bool IsConfigurationsActive => ActiveTab == "Configurations";
    public bool IsHealthActive => ActiveTab == "Health" || ActiveTab == "Tools";
    public bool IsToolsActive => ActiveTab == "Health" || ActiveTab == "Tools";
    public bool IsSettingsActive => ActiveTab == "Settings";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _hasDirtySets;

    [ObservableProperty]
    private bool _isUpdateToastVisible = false;

    [ObservableProperty]
    private string _updateToastVersion = string.Empty;

    [ObservableProperty]
    private string _updateToastMessage = string.Empty;

    public QuickSwitcherViewModel QuickSwitcher { get; }

    public MainViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        QuickSwitcher = serviceProvider.GetRequiredService<QuickSwitcherViewModel>();
        NavigateToContentManager();
        _ = RefreshDirtyStateAsync();
        _ = CheckForUpdatesOnStartupAsync();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            // Delay slightly to let MainWindow finish rendering
            await Task.Delay(2500);

            var updateService = _serviceProvider.GetRequiredService<Services.UpdateService>();
            var asmVersion = typeof(MainViewModel).Assembly.GetName().Version;
            string currentVersion = asmVersion != null ? $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}" : "2.0.0";

            var result = await updateService.CheckForUpdatesAsync(currentVersion);
            if (result.IsUpdateAvailable)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateToastVersion = result.LatestVersion;
                    UpdateToastMessage = $"PlumbobForge v{result.LatestVersion} is available!";
                    IsUpdateToastVisible = true;
                });
            }
        }
        catch
        {
            // Background check failure is non-blocking
        }
    }

    [RelayCommand]
    public void DismissUpdateToast()
    {
        IsUpdateToastVisible = false;
    }

    [RelayCommand]
    public async Task OpenUpdateSettingsAsync()
    {
        IsUpdateToastVisible = false;
        NavigateToSettings();
        var settingsVm = _serviceProvider.GetRequiredService<SettingsViewModel>();
        if (!settingsVm.IsUpdateAvailable)
        {
            await settingsVm.CheckForUpdatesAsync();
        }
    }

    public async Task RefreshDirtyStateAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dirty = await db.SetsEntities.AsNoTracking().AnyAsync(s => s.Dirty);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                HasDirtySets = dirty;
            });
        }
        catch
        {
            // Ignore temporary DB concurrency or startup state
        }
    }

    [RelayCommand]
    public async Task OpenQuickSwitcherAsync()
    {
        await QuickSwitcher.OpenAsync();
    }

    [RelayCommand]
    public void NavigateToContentManager()
    {
        ActiveTab = "ContentManager";
        CurrentView = _serviceProvider.GetRequiredService<ContentManagerViewModel>();
    }

    [RelayCommand]
    public void NavigateToConfigurations()
    {
        ActiveTab = "Configurations";
        CurrentView = _serviceProvider.GetRequiredService<ConfigurationsViewModel>();
    }

    [RelayCommand]
    public void NavigateToHealth()
    {
        ActiveTab = "Tools";
        CurrentView = _serviceProvider.GetRequiredService<HealthViewModel>();
    }

    [RelayCommand]
    public void NavigateToTools()
    {
        ActiveTab = "Tools";
        CurrentView = _serviceProvider.GetRequiredService<HealthViewModel>();
    }

    [RelayCommand]
    public void NavigateToSettings()
    {
        ActiveTab = "Settings";
        CurrentView = _serviceProvider.GetRequiredService<SettingsViewModel>();
    }

    public void NavigateToSet(long setId)
    {
        NavigateToContentManager();
        var contentVm = _serviceProvider.GetRequiredService<ContentManagerViewModel>();
        contentVm.SelectSetById(setId);
    }

    public void NavigateToContentManagerAndSelectSet(long setId, long metaId)
    {
        NavigateToContentManager();
        try
        {
            var contentVm = _serviceProvider.GetRequiredService<ContentManagerViewModel>();
            contentVm.SelectSetById(setId);
        }
        catch { }
    }

    public void NavigateToConfiguration(long configId)
    {
        NavigateToConfigurations();
        var configVm = _serviceProvider.GetRequiredService<ConfigurationsViewModel>();
        var target = configVm.Configurations.FirstOrDefault(c => c.Id == configId);
        if (target != null)
        {
            configVm.SelectedConfiguration = target;
        }
    }

    public void NavigateToItem(long setId, long itemId)
    {
        NavigateToContentManager();
        var contentVm = _serviceProvider.GetRequiredService<ContentManagerViewModel>();
        contentVm.SelectSetById(setId, itemId);
    }

    [ObservableProperty]
    private bool _isRebuildingCache;

    [ObservableProperty]
    private TaskProgressModalViewModel? _activeProgressModal;

    [RelayCommand]
    public async Task RebuildCacheAsync()
    {
        if (IsRebuildingCache || ActiveProgressModal?.IsRunning == true) return;
        var contentVm = _serviceProvider.GetRequiredService<ContentManagerViewModel>();
        await contentVm.RebuildCacheAsync(forceRebuild: false);
        await RefreshDirtyStateAsync();
    }

    [RelayCommand]
    public async Task ForceRebuildCacheAsync()
    {
        if (IsRebuildingCache || ActiveProgressModal?.IsRunning == true) return;
        var contentVm = _serviceProvider.GetRequiredService<ContentManagerViewModel>();
        await contentVm.RebuildCacheAsync(forceRebuild: true);
        await RefreshDirtyStateAsync();
    }
}
