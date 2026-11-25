using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Utils;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ConflictPackageItemViewModel : ObservableObject
{
    private readonly SimpleConflictCardViewModel _parentCard;

    public PackageConflictItem Model { get; }

    public string PackageFileName => Model.PackageFileName;
    public string PackagePath => Model.PackagePath;
    public string SetName => Model.SetName;
    public long? SetId => Model.SetId;
    public long? MetaEntityId => Model.MetaEntityId;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isWinningInLoadOrder;

    public bool IsLoadedByConfiguration => Model.IsLoadedByConfiguration;

    public bool ShowActiveBadge => IsEnabled && IsWinningInLoadOrder;
    public bool ShowOverriddenBadge => IsEnabled && IsLoadedByConfiguration && !IsWinningInLoadOrder;
    public bool ShowNotLoadedBadge => IsEnabled && !IsLoadedByConfiguration;
    public bool ShowDisabledBadge => !IsEnabled;

    public bool CanKeepOnlyThis => _parentCard.Packages.Count > 1;

    public ConflictPackageItemViewModel(PackageConflictItem model, SimpleConflictCardViewModel parentCard)
    {
        Model = model;
        _parentCard = parentCard;
        _isEnabled = model.IsEnabled;
        _isWinningInLoadOrder = model.IsWinningInLoadOrder;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowActiveBadge));
        OnPropertyChanged(nameof(ShowOverriddenBadge));
        OnPropertyChanged(nameof(ShowNotLoadedBadge));
        OnPropertyChanged(nameof(ShowDisabledBadge));

        if (Model.IsEnabled != value)
        {
            Model.IsEnabled = value;
            SimpleConflictCardViewModel.OnTogglePackageRequested?.Invoke(Model, value);
            _parentCard.RecalculateLoadOrder();
        }
    }

    partial void OnIsWinningInLoadOrderChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowActiveBadge));
        OnPropertyChanged(nameof(ShowOverriddenBadge));
        OnPropertyChanged(nameof(ShowNotLoadedBadge));
        OnPropertyChanged(nameof(ShowDisabledBadge));
    }

    [RelayCommand]
    public void KeepOnlyThis()
    {
        _parentCard.KeepOnlyPackage(this);
    }

    [RelayCommand]
    public void NavigateToLibrary()
    {
        SimpleConflictCardViewModel.OnNavigateRequested?.Invoke(Model);
    }
}

public partial class SimpleConflictCardViewModel : ObservableObject
{
    public ConflictCardModel Model { get; }
    private readonly HealthViewModel? _healthVm;

    public string Title => Model.Title;
    public string Explanation => Model.Explanation;
    public string Recommendation => Model.Recommendation;
    public ConflictCardCategory Category => Model.Category;
    public bool IsIntraSet => Model.IsIntraSet;
    public string AffectedSummary => Model.AffectedSummary;
    public int AffectedResourceCount => Model.AffectedResourceCount;
    public List<string> TechnicalDetails => Model.TechnicalDetails;
    public bool HasTechnicalDetails => TechnicalDetails.Count > 0;
    public string ConflictFingerprint => Model.ConflictFingerprint;

    public ObservableCollection<ConflictPackageItemViewModel> Packages { get; } = new();

    public PackageConflictItem PrimaryPackage => Model.PrimaryPackage;
    public PackageConflictItem? SecondaryPackage => Model.SecondaryPackage;
    public bool HasSecondaryPackage => Packages.Count > 1;

    public string FileCountBadgeText => Packages.Count == 1 ? "1 file" : $"{Packages.Count} files";

    // The wide layout's list row: which files, at a glance.
    public string FileNamesText => string.Join(" · ", Packages.Select(p => p.PackageFileName));

    [ObservableProperty]
    private bool _isDetailsExpanded = false;

    [ObservableProperty]
    private bool _isIgnored = false;

    public string CategoryBadgeText => Category switch
    {
        ConflictCardCategory.Incompatibility => "Incompatible Mods",
        ConflictCardCategory.Duplicate => "Exact Duplicate",
        ConflictCardCategory.DefaultOverride => "Default Override",
        ConflictCardCategory.CorruptFile => "Broken File",
        _ => "Collision"
    };

