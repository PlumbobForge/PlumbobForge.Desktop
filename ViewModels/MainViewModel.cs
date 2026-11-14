using System;
using System.Collections.ObjectModel;
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
using PlumbobForge.Desktop.Controls.Icons;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    // 2: compression removed; libraries compressed by older versions need a one-time repair + rebuild.
    public const int CurrentCacheOptimizationMilestone = 2;

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
    private bool _isVaultActive;

    [ObservableProperty]
    private string _vaultStatusText = "Vault is up-to-date";

    public void SetVaultUpdating(bool isUpdating, string? customText = null)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            VaultStatusText = isUpdating
                ? (customText ?? LocalizationManager.Instance.GetString("progress.vault_status_updating"))
                : LocalizationManager.Instance.GetString("progress.vault_status_uptodate");
        });
    }

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

    // Title-bar tabs, in display order.
    public ObservableCollection<NavTabViewModel> NavTabs { get; }

    partial void OnActiveTabChanged(string value) => RefreshNavTabs();

    public void RefreshNavTabs()
    {
        foreach (var tab in NavTabs) tab.RefreshIsActive();
    }

    public QuickSwitcherViewModel QuickSwitcher { get; }
    public ContentManagerViewModel ContentManagerVm { get; }
    public ConfigurationsViewModel ConfigurationsVm { get; }
    public HealthViewModel HealthVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public MainViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        QuickSwitcher = serviceProvider.GetRequiredService<QuickSwitcherViewModel>();
        ContentManagerVm = serviceProvider.GetRequiredService<ContentManagerViewModel>();
        ConfigurationsVm = serviceProvider.GetRequiredService<ConfigurationsViewModel>();
        HealthVm = serviceProvider.GetRequiredService<HealthViewModel>();
        SettingsVm = serviceProvider.GetRequiredService<SettingsViewModel>();

        NavTabs = new ObservableCollection<NavTabViewModel>
        {
            new("library", "nav.library", PackIconBoxIconsKind.RegularFolder, NavigateToContentManagerCommand, () => IsContentManagerActive),
            new("configurations", "nav.configurations", PackIconBoxIconsKind.RegularSliderAlt, NavigateToConfigurationsCommand, () => IsConfigurationsActive),
            new("tools", "nav.tools", PackIconBoxIconsKind.RegularSpanner, NavigateToToolsCommand, () => IsToolsActive),
            new("settings", "nav.settings", PackIconBoxIconsKind.RegularCog, NavigateToSettingsCommand, () => IsSettingsActive),
        };
        RefreshNavTabs();

        VaultStatusText = LocalizationManager.Instance.GetString("progress.vault_status_uptodate");
        LocalizationManager.Instance.LanguageChanged += _ =>
        {
            VaultStatusText = LocalizationManager.Instance.GetString("progress.vault_status_uptodate");
        };

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

    // "Later" only hides the toast for this session so the repair is offered again next launch.
    [RelayCommand]
    public void HideCacheOptimizationToast()
    {
        IsCacheOptimizationToastVisible = false;
    }

    [RelayCommand]
    public async Task ForceRebuildFromToastAsync()
    {
        IsCacheOptimizationToastVisible = false;
        // Only record the milestone once repair and rebuild succeed, so a failed run is offered again.
        await _serviceProvider.GetRequiredService<ContentManagerViewModel>()
            .RepairCompressionAndRebuildAsync(onSucceeded: MarkCacheOptimizedVersionAsync);
        await RefreshDirtyStateAsync();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await Task.Delay(2500);
            UpdateService requiredService = _serviceProvider.GetRequiredService<UpdateService>();
            Version? version = typeof(MainViewModel).Assembly.GetName().Version;
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
            var options = scope.ServiceProvider.GetService<Microsoft.Extensions.Options.IOptionsMonitor<PlumbobForge.Backend.Configuration.PlumbobForgeOptions>>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            bool hasVaultShards = await db.VaultShards.AnyAsync();
            bool vaultActive = (options?.CurrentValue?.EnableSingleSourceVault ?? false) || hasVaultShards;

            if (hasVaultShards && options?.CurrentValue != null && !options.CurrentValue.EnableSingleSourceVault)
            {
                options.CurrentValue.EnableSingleSourceVault = true;
                _ = AppSettingsService.SaveOptionsAsync(options.CurrentValue);
            }

            // Dirty flags only mean "rebuild pending" in library mode; the vault never rebuilds.
            bool dirty = !vaultActive && await db.SetsEntities.AsNoTracking().AnyAsync((SetsEntity s) => s.Dirty);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HasDirtySets = dirty;
                IsVaultActive = vaultActive;
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
    public async Task OpenFeedbackAsync()
    {
        var options = _serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlumbobForge.Backend.Configuration.PlumbobForgeOptions>>().Value;
        var gameLogs = await Task.Run(() =>
        {
            try
            {
                var sims3Folder = PlumbobForge.Backend.Services.Sims3PathResolver.ResolveSims3UserFolder(options.Sims3UserDir);
                return FeedbackService.FindRecentGameLogs(sims3Folder);
            }
            catch
            {
                return (System.Collections.Generic.IReadOnlyList<string>)Array.Empty<string>();
            }
        });
        await Views.Dialogs.DialogHelper.ShowFeedbackAsync(gameLogs);
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
        CurrentView = ContentManagerVm;
    }

    [RelayCommand]
    public void NavigateToConfigurations()
    {
        ActiveTab = "Configurations";
        CurrentView = ConfigurationsVm;
        _ = ConfigurationsVm.LoadDataAsync();
    }

    [RelayCommand]
    public void NavigateToHealth()
    {
        ActiveTab = "Tools";
        CurrentView = HealthVm;
    }

    [RelayCommand]
    public void NavigateToTools()
    {
        ActiveTab = "Tools";
        CurrentView = HealthVm;
    }

    [RelayCommand]
    public void NavigateToSettings()
    {
        ActiveTab = "Settings";
        CurrentView = SettingsVm;
    }

    public void NavigateToSet(long setId)
    {
        NavigateToContentManager();
        ContentManagerVm.SelectSetById(setId, null);
    }

    public void NavigateToContentManagerAndSelectSet(long setId, long metaId)
    {
        NavigateToContentManager();
        try
        {
            ContentManagerVm.SelectSetById(setId, metaId);
        }
        catch
        {
        }
    }

    public void NavigateToItem(long itemId, long? setId = null)
    {
        NavigateToContentManager();
        try
        {
            if (setId.HasValue)
            {
                ContentManagerVm.SelectSetById(setId.Value, itemId);
            }
            else
            {
                ContentManagerVm.SelectAllItemsAndSelectItem(itemId);
            }
        }
        catch
        {
        }
    }

    public void NavigateToConfiguration(long configId)
    {
        NavigateToConfigurations();
        ConfigurationsViewModel requiredService = _serviceProvider.GetRequiredService<ConfigurationsViewModel>();
        ConfigItemViewModel? configItemViewModel = requiredService.Configurations.FirstOrDefault((ConfigItemViewModel c) => c.Id == configId);
        if (configItemViewModel != null)
        {
            requiredService.SelectedConfiguration = configItemViewModel;
        }
    }

    public void NavigateToToolSection(string section)
    {
        NavigateToTools();
        HealthVm.SelectSection(section);
    }

    public void NavigateToSettingsSection(string section)
    {
        NavigateToSettings();
        SettingsVm.ScrollToAndHighlightSection(section);
    }
}
