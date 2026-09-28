using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ContentManagerViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly PKGManager _pkgManager;
    private readonly ThumbnailService _thumbnailService;
    private readonly UiStateService _uiStateService;
    private readonly NotificationService _notificationService;
    private readonly Guid _notificationSubscriptionId;
    private bool _isInitializingState = false;
    private long? _savedSetId = null;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isDragOver = false;

    [ObservableProperty]
    private string _importStatusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchQuery))]
    private string _searchQuery = string.Empty;

    public bool HasSearchQuery => !string.IsNullOrEmpty(SearchQuery);

    [RelayCommand]
    public void ClearSearchQuery() => SearchQuery = string.Empty;

    [ObservableProperty]
    private SetNodeViewModel? _selectedSetNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleSetsSelected))]
    private int _selectedSetsCount = 0;

    public bool HasMultipleSetsSelected => SelectedSetsCount >= 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteItemsMenuHeader))]
    private int _selectedCount = 0;

    public string DeleteItemsMenuHeader => SelectedCount > 1 ? "Delete Items" : "Delete Item";

    [ObservableProperty]
    private bool _hasSelectedItems = false;

    [ObservableProperty]
    private SetsEntity? _selectedMoveTargetSet;

    [ObservableProperty]
    private string _batchTagInput = string.Empty;

    [ObservableProperty]
    private string _batchPackageType = "CAS";

    [ObservableProperty]
    private string _currentSetSort = "DateCreated"; // "DateCreated", "NameAsc", "NameDesc"

    [ObservableProperty]
    private string _currentItemSort = "DateAdded"; // "DateAdded", "NameAsc", "NameDesc"

    [RelayCommand]
    public void SetItemSortMode(string sortMode)
    {
        CurrentItemSort = sortMode;
        SaveCurrentUiState();
        ApplyFilter();
    }

    [ObservableProperty]
    private bool _isSetsSidebarCollapsed = false;

    [ObservableProperty]
    private bool _isFiltersSidebarCollapsed = false;

    [ObservableProperty]
    private bool _isCompactListView = false;

    [ObservableProperty]
    private int _gridZoomLevel = 3; // 1 to 5, default 3 (middle)

    partial void OnIsCompactListViewChanged(bool value) => SaveCurrentUiState();
    partial void OnIsSetsSidebarCollapsedChanged(bool value) => SaveCurrentUiState();
    partial void OnIsFiltersSidebarCollapsedChanged(bool value) => SaveCurrentUiState();

    public double BaseItemWidth => GridZoomLevel switch
    {
        1 => 140.0,
        2 => 175.0,
        3 => 210.0,
        4 => 265.0,
        5 => 330.0,
        _ => 210.0
    };

    public double CardThumbnailHeight => GridZoomLevel switch
    {
        1 => 120.0,
        2 => 150.0,
        3 => 180.0,
        4 => 225.0,
        5 => 280.0,
        _ => 180.0
    };

    partial void OnGridZoomLevelChanged(int value)
    {
        ItemViewModel.GlobalThumbnailHeight = CardThumbnailHeight;
        foreach (var item in Items)
        {
            item.NotifyThumbnailHeightChanged();
        }
        if (_lastAvailableWidth > 0)
        {
            RecalculateColumns(_lastAvailableWidth);
        }
        else
        {
            RebuildRows();
        }
        SaveCurrentUiState();
    }

    private double _lastAvailableWidth = 0;

    public void RecalculateColumns(double availableWidth)
    {
        _lastAvailableWidth = availableWidth;
        int cols = Math.Max(1, (int)Math.Floor(availableWidth / BaseItemWidth));
        SetColumnCount(cols);
    }

    [RelayCommand]
    public void ToggleSetsSidebar() => IsSetsSidebarCollapsed = !IsSetsSidebarCollapsed;

    [RelayCommand]
    public void ToggleFiltersSidebar() => IsFiltersSidebarCollapsed = !IsFiltersSidebarCollapsed;

    [RelayCommand]
    public void SetGridView() => IsCompactListView = false;

    [RelayCommand]
    public void SetCompactListView() => IsCompactListView = true;

    [RelayCommand]
    public void ToggleViewMode() => IsCompactListView = !IsCompactListView;

    public ObservableCollection<SetNodeViewModel> RootSets { get; } = new();
    public ObservableCollection<SetsEntity> FlatSets { get; } = new();
    public ObservableCollection<ItemViewModel> Items { get; } = new();
    public ObservableCollection<ItemViewModel> FilteredItems { get; } = new();
    public ObservableCollection<ItemRowViewModel> FilteredRows { get; } = new();
    public int ColumnCount { get; private set; } = 4;
    public List<string> AvailablePackageTypes { get; } = new() { "CAS", "Build", "Buy", "Pattern", "Other" };

    [ObservableProperty]
    private ItemViewModel? _inspectedItem;

    [ObservableProperty]
    private bool _isDetailsModalOpen = false;

    [RelayCommand]
    public void OpenDetailsModal(ItemViewModel? item)
    {
        if (item == null) return;
        InspectedItem = item;
        IsDetailsModalOpen = true;
    }

    [RelayCommand]
    public void CloseDetailsModal()
    {
        IsDetailsModalOpen = false;
        InspectedItem = null;
    }

    public ContentManagerViewModel(AppDbContext db, PKGManager pkgManager, ThumbnailService thumbnailService, UiStateService uiStateService, NotificationService notificationService)
    {
        _db = db;
        _pkgManager = pkgManager;
        _thumbnailService = thumbnailService;
        _uiStateService = uiStateService;
        _notificationService = notificationService;
        ItemViewModel.GlobalThumbnailService = thumbnailService;
        ItemViewModel.OnRenameRequested = item => _ = RenameItemAsync(item);
        ItemViewModel.OnEditTagsRequested = item => _ = EditItemTagsAsync(item);
        ItemViewModel.OnToggleEnableRequested = item => _ = ToggleEnableItemAsync(item);
        ItemViewModel.OnShowDetailsRequested = item => OpenDetailsModal(item);
        ItemViewModel.OnNoteChangedCallback = (item, note) => _ = SaveItemNoteAsync(item, note);
        
        _notificationSubscriptionId = _notificationService.Subscribe(async payload =>
        {
            if (payload.Contains("library_changed") || payload.Contains("items_imported"))
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await LoadDataAsync();
                });
            }
        });

        RestoreUiState();
        IsLoading = true;
    }

    private void RestoreUiState()
    {
        _isInitializingState = true;
        try
        {
            var state = _uiStateService.LoadState();
            IsCompactListView = state.IsCompactListView;
            GridZoomLevel = state.GridZoomLevel;
            _savedSetId = state.SelectedSetId;
            IsSetsSidebarCollapsed = state.IsSetsSidebarCollapsed;
            IsFiltersSidebarCollapsed = state.IsFiltersSidebarCollapsed;
            if (!string.IsNullOrEmpty(state.CurrentItemSort)) CurrentItemSort = state.CurrentItemSort;
            if (!string.IsNullOrEmpty(state.CurrentSetSort)) CurrentSetSort = state.CurrentSetSort;

            // Main Type Filters
            FilterTypeCAS = state.FilterTypeCAS;
            FilterTypeBuildBuy = state.FilterTypeBuildBuy;
            FilterTypeOther = state.FilterTypeOther;

            // CAS Categories
            FilterCasHair = state.FilterCasHair;
            FilterCasFullBody = state.FilterCasFullBody;
            FilterCasTops = state.FilterCasTops;
            FilterCasBottoms = state.FilterCasBottoms;
            FilterCasShoes = state.FilterCasShoes;
            FilterCasDetails = state.FilterCasDetails;
            FilterCasSkins = state.FilterCasSkins;
            FilterCasAccessories = state.FilterCasAccessories;
            FilterCasSliders = state.FilterCasSliders;
            FilterCasPresets = state.FilterCasPresets;
            FilterCasOther = state.FilterCasOther;

            // Ages
            FilterAgeBaby = state.FilterAgeBaby;
            FilterAgeToddler = state.FilterAgeToddler;
            FilterAgeChild = state.FilterAgeChild;
            FilterAgeTeen = state.FilterAgeTeen;
            FilterAgeYoungAdult = state.FilterAgeYoungAdult;
            FilterAgeAdult = state.FilterAgeAdult;
            FilterAgeElder = state.FilterAgeElder;

            // Gender
            FilterGenderMale = state.FilterGenderMale;
            FilterGenderFemale = state.FilterGenderFemale;

            // Outfit Categories
            FilterOutfitEveryday = state.FilterOutfitEveryday;
            FilterOutfitFormal = state.FilterOutfitFormal;
            FilterOutfitSleepwear = state.FilterOutfitSleepwear;
            FilterOutfitSwimwear = state.FilterOutfitSwimwear;
            FilterOutfitAthletic = state.FilterOutfitAthletic;
            FilterOutfitCareer = state.FilterOutfitCareer;
            FilterOutfitOuterwear = state.FilterOutfitOuterwear;

            // Other Sub-Categories
            FilterOtherWorlds = state.FilterOtherWorlds;
            FilterOtherSims = state.FilterOtherSims;
            FilterOtherLots = state.FilterOtherLots;
            FilterOtherMisc = state.FilterOtherMisc;

            // Mode
            FilterModeEnabled = state.FilterModeEnabled;
            FilterModeDisabled = state.FilterModeDisabled;

            // Accordion Expansion States
            IsTypeExpanded = state.IsTypeExpanded;
            IsCasSubFiltersExpanded = state.IsCasSubFiltersExpanded;
            IsCasCategoryExpanded = state.IsCasCategoryExpanded;
            IsCasAgeExpanded = state.IsCasAgeExpanded;
            IsCasGenderExpanded = state.IsCasGenderExpanded;
            IsCasOutfitExpanded = state.IsCasOutfitExpanded;
            IsOtherSubExpanded = state.IsOtherSubExpanded;
            IsModeExpanded = state.IsModeExpanded;

            ItemViewModel.GlobalThumbnailHeight = CardThumbnailHeight;
        }
        finally
        {
            _isInitializingState = false;
        }
    }

    public void SaveCurrentUiState(bool immediate = false)
    {
        if (_isInitializingState) return;

        var state = new ContentManagerUiState
        {
            IsCompactListView = IsCompactListView,
            GridZoomLevel = GridZoomLevel,
            SelectedSetId = SelectedSetNode?.Id ?? _savedSetId,
            IsSetsSidebarCollapsed = IsSetsSidebarCollapsed,
            IsFiltersSidebarCollapsed = IsFiltersSidebarCollapsed,
            CurrentItemSort = CurrentItemSort,
            CurrentSetSort = CurrentSetSort,

            FilterTypeCAS = FilterTypeCAS,
            FilterTypeBuildBuy = FilterTypeBuildBuy,
            FilterTypeOther = FilterTypeOther,

            FilterCasHair = FilterCasHair,
            FilterCasFullBody = FilterCasFullBody,
            FilterCasTops = FilterCasTops,
            FilterCasBottoms = FilterCasBottoms,
            FilterCasShoes = FilterCasShoes,
            FilterCasDetails = FilterCasDetails,
            FilterCasSkins = FilterCasSkins,
            FilterCasAccessories = FilterCasAccessories,
            FilterCasSliders = FilterCasSliders,
            FilterCasPresets = FilterCasPresets,
            FilterCasOther = FilterCasOther,

            FilterAgeBaby = FilterAgeBaby,
            FilterAgeToddler = FilterAgeToddler,
            FilterAgeChild = FilterAgeChild,
            FilterAgeTeen = FilterAgeTeen,
            FilterAgeYoungAdult = FilterAgeYoungAdult,
            FilterAgeAdult = FilterAgeAdult,
            FilterAgeElder = FilterAgeElder,

            FilterGenderMale = FilterGenderMale,
            FilterGenderFemale = FilterGenderFemale,

            FilterOutfitEveryday = FilterOutfitEveryday,
            FilterOutfitFormal = FilterOutfitFormal,
            FilterOutfitSleepwear = FilterOutfitSleepwear,
            FilterOutfitSwimwear = FilterOutfitSwimwear,
            FilterOutfitAthletic = FilterOutfitAthletic,
            FilterOutfitCareer = FilterOutfitCareer,
            FilterOutfitOuterwear = FilterOutfitOuterwear,

            FilterOtherWorlds = FilterOtherWorlds,
            FilterOtherSims = FilterOtherSims,
            FilterOtherLots = FilterOtherLots,
            FilterOtherMisc = FilterOtherMisc,

            FilterModeEnabled = FilterModeEnabled,
            FilterModeDisabled = FilterModeDisabled,

            IsTypeExpanded = IsTypeExpanded,
            IsCasSubFiltersExpanded = IsCasSubFiltersExpanded,
            IsCasCategoryExpanded = IsCasCategoryExpanded,
            IsCasAgeExpanded = IsCasAgeExpanded,
            IsCasGenderExpanded = IsCasGenderExpanded,
            IsCasOutfitExpanded = IsCasOutfitExpanded,
            IsOtherSubExpanded = IsOtherSubExpanded,
            IsModeExpanded = IsModeExpanded
        };

        _uiStateService?.SaveState(state, immediate);
    }

    public SetNodeViewModel? FindNodeById(IEnumerable<SetNodeViewModel> nodes, long id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id) return node;
            var childMatch = FindNodeById(node.Children, id);
            if (childMatch != null) return childMatch;
        }
        return null;
    }

    public void SetColumnCount(int columns)
    {
        if (columns < 1) columns = 1;
        if (ColumnCount != columns)
        {
            ColumnCount = columns;
            RebuildRows();
        }
    }

    public void RebuildRows()
    {
        FilteredRows.Clear();
        int cols = ColumnCount > 0 ? ColumnCount : 4;
        var items = FilteredItems;
        for (int i = 0; i < items.Count; i += cols)
        {
            int count = Math.Min(cols, items.Count - i);
            var rowItems = new List<ItemViewModel>(count);
            for (int j = 0; j < count; j++)
            {
                rowItems.Add(items[i + j]);
            }
            FilteredRows.Add(new ItemRowViewModel(rowItems, cols));
        }
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            // 1. Instant: Load & build Set hierarchy first
            var setsList = await _db.SetsEntities.AsNoTracking().ToListAsync();

            FlatSets.Clear();
            foreach (var set in setsList)
            {
                FlatSets.Add(set);
            }

            RootSets.Clear();
            var allItemsNode = new SetNodeViewModel("All Items", null);
            RootSets.Add(allItemsNode);

            var nodeMap = setsList.ToDictionary(s => s.Id, s => new SetNodeViewModel(s));
            foreach (var set in setsList)
            {
                var node = nodeMap[set.Id];
                if (set.ParentSetsEntityId.HasValue && nodeMap.TryGetValue(set.ParentSetsEntityId.Value, out var parentNode))
                {
                    parentNode.Children.Add(node);
                }
                else
                {
                    RootSets.Add(node);
                }
            }

            SortSetsTree();

            SetNodeViewModel? targetNode = null;
            if (_savedSetId.HasValue)
            {
                targetNode = FindNodeById(RootSets, _savedSetId.Value);
            }
            SelectedSetNode = targetNode ?? allItemsNode;

            // 2. Allow UI frame to render window, header, and loading animation on screen
            await Task.Delay(150);

            // 3. Background: Load items from database off the UI thread
            var (items, counts, totalCount) = await Task.Run(async () =>
            {
                using var scope = App.Services?.CreateScope();
                var db = scope != null ? scope.ServiceProvider.GetRequiredService<AppDbContext>() : _db;
                var itemsList = await db.MetaEntities.AsNoTracking().ToListAsync();

                var itemVms = new List<ItemViewModel>(itemsList.Count);
                var itemCounts = new Dictionary<long, int>();

                foreach (var item in itemsList)
                {
                    var vm = new ItemViewModel(item)
                    {
                        OnSelectionChanged = UpdateSelectionState
                    };
                    itemVms.Add(vm);

                    if (item.SetsEntityId.HasValue)
                    {
                        itemCounts[item.SetsEntityId.Value] = itemCounts.GetValueOrDefault(item.SetsEntityId.Value, 0) + 1;
                    }
                }

                return (itemVms, itemCounts, itemsList.Count);
            });

            // 3. UI Thread: Populate items collection and apply initial view
            allItemsNode.ItemCount = totalCount;
            foreach (var rootNode in RootSets)
            {
                if (rootNode.Id.HasValue)
                {
                    rootNode.CalculateItemCount(counts);
                }
            }

            Items.Clear();
            foreach (var vm in items)
            {
                Items.Add(vm);
            }

            UpdateSelectionState();
            ApplyFilter();
            NotifyDirtyStateChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void NotifyDirtyStateChanged()
    {
        var mainVm = App.Services?.GetService<MainViewModel>();
        if (mainVm != null)
        {
            _ = mainVm.RefreshDirtyStateAsync();
        }

        var configVm = App.Services?.GetService<ConfigurationsViewModel>();
        if (configVm != null)
        {
            _ = configVm.LoadDataAsync();
        }

        if (_notificationService != null)
        {
            _ = _notificationService.BroadcastAsync("sets_changed", new { timestamp = DateTime.UtcNow });
        }
    }

    public void RecalculateSetCounts()
    {
        var itemCounts = new Dictionary<long, int>();
        foreach (var item in Items)
        {
            if (item.Entity.SetsEntityId.HasValue)
            {
                itemCounts[item.Entity.SetsEntityId.Value] = itemCounts.GetValueOrDefault(item.Entity.SetsEntityId.Value, 0) + 1;
            }
        }

        var allItemsNode = RootSets.FirstOrDefault(n => n.IsAllItems);
        if (allItemsNode != null)
        {
            allItemsNode.ItemCount = Items.Count;
        }

        foreach (var rootNode in RootSets)
        {
            if (rootNode.Id.HasValue)
            {
                rootNode.CalculateItemCount(itemCounts);
            }
        }
    }

    [RelayCommand]
    public async Task ImportDroppedFilesAsync(string[] filePaths)
    {
        if (filePaths == null || filePaths.Length == 0) return;

        // Check if any files or archive contents are duplicates
        string duplicateAction = "rename";
        var existingNames = Items.Select(i => i.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateNames = new List<string>();
        foreach (var p in filePaths)
        {
            if (PKGManager.IsArchiveExtension(p))
            {
                candidateNames.AddRange(ArchiveService.GetArchivePackageFileNames(p));
            }
            else
            {
                candidateNames.Add(Path.GetFileName(p));
            }
        }

        bool hasDuplicates = candidateNames.Any(n => existingNames.Contains(n));

        if (hasDuplicates)
        {
            duplicateAction = await DialogHelper.ShowDuplicateAlertAsync(
                "Duplicate Files Detected",
                "Some of the dropped files already exist in your library. Choose how to handle duplicates:");

            if (duplicateAction == "cancel") return;
        }

        IsLoading = true;
        ImportStatusText = "Importing dropped files...";
        try
        {
            long? targetSetId = SelectedSetNode?.Id;
            await Task.Run(async () =>
            {
                await _pkgManager.ImportFilesAsync(filePaths, msg =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ImportStatusText = msg;
                    });
                }, duplicateAction, targetSetId);
            });

            await LoadDataAsync();
        }
        finally
        {
            IsLoading = false;
            ImportStatusText = string.Empty;
        }
    }

    private async Task SaveItemNoteAsync(ItemViewModel item, string? note)
    {
        try
        {
            var meta = await _db.MetaEntities.FindAsync(item.Id);
            if (meta != null)
            {
                meta.Description = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
                await _db.SaveChangesAsync();
            }
        }
        catch
        {
            // Ignore background save errors
        }
    }

    [RelayCommand]
    public async Task RebuildCacheAsync(bool forceRebuild = false)
    {
        var mainVm = App.Services?.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel();
        modal.Start(LocalizationManager.Instance.GetString(forceRebuild ? "progress.force_rebuilding_cache" : "progress.rebuilding_cache"));

        if (mainVm != null)
        {
            mainVm.ActiveProgressModal = modal;
        }

        modal.Closed += () =>
        {
            if (mainVm != null)
            {
                mainVm.ActiveProgressModal = null;
            }
        };

        bool success = false;
        try
        {
            await Task.Run(async () =>
            {
                await _pkgManager.RunAsync(isRefresh: true, onProgress: null, progress: modal, forceRebuild: forceRebuild);
            });
            success = true;
            modal.Finish(success: true);

            if (modal.CloseAppOnFinish && success)
            {
                await Task.Delay(400);
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                    return;
                }
            }

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Error rebuilding cache", ex, "ContentManagerViewModel");
            modal.Finish(success: false);
        }
    }
}