    // Theme colors, read when the card is shown, so the badge follows light and dark themes.
    public string CategoryBadgeBg => Category switch
    {
        ConflictCardCategory.Incompatibility => ThemeService.ResourceHex("StatusRedBg", "#331518"),
        ConflictCardCategory.Duplicate => ThemeService.ResourceHex("StatusAmberBg", "#33240f"),
        ConflictCardCategory.DefaultOverride => ThemeService.ResourceHex("StatusBlueBg", "#122544"),
        ConflictCardCategory.CorruptFile => ThemeService.ResourceHex("StatusPurpleBg", "#261a3d"),
        _ => ThemeService.ResourceHex("BgCardAlt", "#1e293b")
    };

    public string CategoryBadgeBorder => Category switch
    {
        ConflictCardCategory.Incompatibility => ThemeService.ResourceHex("StatusRed", "#ef4444"),
        ConflictCardCategory.Duplicate => ThemeService.ResourceHex("StatusAmber", "#f59e0b"),
        ConflictCardCategory.DefaultOverride => ThemeService.ResourceHex("StatusBlue", "#3b82f6"),
        ConflictCardCategory.CorruptFile => ThemeService.ResourceHex("StatusPurple", "#8b5cf6"),
        _ => ThemeService.ResourceHex("TextMuted", "#64748b")
    };

    public string CategoryBadgeColor => CategoryBadgeBorder;

    public bool IsIncompatibility => Category == ConflictCardCategory.Incompatibility;
    public bool IsDuplicate => Category == ConflictCardCategory.Duplicate;
    public bool IsOverride => Category == ConflictCardCategory.DefaultOverride;
    public bool IsCorrupt => Category == ConflictCardCategory.CorruptFile;

    public static Action<PackageConflictItem?, bool>? OnTogglePackageRequested { get; set; }
    public static Action<PackageConflictItem?>? OnNavigateRequested { get; set; }

    public SimpleConflictCardViewModel(ConflictCardModel model, HealthViewModel? healthVm = null)
    {
        Model = model;
        _healthVm = healthVm;
        _isIgnored = model.IsIgnored;

        foreach (var pkg in model.Packages)
        {
            Packages.Add(new ConflictPackageItemViewModel(pkg, this));
        }

        RecalculateLoadOrder();
    }

    [RelayCommand]
    public void ToggleDetails()
    {
        IsDetailsExpanded = !IsDetailsExpanded;
    }

    [RelayCommand]
    public void ToggleIgnore()
    {
        IsIgnored = !IsIgnored;
        Model.IsIgnored = IsIgnored;
        _healthVm?.OnConflictIgnoreToggled(this);
    }

    public void KeepOnlyPackage(ConflictPackageItemViewModel chosenPackage)
    {
        foreach (var pkg in Packages)
        {
            if (pkg == chosenPackage)
            {
                if (!pkg.IsEnabled) pkg.IsEnabled = true;
            }
            else
            {
                if (pkg.IsEnabled) pkg.IsEnabled = false;
            }
        }
        RecalculateLoadOrder();
    }

    public void RecalculateLoadOrder()
    {
        // Same rule as the scan: the enabled, loaded package earliest in the game's load order wins.
        Sims3HealthService.ComputeLoadOrderWinners(Packages.Select(p => p.Model));
        foreach (var p in Packages)
        {
            p.IsWinningInLoadOrder = p.Model.IsWinningInLoadOrder;
        }
    }
}

public partial class HealthViewModel
{
    #region Conflict Scanner Properties & Commands

    private CancellationTokenSource? _conflictSearchCts;

    public ObservableCollection<SimpleConflictCardViewModel> AllConflictCards { get; } = new();
    public RangeObservableCollection<SimpleConflictCardViewModel> FilteredConflictCards { get; } = new();

    [ObservableProperty]
    private bool _isScanningConflicts = false;

    [ObservableProperty]
    private bool _hasScannedConflicts = false;

    [ObservableProperty]
    private int _totalPackagesScanned = 0;

    [ObservableProperty]
    private int _incompatibilitiesCount = 0;

    [ObservableProperty]
    private int _duplicatesCount = 0;

