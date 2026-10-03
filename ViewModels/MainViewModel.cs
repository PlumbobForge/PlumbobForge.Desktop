using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Installer.Shared;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    public const int CurrentCacheOptimizationMilestone = 1;

    [ObservableProperty]
    private ObservableObject? _currentView;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentManagerActive))]
    [NotifyPropertyChangedFor(nameof(IsConfigurationsActive))]
    [NotifyPropertyChangedFor(nameof(IsHealthActive))]
    [NotifyPropertyChangedFor(nameof(IsToolsActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private string _activeTab = "ContentManager";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _hasDirtySets;

    [ObservableProperty]
    private TaskProgressModalViewModel? _activeProgressModal;

    [ObservableProperty]
    private bool _isUpdateToastVisible;

    [ObservableProperty]
    private string _updateToastVersion = string.Empty;

    [ObservableProperty]
    private string _updateToastMessage = string.Empty;

    [ObservableProperty]
    private bool _isCacheOptimizationToastVisible;

    public bool IsContentManagerActive => ActiveTab == "ContentManager";
    public bool IsConfigurationsActive => ActiveTab == "Configurations";
    public bool IsHealthActive => ActiveTab == "Health" || ActiveTab == "Tools";
    public bool IsToolsActive => ActiveTab == "Health" || ActiveTab == "Tools";
    public bool IsSettingsActive => ActiveTab == "Settings";

    public QuickSwitcherViewModel QuickSwitcher { get; }

    public MainViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        QuickSwitcher = serviceProvider.GetRequiredService<QuickSwitcherViewModel>();

        // Defer heavy content loading until after MainWindow renders its initial frame
        Dispatcher.UIThread.Post(() =>
        {
            NavigateToContentManager();
            _ = RefreshDirtyStateAsync();
            _ = CheckCacheOptimizationOnStartupAsync();
        }, DispatcherPriority.Loaded);

        _ = CheckForUpdatesOnStartupAsync();
    }

    private async Task CheckCacheOptimizationOnStartupAsync()
    {
        try
        {
            await Task.Delay(1000);

            string settingsPath = AppSettingsService.GetAppSettingsPath();
            if (!File.Exists(settingsPath)) return;

            string json = await File.ReadAllTextAsync(settingsPath);
            var node = JsonNode.Parse(json);
            var pfNode = node?["PlumbobForge"];
            if (pfNode == null) return;

            bool hasSeen = pfNode["HasSeenWalkthrough"]?.GetValue<bool>() ?? false;
            if (!hasSeen) return;

            int lastMilestone = pfNode["LastOptimizedCacheMilestone"]?.GetValue<int>() ?? 0;
            // Backward compatibility: If LastOptimizedCacheVersion was already recorded,
            // milestone 1 is already completed so the toast will not reappear when updating to future versions.
            if (lastMilestone == 0 && !string.IsNullOrEmpty(pfNode["LastOptimizedCacheVersion"]?.GetValue<string>()))
            {
                lastMilestone = 1;
            }

            if (lastMilestone < CurrentCacheOptimizationMilestone)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsCacheOptimizationToastVisible = true;
                });
            }
        }
        catch
        {
        }
    }

    private async Task MarkCacheOptimizedVersionAsync()
    {
        try
        {
            string settingsPath = AppSettingsService.GetAppSettingsPath();
            JsonObject root;
            if (File.Exists(settingsPath))
            {
                root = (JsonNode.Parse(await File.ReadAllTextAsync(settingsPath)) as JsonObject) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var pfNode = root["PlumbobForge"] as JsonObject;
            if (pfNode == null)
            {
                pfNode = new JsonObject();
                root["PlumbobForge"] = pfNode;
            }

            var asmVersion = typeof(MainViewModel).Assembly.GetName().Version;
            string currentVer = asmVersion != null ? asmVersion.ToString(3) : InstallerConstants.DisplayVersion;
            pfNode["LastOptimizedCacheVersion"] = currentVer;
            pfNode["LastOptimizedCacheMilestone"] = CurrentCacheOptimizationMilestone;

            var opts = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(settingsPath, root.ToJsonString(opts));
        }
        catch
        {
        }
    }

    [RelayCommand]
    public async Task DismissCacheOptimizationToastAsync()
    {
        IsCacheOptimizationToastVisible = false;
        await MarkCacheOptimizedVersionAsync();
    }

    [RelayCommand]
    public async Task ForceRebuildFromToastAsync()
    {
        IsCacheOptimizationToastVisible = false;
        await MarkCacheOptimizedVersionAsync();
        await ForceRebuildCacheAsync();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await Task.Delay(2500);
            UpdateService requiredService = _serviceProvider.GetRequiredService<UpdateService>();
            Version version = typeof(MainViewModel).Assembly.GetName().Version;
            string currentVersionString = version != null
                ? (version.Revision > 0 ? version.ToString(4) : version.ToString(3))
                : InstallerConstants.DisplayVersion;

            UpdateCheckResult result = await requiredService.CheckForUpdatesAsync(currentVersionString);
            if (result.IsUpdateAvailable)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateToastVersion = result.LatestVersion;
                    UpdateToastMessage = "PlumbobForge v" + result.LatestVersion + " is available!";
                    IsUpdateToastVisible = true;
                });
            }
        }
        catch
        {
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
        SettingsViewModel requiredService = _serviceProvider.GetRequiredService<SettingsViewModel>();
        if (!requiredService.IsUpdateAvailable)
        {
            await requiredService.CheckForUpdatesAsync();
        }
    }

    public async Task RefreshDirtyStateAsync()
    {
        try
        {
            using IServiceScope scope = _serviceProvider.CreateScope();
            bool dirty = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SetsEntities.AsNoTracking().AnyAsync((SetsEntity s) => s.Dirty);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HasDirtySets = dirty;
            });
        }
        catch
        {
        }
    }

    [RelayCommand]
    public async Task OpenQuickSwitcherAsync()
    {
        await QuickSwitcher.OpenAsync();
    }

    [RelayCommand]
    public async Task RebuildCacheAsync()
    {
        await _serviceProvider.GetRequiredService<ContentManagerViewModel>().RebuildCacheAsync();
        await RefreshDirtyStateAsync();
    }

    [RelayCommand]
    public async Task ForceRebuildCacheAsync()
    {
        await _serviceProvider.GetRequiredService<ContentManagerViewModel>().RebuildCacheAsync(forceRebuild: true);
        await RefreshDirtyStateAsync();
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
        ConfigurationsViewModel configurationsViewModel = (ConfigurationsViewModel)(CurrentView = _serviceProvider.GetRequiredService<ConfigurationsViewModel>());
        configurationsViewModel.LoadDataAsync();
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
        _serviceProvider.GetRequiredService<ContentManagerViewModel>().SelectSetById(setId, null);
    }

    public void NavigateToContentManagerAndSelectSet(long setId, long metaId)
    {
        NavigateToContentManager();
        try
        {
            _serviceProvider.GetRequiredService<ContentManagerViewModel>().SelectSetById(setId, metaId);
        }
        catch
        {
        }
    }

    public void NavigateToItem(long itemId, long setId)
    {
        NavigateToContentManager();
        _serviceProvider.GetRequiredService<ContentManagerViewModel>().SelectSetById(setId, itemId);
    }

    public void NavigateToConfiguration(long configId)
    {
        NavigateToConfigurations();
        ConfigurationsViewModel requiredService = _serviceProvider.GetRequiredService<ConfigurationsViewModel>();
        ConfigItemViewModel configItemViewModel = requiredService.Configurations.FirstOrDefault((ConfigItemViewModel c) => c.Id == configId);
        if (configItemViewModel != null)
        {
            requiredService.SelectedConfiguration = configItemViewModel;
        }
    }
}
