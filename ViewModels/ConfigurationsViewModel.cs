using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ConfigurationsViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AppDbContext _db;
    private readonly PKGManager _pkgManager;
    private readonly PlumbobForgeOptions _options;

    [ObservableProperty]
    private ConfigItemViewModel? _selectedConfiguration;

    [ObservableProperty]
    private bool _isSidebarCollapsed = false;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _configSortMode = "Date"; // "Date", "NameAsc", "NameDesc"

    [ObservableProperty]
    private string _setSortMode = "Date"; // "Date", "NameAsc", "NameDesc", "SubsetsDesc"

    [ObservableProperty]
    private int _selectedSetsCount = 0;

    [ObservableProperty]
    private bool _hasSelectedSets = false;

    // Toast Alert
    [ObservableProperty]
    private string _statusToastMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusToastVisible = false;

    [ObservableProperty]
    private bool _isStatusToastError = false;

    // Sub-Set Cascade Modal State
    [ObservableProperty]
    private bool _isCascadeModalVisible = false;

    [ObservableProperty]
    private string _cascadeModalTitle = string.Empty;

    [ObservableProperty]
    private string _cascadeModalMessage = string.Empty;

    private List<long> _pendingTargetSetIds = new();
    private List<long> _pendingDescendantSetIds = new();
    private bool _pendingTargetToEnabled = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEnabledSetsSearchQuery))]
    private string _enabledSetsSearchQuery = string.Empty;

    public bool HasEnabledSetsSearchQuery => !string.IsNullOrWhiteSpace(EnabledSetsSearchQuery);

    partial void OnEnabledSetsSearchQueryChanged(string value)
    {
        RefreshSetsColumns();
    }

    [RelayCommand]
    public void ClearEnabledSetsSearch() => EnabledSetsSearchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisabledSetsSearchQuery))]
    private string _disabledSetsSearchQuery = string.Empty;

    public bool HasDisabledSetsSearchQuery => !string.IsNullOrWhiteSpace(DisabledSetsSearchQuery);

    partial void OnDisabledSetsSearchQueryChanged(string value)
    {
        RefreshSetsColumns();
    }

    [RelayCommand]
    public void ClearDisabledSetsSearch() => DisabledSetsSearchQuery = string.Empty;

    public ObservableCollection<ConfigItemViewModel> Configurations { get; } = new();
    public ObservableCollection<ConfigSetItemViewModel> EnabledSets { get; } = new();
    public ObservableCollection<ConfigSetItemViewModel> DisabledSets { get; } = new();

    private List<SetsEntity> _allSets = new();
    private Dictionary<long, SetsEntity> _setsById = new();

    public int TotalSetsCount => _allSets.Count;
    public int EnabledSetsCount => EnabledSets.Count;
    public int DisabledSetsCount => DisabledSets.Count;

    public string SetSortModeLabel => SetSortMode switch
    {
        "NameAsc" => LocalizationManager.Instance.GetString("library.sort_name_asc"),
        "NameDesc" => LocalizationManager.Instance.GetString("library.sort_name_desc"),
        "SubsetsDesc" => LocalizationManager.Instance.GetString("library.sort_subsets_count"),
        _ => LocalizationManager.Instance.GetString("library.sort_date")
    };

    private Guid? _notificationSubscriptionId;

    public ConfigurationsViewModel(
        IServiceProvider serviceProvider,
        AppDbContext db,
        PKGManager pkgManager,
        IOptions<PlumbobForgeOptions> options)
    {
        _serviceProvider = serviceProvider;
        _db = db;
        _pkgManager = pkgManager;
        _options = options.Value ?? new PlumbobForgeOptions();

        LocalizationManager.Instance.LanguageChanged += _ =>
        {
            OnPropertyChanged(nameof(SetSortModeLabel));
        };

        var notifier = _serviceProvider.GetService<NotificationService>();
        if (notifier != null)
        {
            _notificationSubscriptionId = notifier.Subscribe(async payload =>
            {
                if (payload.Contains("sets_changed") || payload.Contains("library_changed") || payload.Contains("items_imported"))
                {
                    await LoadDataAsync();
                }
            });
        }

        _ = LoadDataAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync() => await LoadDataAsync();

    public async Task LoadDataAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(LoadDataAsync);
            return;
        }

        IsLoading = true;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var rawConfigs = await db.ConfigEntities
                .Include(c => c.ConfigSetsEntities)
                .AsNoTracking()
                .ToListAsync();

            _allSets = await db.SetsEntities
                .AsNoTracking()
                .ToListAsync();

            _setsById = _allSets.ToDictionary(s => s.Id);

            long? currentSelectedId = SelectedConfiguration?.Id;

            Configurations.Clear();
            foreach (var raw in rawConfigs)
            {
                Configurations.Add(new ConfigItemViewModel(raw));
            }

            SortConfigurationsList();

            if (currentSelectedId.HasValue)
            {
                var match = Configurations.FirstOrDefault(c => c.Id == currentSelectedId.Value);
                SelectedConfiguration = match ?? Configurations.FirstOrDefault(c => c.Active) ?? Configurations.FirstOrDefault();
            }
            else
            {
                SelectedConfiguration = Configurations.FirstOrDefault(c => c.Active) ?? Configurations.FirstOrDefault();
            }

            foreach (var c in Configurations)
            {
                c.IsSelected = (c == SelectedConfiguration);
            }

            RefreshSetsColumns();
        }
        catch (Exception ex)
        {
            ShowStatusToast($"Failed to load configurations: {ex.Message}", isError: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedConfigurationChanged(ConfigItemViewModel? value)
    {
        foreach (var c in Configurations)
        {
            c.IsSelected = (c == value);
        }
        RefreshSetsColumns();
    }

    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    [RelayCommand]
    public void SetConfigSortMode(string mode)
    {
        ConfigSortMode = mode;
        SortConfigurationsList();
    }

    [RelayCommand]
    public void SetSetSortMode(string mode)
    {
        SetSortMode = mode;
        OnPropertyChanged(nameof(SetSortModeLabel));
        RefreshSetsColumns();
    }

    private void SortConfigurationsList()
    {
        var list = Configurations.ToList();
        list = ConfigSortMode switch
        {
            "NameAsc" => list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "NameDesc" => list.OrderByDescending(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => list.OrderBy(c => c.Id).ToList()
        };

        Configurations.Clear();
        foreach (var item in list)
        {
            Configurations.Add(item);
        }
    }

    private void RefreshSetsColumns()
    {
        if (SelectedConfiguration == null)
        {
            EnabledSets.Clear();
            DisabledSets.Clear();
            OnPropertyChanged(nameof(TotalSetsCount));
            OnPropertyChanged(nameof(EnabledSetsCount));
            OnPropertyChanged(nameof(DisabledSetsCount));
            return;
        }

        var activeSetIds = SelectedConfiguration.SetIds.ToHashSet();

        var enabledList = new List<ConfigSetItemViewModel>();
        var disabledList = new List<ConfigSetItemViewModel>();

        foreach (var set in _allSets)
        {
            string breadcrumb = BuildBreadcrumb(set);
            int descCount = GetDescendantCount(set.Id);
            bool isEnabled = activeSetIds.Contains(set.Id);

            var vm = new ConfigSetItemViewModel(set, breadcrumb, descCount, isEnabled);
            if (isEnabled)
            {
                enabledList.Add(vm);
            }
            else
            {
                disabledList.Add(vm);
            }
        }

        enabledList = SortSetsList(enabledList);
        disabledList = SortSetsList(disabledList);

        if (!string.IsNullOrWhiteSpace(EnabledSetsSearchQuery))
        {
            var q = EnabledSetsSearchQuery.Trim();
            enabledList = enabledList.Where(s =>
                s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(s.BreadcrumbPath) && s.BreadcrumbPath.Contains(q, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        if (!string.IsNullOrWhiteSpace(DisabledSetsSearchQuery))
        {
            var q = DisabledSetsSearchQuery.Trim();
            disabledList = disabledList.Where(s =>
                s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(s.BreadcrumbPath) && s.BreadcrumbPath.Contains(q, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        EnabledSets.Clear();
        foreach (var item in enabledList) EnabledSets.Add(item);

        DisabledSets.Clear();
        foreach (var item in disabledList) DisabledSets.Add(item);

        UpdateSelectionState();
        OnPropertyChanged(nameof(TotalSetsCount));
        OnPropertyChanged(nameof(EnabledSetsCount));
        OnPropertyChanged(nameof(DisabledSetsCount));
    }

    private List<ConfigSetItemViewModel> SortSetsList(List<ConfigSetItemViewModel> list)
    {
        return SetSortMode switch
        {
            "NameAsc" => list.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "NameDesc" => list.OrderByDescending(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "SubsetsDesc" => list.OrderByDescending(s => s.DescendantCount).ThenBy(s => s.Name).ToList(),
            _ => list.OrderBy(s => s.Id).ToList()
        };
    }

    private string BuildBreadcrumb(SetsEntity set)
    {
        var ancestors = new List<string>();
        long? currentParentId = set.ParentSetsEntityId;
        var visited = new HashSet<long>();

        while (currentParentId.HasValue && !visited.Contains(currentParentId.Value))
        {
            visited.Add(currentParentId.Value);
            if (_setsById.TryGetValue(currentParentId.Value, out var parent))
            {
                ancestors.Insert(0, parent.Name);
                currentParentId = parent.ParentSetsEntityId;
            }
            else
            {
                break;
            }
        }

        return ancestors.Count > 0 ? string.Join("  ›  ", ancestors) : string.Empty;
    }

    private List<long> GetAllDescendantIds(long setId)
    {
        var descendants = new List<long>();
        var queue = new Queue<long>();
        var visited = new HashSet<long> { setId };
        queue.Enqueue(setId);

        while (queue.Count > 0)
        {
            long parentId = queue.Dequeue();
            var children = _allSets.Where(s => s.ParentSetsEntityId == parentId);
            foreach (var child in children)
            {
                if (!visited.Contains(child.Id))
                {
                    visited.Add(child.Id);
                    descendants.Add(child.Id);
                    queue.Enqueue(child.Id);
                }
            }
        }

        return descendants;
    }

    private int GetDescendantCount(long setId) => GetAllDescendantIds(setId).Count;

    [RelayCommand]
    public async Task SelectConfiguration(ConfigItemViewModel? config)
    {
        if (config == null) return;
        bool isChanging = SelectedConfiguration?.Id != config.Id;
        SelectedConfiguration = config;

        if (isChanging && !config.Active)
        {
            await ActivateConfigurationAsync(config);
        }
    }

    [RelayCommand]
    public async Task CreateConfigurationAsync()
    {
        var name = await DialogHelper.ShowInputAsync("New Configuration", "Enter name for new configuration:", "New Configuration");
        if (string.IsNullOrWhiteSpace(name)) return;

        if (!PlumbobForge.Desktop.Utils.NameValidator.IsValidName(name, out var createError))
        {
            ShowStatusToast(createError ?? "Invalid configuration name.", isError: true);
            return;
        }

        try
        {
            var config = new ConfigEntity
            {
                Name = name.Trim(),
                Active = false,
                Default = false
            };
            _db.ConfigEntities.Add(config);
            await _db.SaveChangesAsync();

            // Enable all sets by default
            foreach (var set in _allSets)
            {
                config.ConfigSetsEntities.Add(new ConfigSetsEntity
                {
                    ConfigEntityId = config.Id,
                    SetsEntityId = set.Id
                });
            }
            await _db.SaveChangesAsync();

            await LoadDataAsync();
            var created = Configurations.FirstOrDefault(c => c.Id == config.Id);
            if (created != null) SelectedConfiguration = created;

            ShowStatusToast($"Configuration '{name.Trim()}' created!", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatusToast($"Failed to create configuration: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task DuplicateConfigurationAsync(ConfigItemViewModel? config)
    {
        if (config == null) return;

        try
        {
            string baseName = $"{config.Name} (Copy)";
            string newName = baseName;
            int copyIndex = 1;
            while (await _db.ConfigEntities.AnyAsync(c => c.Name == newName))
            {
                copyIndex++;
                newName = $"{config.Name} (Copy {copyIndex})";
            }

            var duplicate = new ConfigEntity
            {
                Name = newName,
                Description = config.Description,
                Active = false,
                Default = false
            };
            _db.ConfigEntities.Add(duplicate);
            await _db.SaveChangesAsync();

            foreach (var setId in config.SetIds)
            {
                duplicate.ConfigSetsEntities.Add(new ConfigSetsEntity
                {
                    ConfigEntityId = duplicate.Id,
                    SetsEntityId = setId
                });
            }
            await _db.SaveChangesAsync();

            await LoadDataAsync();
            var created = Configurations.FirstOrDefault(c => c.Id == duplicate.Id);
            if (created != null) SelectedConfiguration = created;

            ShowStatusToast($"Duplicated as '{newName}'!", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatusToast($"Failed to duplicate configuration: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task CustomizeConfigurationAsync(ConfigItemViewModel? config)
    {
        if (config == null) return;

        var entity = await _db.ConfigEntities.FindAsync(config.Id);
        if (entity == null) return;

        var result = await DialogHelper.ShowCustomizeConfigAsync(
            entity.Name,
            entity.Description,
            entity.Icon,
            entity.Color,
            entity.Default,
            entity.Active
        );

        if (result == null || !result.Confirmed) return;

        bool hasChanged = false;

        if (!entity.Default && !string.IsNullOrWhiteSpace(result.Name) && result.Name.Trim() != entity.Name)
        {
            if (!PlumbobForge.Desktop.Utils.NameValidator.IsValidName(result.Name, out var nameErr))
            {
                ShowStatusToast(nameErr ?? "Invalid configuration name.", isError: true);
                return;
            }

            entity.Name = result.Name.Trim();
            config.Name = entity.Name;
            hasChanged = true;
        }

        if (result.Description != entity.Description)
        {
            entity.Description = string.IsNullOrWhiteSpace(result.Description) ? null : result.Description.Trim();
            config.Description = entity.Description;
            hasChanged = true;
        }

        if (result.Icon != entity.Icon)
        {
            entity.Icon = result.Icon;
            config.Icon = result.Icon ?? "RegularSlider";
            hasChanged = true;
        }

        if (result.Color != entity.Color)
        {
            entity.Color = result.Color;
            config.Color = result.Color;
            hasChanged = true;
        }

        if (hasChanged)
        {
            try
            {
                await _db.SaveChangesAsync();
                config.RefreshEntity();
                ShowStatusToast($"Configuration '{config.Name}' updated!", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatusToast($"Failed to update configuration: {ex.Message}", isError: true);
            }
        }
    }

    [RelayCommand]
    public Task RenameConfigurationAsync(ConfigItemViewModel? config) => CustomizeConfigurationAsync(config);

    [RelayCommand]
    public Task EditDescriptionAsync(ConfigItemViewModel? config) => CustomizeConfigurationAsync(config);

    [RelayCommand]
    public async Task DeleteConfigurationAsync(ConfigItemViewModel? config)
    {
        if (config == null || config.Default || config.Active) return;

        var confirm = await DialogHelper.ShowDeleteConfirmAsync($"Are you sure you want to delete configuration '{config.Name}'?");
        if (!confirm.Confirmed) return;

        try
        {
            var entity = await _db.ConfigEntities.FindAsync(config.Id);
            if (entity != null)
            {
                _db.ConfigEntities.Remove(entity);
                await _db.SaveChangesAsync();
                await LoadDataAsync();
                ShowStatusToast($"Configuration '{config.Name}' deleted.", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowStatusToast($"Failed to delete configuration: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task ActivateConfigurationAsync(ConfigItemViewModel? config)
    {
        if (config == null || config.Active) return;

        bool isStatic = string.Equals(_options.CacheMethod, "Static", StringComparison.OrdinalIgnoreCase);
        bool shouldAutoRebuild = isStatic ? _options.AutoRebuildStaticCache : true;

        if (isStatic && !shouldAutoRebuild)
        {
            try
            {
                var allConfigs = await _db.ConfigEntities.ToListAsync();
                foreach (var c in allConfigs)
                {
                    c.Active = (c.Id == config.Id);
                }

                // In static mode without auto-rebuild, mark all sets dirty so user can rebuild when ready
                var allSets = await _db.SetsEntities.ToListAsync();
                foreach (var s in allSets)
                {
                    s.Dirty = true;
                }

                await _db.SaveChangesAsync();

                foreach (var c in Configurations)
                {
                    c.Active = (c.Id == config.Id);
                }
                config.Active = true;

                _ = App.Services?.GetService<MainViewModel>()?.RefreshDirtyStateAsync();
                ShowStatusToast($"'{config.Name}' is now active. Rebuild cache when ready.", isError: false);
            }
            catch (Exception ex)
            {
                ShowStatusToast($"Failed to activate configuration: {ex.Message}", isError: true);
            }
            return;
        }

        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel();
        modal.Start(LocalizationManager.Instance.GetString("progress.activating_config", config.Name));

        if (mainVm != null && isStatic) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            await Task.Run(async () =>
            {
                modal.StartStep("update_db", LocalizationManager.Instance.GetString("progress.updating_active_config"), progress: 0.2);

                var allConfigs = await _db.ConfigEntities.ToListAsync();
                foreach (var c in allConfigs)
                {
                    c.Active = (c.Id == config.Id);
                }

                if (isStatic)
                {
                    // For static caching, mark all sets dirty to rebuild the bundle
                    var allSets = await _db.SetsEntities.ToListAsync();
                    foreach (var s in allSets)
                    {
                        s.Dirty = true;
                    }
                }

                await _db.SaveChangesAsync();
                modal.CompleteStep("update_db", finalBadge: LocalizationManager.Instance.GetString("progress.badge_updated"));

                // Sync to The Sims 3
                modal.StartStep("sync_sims3", isStatic
                    ? LocalizationManager.Instance.GetString("progress.rebuilding_static_bundle")
                    : LocalizationManager.Instance.GetString("progress.updating_game_configs"), progress: 0.6);
                await _pkgManager.SyncToSims3Async(forceRebuildStatic: isStatic);
                modal.CompleteStep("sync_sims3", finalBadge: LocalizationManager.Instance.GetString("progress.badge_synchronized"));
            });

            foreach (var c in Configurations)
            {
                c.Active = (c.Id == config.Id);
            }
            config.Active = true;

            modal.Finish(success: true);
            _ = App.Services?.GetService<MainViewModel>()?.RefreshDirtyStateAsync();
            ShowStatusToast($"'{config.Name}' is now the active configuration. Game files updated!", isError: false);
        }
        catch (Exception ex)
        {
            modal.Finish(success: false);
            ShowStatusToast($"Failed to activate configuration: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task ToggleSetEnabled(ConfigSetItemViewModel? set)
    {
        if (set == null || SelectedConfiguration == null) return;

        bool targetToEnabled = !set.IsEnabledInConfig;
        var targetSetIds = GetSelectedTargetSetIds(set);

        await RequestMoveSetsAsync(targetSetIds, targetToEnabled);
    }

    public async Task RequestMoveSetsAsync(List<long> targetSetIds, bool targetToEnabled)
    {
        if (SelectedConfiguration == null || targetSetIds == null || targetSetIds.Count == 0) return;

        // Check for child sub-sets
        var descendants = new HashSet<long>();
        foreach (var id in targetSetIds)
        {
            foreach (var d in GetAllDescendantIds(id))
            {
                if (!targetSetIds.Contains(d))
                {
                    descendants.Add(d);
                }
            }
        }

        if (descendants.Count > 0)
        {
            _pendingTargetSetIds = targetSetIds;
            _pendingDescendantSetIds = descendants.ToList();
            _pendingTargetToEnabled = targetToEnabled;

            string firstSetName = _setsById.TryGetValue(targetSetIds[0], out var s) ? s.Name : "Set";

            CascadeModalTitle = "Move Sub-Sets?";
            CascadeModalMessage = targetSetIds.Count == 1
                ? $"The set '{firstSetName}' has {descendants.Count} sub-set(s). Would you like to also move its sub-sets to {(targetToEnabled ? "Enabled" : "Disabled")}?"
                : $"{targetSetIds.Count} sets have {descendants.Count} sub-set(s). Would you like to also move their sub-sets to {(targetToEnabled ? "Enabled" : "Disabled")}?";

            IsCascadeModalVisible = true;
            return;
        }

        await ApplyMoveSetsAsync(targetSetIds, targetToEnabled);
    }

    private List<long> GetSelectedTargetSetIds(ConfigSetItemViewModel clickedSet)
    {
        var allSelectedInColumn = (clickedSet.IsEnabledInConfig ? EnabledSets : DisabledSets)
            .Where(s => s.IsSelected)
            .Select(s => s.Id)
            .ToList();

        if (allSelectedInColumn.Contains(clickedSet.Id) && allSelectedInColumn.Count > 1)
        {
            return allSelectedInColumn;
        }

        return new List<long> { clickedSet.Id };
    }

    [RelayCommand]
    public async Task CascadeIncludeSubSetsAsync()
    {
        IsCascadeModalVisible = false;
        var allIds = new HashSet<long>(_pendingTargetSetIds);
        foreach (var d in _pendingDescendantSetIds) allIds.Add(d);

        await ApplyMoveSetsAsync(allIds.ToList(), _pendingTargetToEnabled);
    }

    [RelayCommand]
    public async Task CascadeSingleSetOnlyAsync()
    {
        IsCascadeModalVisible = false;
        await ApplyMoveSetsAsync(_pendingTargetSetIds, _pendingTargetToEnabled);
    }

    [RelayCommand]
    public void CascadeCancel()
    {
        IsCascadeModalVisible = false;
    }

    public async Task ApplyMoveSetsAsync(List<long> setIds, bool toEnabled)
    {
        if (SelectedConfiguration == null || setIds.Count == 0) return;

        var config = SelectedConfiguration;
        var currentSetIds = config.SetIds.ToHashSet();

        if (toEnabled)
        {
            foreach (var id in setIds) currentSetIds.Add(id);
        }
        else
        {
            foreach (var id in setIds) currentSetIds.Remove(id);
        }

        config.SetIds = currentSetIds.ToList();

        try
        {
            var configEntity = await _db.ConfigEntities
                .Include(c => c.ConfigSetsEntities)
                .FirstOrDefaultAsync(c => c.Id == config.Id);

            if (configEntity != null)
            {
                configEntity.ConfigSetsEntities.Clear();
                foreach (var id in config.SetIds)
                {
                    configEntity.ConfigSetsEntities.Add(new ConfigSetsEntity
                    {
                        ConfigEntityId = config.Id,
                        SetsEntityId = id
                    });
                }
                await _db.SaveChangesAsync();

                if (config.Active)
                {
                    bool isStatic = string.Equals(_options.CacheMethod, "Static", StringComparison.OrdinalIgnoreCase);
                    if (isStatic)
                    {
                        var allSets = await _db.SetsEntities.ToListAsync();
                        foreach (var s in allSets) s.Dirty = true;
                        await _db.SaveChangesAsync();

                        if (_options.AutoRebuildStaticCache)
                        {
                            await _pkgManager.SyncToSims3Async(forceRebuildStatic: true);
                        }
                    }
                    else
                    {
                        // On dynamic caching, only update Resource.cfg!
                        await _pkgManager.SyncToSims3Async(forceRebuildStatic: false);
                    }
                }
            }

            RefreshSetsColumns();
            _ = App.Services?.GetService<MainViewModel>()?.RefreshDirtyStateAsync();
        }
        catch (Exception ex)
        {
            ShowStatusToast($"Failed to update configuration sets: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task SwapSetsAsync()
    {
        if (SelectedConfiguration == null) return;
        var currentlyEnabled = SelectedConfiguration.SetIds.ToHashSet();
        var allIds = _allSets.Select(s => s.Id).ToList();
        var newEnabled = allIds.Where(id => !currentlyEnabled.Contains(id)).ToList();

        SelectedConfiguration.SetIds = newEnabled;

        try
        {
            var configEntity = await _db.ConfigEntities
                .Include(c => c.ConfigSetsEntities)
                .FirstOrDefaultAsync(c => c.Id == SelectedConfiguration.Id);

            if (configEntity != null)
            {
                configEntity.ConfigSetsEntities.Clear();
                foreach (var id in SelectedConfiguration.SetIds)
                {
                    configEntity.ConfigSetsEntities.Add(new ConfigSetsEntity
                    {
                        ConfigEntityId = SelectedConfiguration.Id,
                        SetsEntityId = id
                    });
                }
                await _db.SaveChangesAsync();

                if (SelectedConfiguration.Active)
                {
                    bool isStatic = string.Equals(_options.CacheMethod, "Static", StringComparison.OrdinalIgnoreCase);
                    if (isStatic)
                    {
                        var allSets = await _db.SetsEntities.ToListAsync();
                        foreach (var s in allSets) s.Dirty = true;
                        await _db.SaveChangesAsync();

                        if (_options.AutoRebuildStaticCache)
                        {
                            await _pkgManager.SyncToSims3Async(forceRebuildStatic: true);
                        }
                    }
                    else
                    {
                        await _pkgManager.SyncToSims3Async(forceRebuildStatic: false);
                    }
                }
            }

            RefreshSetsColumns();
            _ = App.Services?.GetService<MainViewModel>()?.RefreshDirtyStateAsync();
            ShowStatusToast("Swapped enabled and disabled sets.", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatusToast($"Failed to swap sets: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task EnableAllSetsAsync()
    {
        if (SelectedConfiguration == null) return;
        var allIds = _allSets.Select(s => s.Id).ToList();
        await ApplyMoveSetsAsync(allIds, toEnabled: true);
        ShowStatusToast("All sets enabled in configuration.", isError: false);
    }

    [RelayCommand]
    public async Task DisableAllSetsAsync()
    {
        if (SelectedConfiguration == null) return;
        var allIds = _allSets.Select(s => s.Id).ToList();
        await ApplyMoveSetsAsync(allIds, toEnabled: false);
        ShowStatusToast("All sets disabled in configuration.", isError: false);
    }

    public void SelectSetCard(ConfigSetItemViewModel set, bool isCtrlPressed, bool isShiftPressed)
    {
        var targetCollection = set.IsEnabledInConfig ? EnabledSets : DisabledSets;
        var otherCollection = set.IsEnabledInConfig ? DisabledSets : EnabledSets;

        foreach (var other in otherCollection) other.IsSelected = false;

        if (isCtrlPressed)
        {
            set.IsSelected = !set.IsSelected;
        }
        else if (isShiftPressed)
        {
            int firstSelected = -1;
            int currentIndex = targetCollection.IndexOf(set);

            for (int i = 0; i < targetCollection.Count; i++)
            {
                if (targetCollection[i].IsSelected)
                {
                    firstSelected = i;
                    break;
                }
            }

            if (firstSelected != -1 && currentIndex != -1)
            {
                int start = Math.Min(firstSelected, currentIndex);
                int end = Math.Max(firstSelected, currentIndex);
                for (int i = 0; i < targetCollection.Count; i++)
                {
                    targetCollection[i].IsSelected = (i >= start && i <= end);
                }
            }
            else
            {
                set.IsSelected = true;
            }
        }
        else
        {
            bool wasSelected = set.IsSelected && targetCollection.Count(s => s.IsSelected) == 1;
            foreach (var item in targetCollection) item.IsSelected = false;
            set.IsSelected = !wasSelected;
        }

        UpdateSelectionState();
    }

    public void ClearSelection()
    {
        foreach (var s in EnabledSets) s.IsSelected = false;
        foreach (var s in DisabledSets) s.IsSelected = false;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        SelectedSetsCount = EnabledSets.Count(s => s.IsSelected) + DisabledSets.Count(s => s.IsSelected);
        HasSelectedSets = SelectedSetsCount > 0;
    }

    public void ShowStatusToast(string message, bool isError = false)
    {
        StatusToastMessage = message;
        IsStatusToastError = isError;
        IsStatusToastVisible = true;

        Task.Delay(3500).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsStatusToastVisible = false;
            });
        });
    }
}
