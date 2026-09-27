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
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Utils;

namespace PlumbobForge.Desktop.ViewModels;

public partial class SimpleConflictCardViewModel : ObservableObject
{
    public ConflictCardModel Model { get; }

    public string Title => Model.Title;
    public string Explanation => Model.Explanation;
    public string Recommendation => Model.Recommendation;
    public ConflictCardCategory Category => Model.Category;

    public PackageConflictItem PrimaryPackage => Model.PrimaryPackage;
    public PackageConflictItem? SecondaryPackage => Model.SecondaryPackage;
    public bool HasSecondaryPackage => SecondaryPackage != null;
    public bool IsIntraSet => Model.IsIntraSet;
    public string AffectedSummary => Model.AffectedSummary;
    public int AffectedResourceCount => Model.AffectedResourceCount;
    public List<string> TechnicalDetails => Model.TechnicalDetails;
    public bool HasTechnicalDetails => TechnicalDetails.Count > 0;

    [ObservableProperty]
    private bool _isDetailsExpanded = false;

    [ObservableProperty]
    private bool _isPrimaryEnabled = true;

    [ObservableProperty]
    private bool _isSecondaryEnabled = true;

    public string CategoryBadgeText => Category switch
    {
        ConflictCardCategory.Incompatibility => "Incompatible Mods",
        ConflictCardCategory.Duplicate => "Exact Duplicate",
        ConflictCardCategory.DefaultOverride => "Default Override",
        ConflictCardCategory.CorruptFile => "Broken File",
        _ => "Collision"
    };

    public string CategoryBadgeBg => Category switch
    {
        ConflictCardCategory.Incompatibility => "#331518",
        ConflictCardCategory.Duplicate => "#33240f",
        ConflictCardCategory.DefaultOverride => "#122544",
        ConflictCardCategory.CorruptFile => "#261a3d",
        _ => "#1e293b"
    };

    public string CategoryBadgeBorder => Category switch
    {
        ConflictCardCategory.Incompatibility => "#ef4444",
        ConflictCardCategory.Duplicate => "#f59e0b",
        ConflictCardCategory.DefaultOverride => "#3b82f6",
        ConflictCardCategory.CorruptFile => "#8b5cf6",
        _ => "#64748b"
    };

    public string CategoryBadgeColor => CategoryBadgeBorder;

    public bool IsIncompatibility => Category == ConflictCardCategory.Incompatibility;
    public bool IsDuplicate => Category == ConflictCardCategory.Duplicate;
    public bool IsOverride => Category == ConflictCardCategory.DefaultOverride;
    public bool IsCorrupt => Category == ConflictCardCategory.CorruptFile;

    public static Action<PackageConflictItem?, bool>? OnTogglePackageRequested { get; set; }

    partial void OnIsPrimaryEnabledChanged(bool value)
    {
        if (PrimaryPackage.IsEnabled != value)
        {
            PrimaryPackage.IsEnabled = value;
            OnTogglePackageRequested?.Invoke(PrimaryPackage, value);
        }
    }

    partial void OnIsSecondaryEnabledChanged(bool value)
    {
        if (SecondaryPackage != null && SecondaryPackage.IsEnabled != value)
        {
            SecondaryPackage.IsEnabled = value;
            OnTogglePackageRequested?.Invoke(SecondaryPackage, value);
        }
    }

    public SimpleConflictCardViewModel(ConflictCardModel model)
    {
        Model = model;
        _isPrimaryEnabled = model.PrimaryPackage.IsEnabled;
        _isSecondaryEnabled = model.SecondaryPackage?.IsEnabled ?? true;
    }

    [RelayCommand]
    public void ToggleDetails()
    {
        IsDetailsExpanded = !IsDetailsExpanded;
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
    private string _conflictFilter = "All"; // "All", "Incompatible", "Duplicates", "Overrides", "Corrupt"

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

    partial void OnConflictFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterIncompatible));
        OnPropertyChanged(nameof(IsFilterDuplicates));
        OnPropertyChanged(nameof(IsFilterOverrides));
        OnPropertyChanged(nameof(IsFilterCorrupt));
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
            IncompatibilitiesCount = summary.IncompatibilitiesCount;
            DuplicatesCount = summary.DuplicatesCount;
            OverridesCount = summary.OverridesCount;
            CorruptCount = summary.CorruptCount;

            AllConflictCards.Clear();
            foreach (var card in summary.ConflictCards)
            {
                AllConflictCards.Add(new SimpleConflictCardViewModel(card));
            }

            HasScannedConflicts = true;
            ApplyConflictFilter();
            OnPropertyChanged(nameof(TotalIssuesCount));
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

    [RelayCommand]
    public void SetFilter(string filter)
    {
        ConflictFilter = filter;
    }

    private void ApplyConflictFilter()
    {
        string query = ConflictSearchQuery?.Trim() ?? string.Empty;

        var cards = AllConflictCards.AsEnumerable();

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

        if (!string.IsNullOrEmpty(query))
        {
            cards = cards.Where(c =>
                c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Explanation.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.PrimaryPackage.PackageFileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.PrimaryPackage.SetName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (c.SecondaryPackage != null && (
                    c.SecondaryPackage.PackageFileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.SecondaryPackage.SetName.Contains(query, StringComparison.OrdinalIgnoreCase))) ||
                c.AffectedSummary.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        FilteredConflictCards.ReplaceAll(cards.ToList());

        OnPropertyChanged(nameof(IsNoIssuesFound));
        OnPropertyChanged(nameof(HasIssues));
    }

    [RelayCommand]
    public void NavigateToPackageItem(PackageConflictItem? item)
    {
        if (item == null) return;
        NavigateToPackageItem(item.MetaEntityId, item.SetId);
    }

    public void NavigateToPackageItem(long? metaEntityId, long? setId)
    {
        var mainVm = _serviceProvider.GetService<MainViewModel>();
        if (mainVm == null) return;

        if (setId.HasValue && metaEntityId.HasValue)
        {
            mainVm.NavigateToContentManagerAndSelectSet(setId.Value, metaEntityId.Value);
        }
        else
        {
            mainVm.NavigateToContentManager();
        }
    }

    public async Task SetPackageEnabledAsync(PackageConflictItem? item, bool isEnabled)
    {
        if (item == null || !item.MetaEntityId.HasValue) return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var meta = await db.MetaEntities.FindAsync(item.MetaEntityId.Value);
            if (meta != null)
            {
                meta.Enabled = isEnabled;
                item.IsEnabled = isEnabled;

                if (meta.SetsEntityId.HasValue)
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
                    if (card.PrimaryPackage.MetaEntityId == item.MetaEntityId && card.IsPrimaryEnabled != isEnabled)
                        card.IsPrimaryEnabled = isEnabled;
                    if (card.SecondaryPackage?.MetaEntityId == item.MetaEntityId && card.IsSecondaryEnabled != isEnabled)
                        card.IsSecondaryEnabled = isEnabled;
                }

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

    [RelayCommand]
    public async Task TogglePackageEnabledAsync(PackageConflictItem? item)
    {
        if (item == null) return;
        await SetPackageEnabledAsync(item, !item.IsEnabled);
    }

    #endregion
}