    [ObservableProperty]
    private int _overridesCount = 0;

    [ObservableProperty]
    private int _corruptCount = 0;

    [ObservableProperty]
    private int _ignoredCount = 0;

    [ObservableProperty]
    private string _conflictFilter = "All"; // "All", "Incompatible", "Duplicates", "Overrides", "Corrupt", "Ignored"

    [ObservableProperty]
    private string _conflictSearchQuery = string.Empty;

    public int TotalIssuesCount => IncompatibilitiesCount + DuplicatesCount + OverridesCount + CorruptCount;
    public bool IsNoIssuesFound => HasScannedConflicts && FilteredConflictCards.Count == 0;
    public bool HasIssues => HasScannedConflicts && FilteredConflictCards.Count > 0;

    public bool IsFilterAll => ConflictFilter == "All";
    public bool IsFilterIncompatible => ConflictFilter == "Incompatible";
    public bool IsFilterDuplicates => ConflictFilter == "Duplicates";
    public bool IsFilterOverrides => ConflictFilter == "Overrides";
    public bool IsFilterCorrupt => ConflictFilter == "Corrupt";
    public bool IsFilterIgnored => ConflictFilter == "Ignored";

    public bool CanDisableDuplicates => DuplicatesCount > 0;
    public bool CanShowDuplicatesBanner => IsFilterDuplicates && CanDisableDuplicates;

    partial void OnConflictFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterIncompatible));
        OnPropertyChanged(nameof(IsFilterDuplicates));
        OnPropertyChanged(nameof(IsFilterOverrides));
        OnPropertyChanged(nameof(IsFilterCorrupt));
        OnPropertyChanged(nameof(IsFilterIgnored));
        OnPropertyChanged(nameof(CanShowDuplicatesBanner));
        ApplyConflictFilter();
    }

    public bool HasConflictSearchQuery => !string.IsNullOrEmpty(ConflictSearchQuery);

    [RelayCommand]
    public void ClearConflictSearch()
    {
        ConflictSearchQuery = string.Empty;
    }

    partial void OnConflictSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasConflictSearchQuery));
        _conflictSearchCts?.Cancel();
        _conflictSearchCts = new CancellationTokenSource();
        var token = _conflictSearchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                if (!token.IsCancellationRequested)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            ApplyConflictFilter();
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    [RelayCommand]
    public async Task ScanConflictsAsync()
    {
        if (IsScanningConflicts) return;

        IsScanningConflicts = true;
        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel();
        modal.Start(LocalizationManager.Instance.GetString("progress.scanning_conflicts_title"));
        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            HealthScanSummary summary = new();
            await Task.Run(async () =>
            {
                summary = await _healthService.ScanLibraryConflictsAsync(progress: modal);
            });

            modal.Finish(success: true);

            TotalPackagesScanned = summary.TotalPackagesScanned;

            AllConflictCards.Clear();
            var ignoredSet = IgnoredConflictsService.GetIgnoredFingerprints();

            foreach (var card in summary.ConflictCards)
            {
                if (!string.IsNullOrEmpty(card.ConflictFingerprint) && ignoredSet.Contains(card.ConflictFingerprint))
                {
                    card.IsIgnored = true;
                }
                AllConflictCards.Add(new SimpleConflictCardViewModel(card, this));
            }

            RecalculateCounts();
            HasScannedConflicts = true;
            ApplyConflictFilter();
        }
        catch (Exception ex)
        {
            modal.Finish(success: false);
            ShowCacheToast($"Conflict scan failed: {ex.Message}", isError: true);
        }
        finally
        {
            IsScanningConflicts = false;
        }
    }

    public void OnConflictIgnoreToggled(SimpleConflictCardViewModel card)
    {
        if (card.IsIgnored)
        {
            IgnoredConflictsService.Ignore(card.ConflictFingerprint);
            ShowCacheToast($"Conflict ignored: '{card.Title}'", isError: false);
        }
        else
        {
            IgnoredConflictsService.Unignore(card.ConflictFingerprint);
            ShowCacheToast($"Conflict restored: '{card.Title}'", isError: false);
        }

        RecalculateCounts();
        ApplyConflictFilter();
    }

    public void RecalculateCounts()
    {
        int incompCount = 0;
        int dupeCount = 0;
        int overrideCount = 0;
        int corruptCount = 0;
        int ignoredCount = 0;

        foreach (var c in AllConflictCards)
        {
            if (c.IsIgnored)
            {
                ignoredCount++;
            }
            else
            {
                switch (c.Category)
                {
                    case ConflictCardCategory.Incompatibility: incompCount++; break;
                    case ConflictCardCategory.Duplicate: dupeCount++; break;
                    case ConflictCardCategory.DefaultOverride: overrideCount++; break;
                    case ConflictCardCategory.CorruptFile: corruptCount++; break;
                }
            }
        }

        IncompatibilitiesCount = incompCount;
        DuplicatesCount = dupeCount;
        OverridesCount = overrideCount;
        CorruptCount = corruptCount;
        IgnoredCount = ignoredCount;

        OnPropertyChanged(nameof(TotalIssuesCount));
        OnPropertyChanged(nameof(CanDisableDuplicates));
        OnPropertyChanged(nameof(CanShowDuplicatesBanner));
    }

    [RelayCommand]
    public void SetFilter(string filter)
    {
        ConflictFilter = filter;
    }

    [RelayCommand]
    public async Task DisableRedundantDuplicatesAsync()
    {
        var dupeCards = AllConflictCards
            .Where(c => !c.IsIgnored && c.Category == ConflictCardCategory.Duplicate)
            .ToList();

        if (dupeCards.Count == 0) return;

        var itemsToDisable = new List<PackageConflictItem>();

        foreach (var card in dupeCards)
        {
            var enabledPkgs = card.Packages.Where(p => p.IsEnabled).ToList();
            if (enabledPkgs.Count > 1)
            {
                var keep = enabledPkgs.FirstOrDefault(p => p.IsWinningInLoadOrder) ?? enabledPkgs[0];
                foreach (var p in enabledPkgs)
                {
                    if (p != keep && p.IsEnabled)
                    {
                        p.IsEnabled = false;
                        itemsToDisable.Add(p.Model);
                    }
                }
                card.RecalculateLoadOrder();
            }
        }

        if (itemsToDisable.Count > 0)
        {
            await BatchUpdatePackagesEnabledAsync(itemsToDisable, false);
            ShowCacheToast($"Disabled {itemsToDisable.Count} redundant duplicate package(s).", isError: false);
        }
        else
        {
            ShowCacheToast("All duplicate sets already have only 1 copy active.", isError: false);
        }
    }

    private void ApplyConflictFilter()
    {
        string query = ConflictSearchQuery?.Trim() ?? string.Empty;

        IEnumerable<SimpleConflictCardViewModel> cards;

        if (ConflictFilter == "Ignored")
        {
            cards = AllConflictCards.Where(c => c.IsIgnored);
        }
        else
        {
            cards = AllConflictCards.Where(c => !c.IsIgnored);

            if (ConflictFilter == "Incompatible")
            {
                cards = cards.Where(c => c.Category == ConflictCardCategory.Incompatibility);
            }
            else if (ConflictFilter == "Duplicates")
            {
                cards = cards.Where(c => c.Category == ConflictCardCategory.Duplicate);
            }
            else if (ConflictFilter == "Overrides")
            {
                cards = cards.Where(c => c.Category == ConflictCardCategory.DefaultOverride);
            }
            else if (ConflictFilter == "Corrupt")
            {
                cards = cards.Where(c => c.Category == ConflictCardCategory.CorruptFile);
            }
        }

        if (!string.IsNullOrEmpty(query))
        {
            cards = cards.Where(c =>
                c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Explanation.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.AffectedSummary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Packages.Any(p =>
                    p.PackageFileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.SetName.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var previous = SelectedConflictCard;
        int oldIndex = previous == null ? -1 : FilteredConflictCards.IndexOf(previous);
        FilteredConflictCards.ReplaceAll(cards.ToList());
        SelectConflictAfterFilter(previous, oldIndex);

        OnPropertyChanged(nameof(IsNoIssuesFound));
        OnPropertyChanged(nameof(HasIssues));
    }

    [RelayCommand]
    public void NavigateToPackageItem(PackageConflictItem? item)
    {
        if (item == null) return;
        NavigateToPackageItem(item.IsModsItem ? null : item.MetaEntityId, item.SetId, item.IsModsItem ? item.MetaEntityId : null);
    }

    // A Mods folder file (modsItemId) opens in its Mods folder.
    public void NavigateToPackageItem(long? metaEntityId, long? setId, long? modsItemId = null)
    {
        var mainVm = _serviceProvider.GetService<MainViewModel>();
        if (mainVm == null) return;
        if (modsItemId.HasValue)
        {
            mainVm.NavigateToItem(modsItemId.Value);
            return;
        }

        if (setId.HasValue && metaEntityId.HasValue)
        {
            mainVm.NavigateToContentManagerAndSelectSet(setId.Value, metaEntityId.Value);
        }
        else
        {
            mainVm.NavigateToContentManager();
        }
    }

    public async Task BatchUpdatePackagesEnabledAsync(IEnumerable<PackageConflictItem> items, bool isEnabled)
    {
        var list = items.ToList();
        var modsIds = list.Where(i => i.IsModsItem && i.MetaEntityId.HasValue).Select(i => i.MetaEntityId!.Value).Distinct().ToList();
        if (modsIds.Count > 0) await SetModsPackagesEnabledAsync(modsIds, isEnabled);

        var metaIds = list.Where(i => !i.IsModsItem && i.MetaEntityId.HasValue).Select(i => i.MetaEntityId!.Value).Distinct().ToList();
        if (metaIds.Count == 0) return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var metas = await db.MetaEntities.Where(m => metaIds.Contains(m.Id)).ToListAsync();

            // Vault items only reach the game once their shard index is rewritten; the vault service does that.
            var vaultIds = metas.Where(m => m.IsVaultManaged).Select(m => m.Id).ToList();
            if (vaultIds.Count > 0)
            {
                var vaultService = scope.ServiceProvider.GetRequiredService<VaultService>();
                await vaultService.ToggleModsBatchAsync(vaultIds, isEnabled);
                metas = metas.Where(m => !m.IsVaultManaged).ToList();
            }

            var setIds = metas.Where(m => m.SetsEntityId.HasValue).Select(m => m.SetsEntityId!.Value).Distinct().ToList();

            foreach (var meta in metas)
            {
                meta.Enabled = isEnabled;
            }

            if (setIds.Count > 0)
            {
                var sets = await db.SetsEntities.Include(s => s.MetaEntities).Where(s => setIds.Contains(s.Id)).ToListAsync();
                foreach (var set in sets)
                {
                    SetDirtyTracker.UpdateDirtyState(set);
                }
            }

            await db.SaveChangesAsync();

            // Sync in-memory ContentManager items so UI reflects immediately without app restart
            await SyncContentManagerItemStatesAsync(metaIds, isEnabled);

            var mainVm = _serviceProvider.GetService<MainViewModel>();
            if (mainVm != null)
            {
                _ = mainVm.RefreshDirtyStateAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to batch update package enabled state", ex, "HealthViewModel.Conflicts");
        }
    }

    public async Task SetPackageEnabledAsync(PackageConflictItem? item, bool isEnabled)
    {
        if (item == null || !item.MetaEntityId.HasValue) return;

        if (item.IsModsItem)
        {
            var done = await SetModsPackagesEnabledAsync(new[] { item.MetaEntityId.Value }, isEnabled);
            if (done.Count > 0)
                ShowCacheToast($"Updated status for '{item.PackageFileName}': {(isEnabled ? "Enabled" : "Disabled")}", isError: false);
            else
                ShowCacheToast($"Failed to update package: {item.PackageFileName}", isError: true);
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var meta = await db.MetaEntities.FindAsync(item.MetaEntityId.Value);
            if (meta != null)
            {
                item.IsEnabled = isEnabled;

                if (meta.IsVaultManaged)
                {
                    // Rewrites the shard index so the game picks up the change.
                    await scope.ServiceProvider.GetRequiredService<VaultService>().ToggleModAsync(meta.Id, isEnabled);
                }
                else
                {
                    meta.Enabled = isEnabled;
                }

                if (!meta.IsVaultManaged && meta.SetsEntityId.HasValue)
                {
                    var set = await db.SetsEntities.Include(s => s.MetaEntities).FirstOrDefaultAsync(s => s.Id == meta.SetsEntityId.Value);
                    if (set != null)
                    {
                        SetDirtyTracker.UpdateDirtyState(set);
                    }
                }

                await db.SaveChangesAsync();

                // Update card visual state across all cards
                foreach (var card in AllConflictCards)
                {
                    var matching = card.Packages.FirstOrDefault(p => p.MetaEntityId == item.MetaEntityId);
                    if (matching != null && matching.IsEnabled != isEnabled)
                    {
                        matching.IsEnabled = isEnabled;
                    }
                }

                // Sync in-memory ContentManager item so Library tab reflects change immediately
                await SyncContentManagerItemStatesAsync(new[] { item.MetaEntityId.Value }, isEnabled);

                var mainVm = _serviceProvider.GetService<MainViewModel>();
                if (mainVm != null)
                {
                    _ = mainVm.RefreshDirtyStateAsync();
                }

                ShowCacheToast($"Updated status for '{item.PackageFileName}': {(isEnabled ? "Enabled" : "Disabled")}", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Failed to update package: {ex.Message}", isError: true);
        }
    }

    // Mods files are switched by renaming them; returns the ids that switched. Cards and the library follow.
    private async Task<List<long>> SetModsPackagesEnabledAsync(IReadOnlyCollection<long> ids, bool isEnabled)
    {
        var done = new List<long>();
        try
        {
            var paths = await Task.Run(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ModsFolderService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var result = new Dictionary<long, string>();
                foreach (long id in ids)
                {
                    if (await service.SetEnabledAsync(id, isEnabled) == ModsFolderService.ToggleOutcome.Failed) continue;
                    result[id] = await db.AllMetaEntities.AsNoTracking().Where(m => m.Id == id)
                        .Select(m => m.CompleteFileName).FirstOrDefaultAsync() ?? string.Empty;
                }
                return result;
            });
            done.AddRange(paths.Keys);

            // A file that couldn't be renamed keeps its old state on every card.
            foreach (var card in AllConflictCards)
            {
                bool changed = false;
                foreach (var package in card.Packages)
                {
                    if (package.MetaEntityId is not long id || !ids.Contains(id)) continue;
                    bool ok = paths.TryGetValue(id, out var path);
                    bool state = ok ? isEnabled : !isEnabled;
                    package.Model.IsEnabled = state;
                    if (package.IsEnabled != state) { package.IsEnabled = state; changed = true; }
                    if (ok && path!.Length > 0) package.Model.PackagePath = path;
                    if (!ok) changed = true;
                }
                if (changed) card.RecalculateLoadOrder();
            }

            var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
            if (contentVm != null && paths.Count > 0)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var libraryItem in contentVm.Items.Where(i => paths.ContainsKey(i.Entity.Id)))
                    {
                        libraryItem.Entity.Enabled = isEnabled;
                        if (paths[libraryItem.Entity.Id].Length > 0) libraryItem.Entity.CompleteFileName = paths[libraryItem.Entity.Id];
                        libraryItem.NotifyEntityChanged();
                    }
                    contentVm.ApplyFilter();
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to switch Mods files", ex, "HealthViewModel.Conflicts");
        }
        return done;
    }

    private async Task SyncContentManagerItemStatesAsync(IReadOnlyCollection<long> metaIds, bool isEnabled)
    {
        var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
        if (contentVm == null) return;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            bool anyUpdated = false;
            foreach (var metaId in metaIds)
            {
                var libraryItem = contentVm.Items.FirstOrDefault(i => i.Entity.Id == metaId);
                if (libraryItem != null)
                {
                    libraryItem.Entity.Enabled = isEnabled;
                    libraryItem.NotifyEntityChanged();
                    anyUpdated = true;
                }
            }

            if (anyUpdated)
            {
                contentVm.RecalculateSetCounts();
                contentVm.ApplyFilter();
                contentVm.NotifyDirtyStateChanged();
            }
        });
    }

    #endregion
}
