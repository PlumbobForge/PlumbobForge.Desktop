using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Desktop.Controls.Icons;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;
using S3ForgeTools.GameFiles.Package;
using PlumbobForge.Desktop.Utils;

namespace PlumbobForge.Desktop.ViewModels;

public partial class CollectionItemViewModel : ObservableObject
{
    public CollectionModel Model { get; }

    public ulong Id => Model.Id;
    public string Name => Model.Name;
    public string FileName => Model.FileName;
    public string FilePath => Model.FilePath;
    public TGI_Key IconKey => Model.IconKey;
    public string IconName => Model.IconName;
    public string IconCategory => Model.IconCategory;
    public int ItemCount => Model.ItemCount;
    public bool IsPlumbobForge => Model.IsPlumbobForge;
    public bool HideFromCatalog => Model.HideFromCatalog;
    public List<string> LinkedSetNames => Model.LinkedSetNames;
    public string FormattedLastModified => Model.LastModified?.ToString("g") ?? "Unknown";

    public Bitmap? IconBitmap { get; }
    public bool HasBitmap => IconBitmap != null;

    public CollectionItemViewModel(CollectionModel model)
    {
        Model = model;
        if (model.IconBytes != null && model.IconBytes.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(model.IconBytes);
                IconBitmap = new Bitmap(ms);
            }
            catch { }
        }
    }
}

public partial class CollectionIconItemViewModel : ObservableObject
{
    public CollectionIconDef Model { get; }

    public string KeyName => Model.KeyName;
    public string DisplayName => Model.DisplayName;
    public string Category => Model.Category;
    public TGI_Key Key => Model.Key;
    public string BoxIconKind => Model.BoxIconKind;
    public Bitmap? IconBitmap { get; }
    public bool HasBitmap => IconBitmap != null;
    public bool IsCustom => Model.IsCustom;

    [ObservableProperty]
    private bool _isActiveSelection;

    public CollectionIconItemViewModel(CollectionIconDef model)
    {
        Model = model;
        if (model.IconBytes != null && model.IconBytes.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(model.IconBytes);
                IconBitmap = new Bitmap(ms);
            }
            catch { }
        }
    }
}

public partial class CollectionSetOptionViewModel : ObservableObject
{
    public CollectionSetOption Model { get; }

    public long SetId => Model.SetId;
    public string SetName => Model.SetName;
    public string BreadcrumbPath => Model.BreadcrumbPath;
    public bool HasBreadcrumb => !string.IsNullOrWhiteSpace(BreadcrumbPath);
    public string Icon => Model.Icon;
    public string? Color => Model.Color;
    public int ItemCount => Model.ItemCount;

    public PackIconBoxIconsKind IconKind =>
        Enum.TryParse<PackIconBoxIconsKind>(Icon, out var kind) ? kind : PackIconBoxIconsKind.RegularFolder;

    public bool HasCustomColor => !string.IsNullOrEmpty(Color);

    public IBrush IconBrush =>
        HasCustomColor
            ? Brush.Parse(Color!)
            : ThemeService.GetCurrentAccentBrush();

    [ObservableProperty]
    private bool _isSelected;

    public CollectionSetOptionViewModel(CollectionSetOption model, bool? isSelected = null)
    {
        Model = model;
        _isSelected = isSelected ?? model.IsSelected;
        Model.IsSelected = _isSelected;
        ThemeService.AccentChanged += () =>
        {
            OnPropertyChanged(nameof(IconBrush));
            OnPropertyChanged(nameof(HasCustomColor));
        };
    }

    partial void OnIsSelectedChanged(bool value)
    {
        Model.IsSelected = value;
    }
}

public sealed partial class CollectionIncludedItemViewModel : ObservableObject
{
    private readonly ItemThumbnail _thumbnail;

    public long ItemId { get; }
    public string FileName { get; }
    public long? SetId { get; }
    public string Location { get; }
    public bool HasLocation => !string.IsNullOrEmpty(Location);
    public Bitmap? Thumbnail => _thumbnail.Get();

    public CollectionIncludedItemViewModel(long itemId, string fileName, long? setId, string location)
    {
        ItemId = itemId;
        FileName = fileName;
        SetId = setId;
        Location = location;
        _thumbnail = new ItemThumbnail(itemId, () => OnPropertyChanged(nameof(Thumbnail)));
    }
}

public partial class HealthViewModel
{
    #region Collection Creator Properties & Commands

    public RangeObservableCollection<CollectionIncludedItemViewModel> IncludedItems { get; } = new();
    public bool HasIncludedItems => IncludedItems.Count > 0;
    private List<SetsEntity> _collectionSets = new();

    private string GetCollectionSetLocation(long? setId)
    {
        var byId = _collectionSets.ToDictionary(s => s.Id);
        var names = new List<string>();
        var visited = new HashSet<long>();
        long? current = setId;
        while (current.HasValue && visited.Add(current.Value) && byId.TryGetValue(current.Value, out var set))
        {
            names.Insert(0, set.Name);
            current = set.ParentSetsEntityId;
        }
        return string.Join("  ›  ", names);
    }

    private async Task LoadIncludedItemsAsync(IReadOnlyCollection<long> itemIds)
    {
        var services = App.Services;
        if (services == null) return;
        var ids = itemIds.Distinct().ToList();
        var (sets, rows) = await Task.Run(async () =>
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.SetsEntities.AsNoTracking().ToListAsync();
            var r = ids.Count == 0
                ? new List<(long Id, string FileName, long? SetId)>()
                : (await db.MetaEntities.AsNoTracking()
                    .Where(m => ids.Contains(m.Id))
                    .Select(m => new { m.Id, m.FileName, m.SetsEntityId })
                    .ToListAsync())
                    .Select(m => (Id: m.Id, FileName: m.FileName, SetId: m.SetsEntityId))
                    .ToList();
            return (s, r);
        });
        _collectionSets = sets;
        IncludedItems.ReplaceAll(rows
            .OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(r => new CollectionIncludedItemViewModel(r.Id, r.FileName, r.SetId, GetCollectionSetLocation(r.SetId))));
        OnIncludedItemsChanged();
    }

    private void OnIncludedItemsChanged()
    {
        OnPropertyChanged(nameof(HasIncludedItems));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
    }

    [RelayCommand]
    public async Task IncludeItemsInCollectionAsync()
    {
        try
        {
            if (_collectionSets.Count == 0) await LoadIncludedItemsAsync(IncludedItems.Select(i => i.ItemId).ToList());
            string name = string.IsNullOrWhiteSpace(EditingCollectionName)
                ? LocalizationManager.Instance.GetString("tools.collections.this_collection")
                : EditingCollectionName.Trim();
            var existing = IncludedItems.ToDictionary(i => i.ItemId, _ => true);
            var picked = await DialogHelper.ShowConfigItemPickerAsync(true, name, _collectionSets, existing, forCollection: true);
            if (picked.Count == 0) return;
            await LoadIncludedItemsAsync(IncludedItems.Select(i => i.ItemId).Concat(picked).ToList());
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error including items: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public void RemoveIncludedItem(CollectionIncludedItemViewModel? item)
    {
        if (item == null) return;
        IncludedItems.Remove(item);
        OnIncludedItemsChanged();
    }

    public RangeObservableCollection<CollectionItemViewModel> AllCollections { get; } = new();
    public RangeObservableCollection<CollectionItemViewModel> FilteredCollections { get; } = new();
    public RangeObservableCollection<CollectionSetOptionViewModel> AvailableSetOptions { get; } = new();
    public RangeObservableCollection<CollectionSetOptionViewModel> AssignedSets { get; } = new();
    public RangeObservableCollection<CollectionSetOptionViewModel> AvailableToAddSets { get; } = new();
    public RangeObservableCollection<CollectionIconItemViewModel> AllIcons { get; } = new();
    public RangeObservableCollection<CollectionIconItemViewModel> FilteredIcons { get; } = new();

    [ObservableProperty]
    private bool _isLoadingCollections = false;

    [ObservableProperty]
    private bool _hasLoadedCollections = false;

    [ObservableProperty]
    private bool _isCollectionEditorOpen = false;

    [ObservableProperty]
    private bool _isLoadingSetOptions = false;

    [ObservableProperty]
    private bool _isSavingCollection = false;

    [ObservableProperty]
    private ulong? _editingCollectionId = null;

    [ObservableProperty]
    private string _editingCollectionName = string.Empty;

    [ObservableProperty]
    private bool _editingHideFromCatalog = false;

    [ObservableProperty]
    private CollectionIconItemViewModel? _selectedIcon = null;

    partial void OnSelectedIconChanged(CollectionIconItemViewModel? value)
    {
        foreach (var icon in AllIcons)
        {
            icon.IsActiveSelection = (icon == value || (value != null && icon.Key.Instance == value.Key.Instance));
        }
    }

    [ObservableProperty]
    private string _iconCategoryFilter = "All"; // "All", "Furniture", "Skills", "Careers", "Pets", "Adventures", "Symbols"

    [ObservableProperty]
    private string _iconSearchQuery = string.Empty;

    [ObservableProperty]
    private string _collectionSearchQuery = string.Empty;

    [ObservableProperty]
    private int _totalCollectionsCount = 0;

    [ObservableProperty]
    private int _totalCollectionItemsCount = 0;

    public bool IsEditingExistingCollection => EditingCollectionId.HasValue && EditingCollectionId.Value != 0;
    public string CollectionEditorTitle => IsEditingExistingCollection ? "Edit Collection" : "Create New Collection";
    public bool HasCollections => FilteredCollections.Count > 0;
    public bool IsNoCollectionsFound => HasLoadedCollections && FilteredCollections.Count == 0;
    public bool HasCollectionSearchQuery => !string.IsNullOrEmpty(CollectionSearchQuery);
    public bool HasIconSearchQuery => !string.IsNullOrEmpty(IconSearchQuery);

    [ObservableProperty]
    private string _setSearchQuery = string.Empty;

    public bool HasAssignedSets => AssignedSets.Count > 0;
    public bool HasAvailableToAddSets => AvailableToAddSets.Count > 0;

    public int SelectedSetsItemCount
    {
        get
        {
            var assigned = AssignedSets.Select(s => s.SetId).ToHashSet();
            return AssignedSets.Sum(s => s.ItemCount) + IncludedItems.Count(i => !i.SetId.HasValue || !assigned.Contains(i.SetId.Value));
        }
    }
    public int SelectedSetsCount => AssignedSets.Count;

    partial void OnSetSearchQueryChanged(string value)
    {
        RefreshAvailableToAddSets();
    }

    private void RefreshAvailableToAddSets()
    {
        var query = SetSearchQuery.Trim();
        var filtered = AvailableSetOptions.Where(s => !s.IsSelected && (
            string.IsNullOrEmpty(query) ||
            s.SetName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.BreadcrumbPath.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

        AvailableToAddSets.ReplaceAll(filtered);
        OnPropertyChanged(nameof(HasAvailableToAddSets));
    }

    public bool IsIconCategoryAll => IconCategoryFilter == "All";
    public bool IsIconCategoryCustom => IconCategoryFilter == "Custom";
    public bool IsIconCategoryFurniture => IconCategoryFilter == "Furniture";
    public bool IsIconCategoryBuild => IconCategoryFilter == "Build";
    public bool IsIconCategoryOccult => IconCategoryFilter == "Occult";
    public bool IsIconCategorySeasons => IconCategoryFilter == "Seasons";
    public bool IsIconCategoryVehicles => IconCategoryFilter == "Vehicles";
    public bool IsIconCategorySkills => IconCategoryFilter == "Skills";
    public bool IsIconCategoryCareers => IconCategoryFilter == "Careers";
    public bool IsIconCategoryPets => IconCategoryFilter == "Pets";
    public bool IsIconCategoryAdventures => IconCategoryFilter == "Adventures";
    public bool IsIconCategorySymbols => IconCategoryFilter == "Symbols";

    partial void OnIconCategoryFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsIconCategoryAll));
        OnPropertyChanged(nameof(IsIconCategoryCustom));
        OnPropertyChanged(nameof(IsIconCategoryFurniture));
        OnPropertyChanged(nameof(IsIconCategoryBuild));
        OnPropertyChanged(nameof(IsIconCategoryOccult));
        OnPropertyChanged(nameof(IsIconCategorySeasons));
        OnPropertyChanged(nameof(IsIconCategoryVehicles));
        OnPropertyChanged(nameof(IsIconCategorySkills));
        OnPropertyChanged(nameof(IsIconCategoryCareers));
        OnPropertyChanged(nameof(IsIconCategoryPets));
        OnPropertyChanged(nameof(IsIconCategoryAdventures));
        OnPropertyChanged(nameof(IsIconCategorySymbols));
        ApplyIconFilter();
    }

    partial void OnIconSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasIconSearchQuery));
        ApplyIconFilter();
    }

    partial void OnCollectionSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasCollectionSearchQuery));
        ApplyCollectionFilter();
    }

    private void InitCollectionIcons()
    {
        var icons = _collectionService.GetStandardIcons();
        var iconVms = new List<CollectionIconItemViewModel>(icons.Count);
        foreach (var ic in icons)
        {
            var vm = new CollectionIconItemViewModel(ic);
            if (SelectedIcon != null && vm.Key.Instance == SelectedIcon.Key.Instance)
            {
                vm.IsActiveSelection = true;
            }
            iconVms.Add(vm);
        }
        AllIcons.ReplaceAll(iconVms);
        ApplyIconFilter();
    }

    [RelayCommand]
    public void SetIconCategoryFilter(string category)
    {
        IconCategoryFilter = category;
    }

    [RelayCommand]
    public async Task ImportCustomIconAsync(IStorageProvider? storageProvider)
    {
        if (storageProvider == null) return;
        try
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Image for Collection Icon",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Image Files (*.png, *.jpg, *.jpeg, *.bmp, *.webp)")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" }
                    }
                }
            });

            if (files.Count > 0)
            {
                var file = files[0];
                await using var stream = await file.OpenReadAsync();
                var newIconDef = await _collectionService.ImportCustomIconAsync(stream, file.Name);

                InitCollectionIcons();
                IconCategoryFilter = "Custom";

                var newVm = AllIcons.FirstOrDefault(i => i.Key.Instance == newIconDef.Key.Instance);
                if (newVm != null)
                {
                    SelectedIcon = newVm;
                }

                ShowCacheToast($"Imported custom icon '{newIconDef.DisplayName}'!", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error importing custom icon: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task DeleteCustomIconAsync(CollectionIconItemViewModel? iconVm)
    {
        if (iconVm == null || !iconVm.IsCustom) return;
        try
        {
            bool deleted = await _collectionService.DeleteCustomIconAsync(iconVm.Key.Instance);
            if (deleted)
            {
                if (SelectedIcon?.Key.Instance == iconVm.Key.Instance)
                {
                    SelectedIcon = AllIcons.FirstOrDefault(i => !i.IsCustom);
                }
                InitCollectionIcons();
                ShowCacheToast($"Deleted custom icon '{iconVm.DisplayName}'.", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error deleting custom icon: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public void SelectIcon(CollectionIconItemViewModel? icon)
    {
        SelectedIcon = icon;
    }

    [RelayCommand]
    public void ClearCollectionSearch()
    {
        CollectionSearchQuery = string.Empty;
    }

    [RelayCommand]
    public void ClearIconSearch()
    {
        IconSearchQuery = string.Empty;
    }

    [RelayCommand]
    public void ClearSetSearch()
    {
        SetSearchQuery = string.Empty;
    }

    [RelayCommand]
    public async Task LoadCollectionsAsync()
    {
        if (IsLoadingCollections) return;
        IsLoadingCollections = true;
        try
        {
            var list = await _collectionService.LoadUserCollectionsAsync();
            var collVms = new List<CollectionItemViewModel>(list.Count);
            int totalItems = 0;

            foreach (var c in list)
            {
                totalItems += c.ItemCount;
                collVms.Add(new CollectionItemViewModel(c));
            }

            AllCollections.ReplaceAll(collVms);
            TotalCollectionsCount = AllCollections.Count;
            TotalCollectionItemsCount = totalItems;
            HasLoadedCollections = true;

            if (AllIcons.Count == 0)
            {
                InitCollectionIcons();
            }
            ApplyCollectionFilter();
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error loading collections: {ex.Message}", isError: true);
        }
        finally
        {
            IsLoadingCollections = false;
        }
    }

    [ObservableProperty]
    private bool _isEditingNonPlumbobForgeCollection = false;

    [RelayCommand]
    public async Task CreateNewCollectionAsync()
    {
        EditingCollectionId = null;
        EditingCollectionName = string.Empty;
        EditingHideFromCatalog = false;
        IsEditingNonPlumbobForgeCollection = false;
        if (AllIcons.Count == 0) InitCollectionIcons();
        SelectedIcon = AllIcons.FirstOrDefault();
        IconCategoryFilter = "All";
        IconSearchQuery = string.Empty;

        OnPropertyChanged(nameof(IsEditingExistingCollection));
        OnPropertyChanged(nameof(CollectionEditorTitle));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));

        IncludedItems.Clear();
        OnIncludedItemsChanged();

        // Open immediately so the user experiences zero lag
        IsCollectionEditorOpen = true;

        await LoadAvailableSetOptionsAsync();
        await LoadIncludedItemsSafeAsync(Array.Empty<long>());
    }

    private async Task LoadIncludedItemsSafeAsync(IReadOnlyCollection<long> itemIds)
    {
        try
        {
            await LoadIncludedItemsAsync(itemIds);
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error loading included items: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task EditCollectionAsync(CollectionItemViewModel? item)
    {
        if (item == null) return;

        EditingCollectionId = item.Id;
        EditingCollectionName = item.Name;
        EditingHideFromCatalog = item.HideFromCatalog;
        IsEditingNonPlumbobForgeCollection = !item.IsPlumbobForge;
        if (AllIcons.Count == 0) InitCollectionIcons();
        SelectedIcon = AllIcons.FirstOrDefault(i => i.Key.Instance == item.IconKey.Instance) ?? AllIcons.FirstOrDefault();
        IconCategoryFilter = "All";
        IconSearchQuery = string.Empty;

        OnPropertyChanged(nameof(IsEditingExistingCollection));
        OnPropertyChanged(nameof(CollectionEditorTitle));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));

        IncludedItems.Clear();
        OnIncludedItemsChanged();

        // Open immediately so the user experiences zero lag
        IsCollectionEditorOpen = true;

        await LoadAvailableSetOptionsAsync(item.Model.LinkedSetIds);
        await LoadIncludedItemsSafeAsync(item.Model.LinkedItemIds);
    }

    private async Task LoadAvailableSetOptionsAsync(List<long>? preSelectedSetIds = null)
    {
        IsLoadingSetOptions = true;
        AvailableSetOptions.Clear();
        AssignedSets.Clear();
        AvailableToAddSets.Clear();
        SetSearchQuery = string.Empty;

        try
        {
            var setOptions = await _collectionService.GetAvailableSetOptionsAsync();
            var availableList = new List<CollectionSetOptionViewModel>(setOptions.Count);
            var assignedList = new List<CollectionSetOptionViewModel>();

            foreach (var s in setOptions)
            {
                bool isSelected = preSelectedSetIds != null && preSelectedSetIds.Contains(s.SetId);
                s.IsSelected = isSelected;

                var vm = new CollectionSetOptionViewModel(s, isSelected);
                availableList.Add(vm);
                if (vm.IsSelected)
                {
                    assignedList.Add(vm);
                }
            }

            AvailableSetOptions.ReplaceAll(availableList);
            AssignedSets.ReplaceAll(assignedList);
            RefreshAvailableToAddSets();
            OnPropertyChanged(nameof(HasAssignedSets));
            OnPropertyChanged(nameof(SelectedSetsItemCount));
            OnPropertyChanged(nameof(SelectedSetsCount));
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error loading sets: {ex.Message}", isError: true);
        }
        finally
        {
            IsLoadingSetOptions = false;
        }
    }

    [RelayCommand]
    public void AddSetToCollection(CollectionSetOptionViewModel? setOption)
    {
        if (setOption == null) return;
        setOption.IsSelected = true;
        if (!AssignedSets.Contains(setOption))
        {
            AssignedSets.Add(setOption);
        }
        RefreshAvailableToAddSets();
        OnPropertyChanged(nameof(HasAssignedSets));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));
    }

    [RelayCommand]
    public void RemoveAssignedSet(CollectionSetOptionViewModel? setOption)
    {
        if (setOption == null) return;
        setOption.IsSelected = false;
        AssignedSets.Remove(setOption);
        RefreshAvailableToAddSets();
        OnPropertyChanged(nameof(HasAssignedSets));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));
    }

    [RelayCommand]
    public void SelectAllSetsForCollection()
    {
        var assignedList = new List<CollectionSetOptionViewModel>(AvailableSetOptions.Count);
        foreach (var opt in AvailableSetOptions)
        {
            opt.IsSelected = true;
            assignedList.Add(opt);
        }
        AssignedSets.ReplaceAll(assignedList);
        RefreshAvailableToAddSets();
        OnPropertyChanged(nameof(HasAssignedSets));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));
    }

    [RelayCommand]
    public void DeselectAllSetsForCollection()
    {
        foreach (var opt in AvailableSetOptions)
        {
            opt.IsSelected = false;
        }
        AssignedSets.Clear();
        RefreshAvailableToAddSets();
        OnPropertyChanged(nameof(HasAssignedSets));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));
    }

    [RelayCommand]
    public void CancelEditCollection()
    {
        IsCollectionEditorOpen = false;
    }

    [RelayCommand]
    public async Task SaveCollectionAsync()
    {
        if (string.IsNullOrWhiteSpace(EditingCollectionName))
        {
            ShowCacheToast("Please enter a collection name.", isError: true);
            return;
        }

        var selectedSets = AssignedSets.Select(s => s.SetId).ToList();
        var iconKey = SelectedIcon?.Key ?? AllIcons.FirstOrDefault()?.Key ?? new TGI_Key(0x2F7D0004, 0, 13431223140568206672uL);

        bool savedSuccessfully = false;
        IsSavingCollection = true;
        try
        {
            var result = await _collectionService.SaveCollectionAsync(
                EditingCollectionId,
                EditingCollectionName.Trim(),
                iconKey,
                selectedSets,
                EditingHideFromCatalog,
                includedItemIds: IncludedItems.Select(i => i.ItemId).ToList());

            if (result.Success)
            {
                savedSuccessfully = true;
                string extraMsg = EditingHideFromCatalog ? " (Catalog Hider active)" : "";
                ShowCacheToast($"Saved collection '{EditingCollectionName}' ({result.ItemCount} items) to Sims 3{extraMsg}!", isError: false);
                IsCollectionEditorOpen = false;
            }
            else
            {
                ShowCacheToast($"Error saving collection: {result.ErrorMessage}", isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error saving collection: {ex.Message}", isError: true);
        }
        finally
        {
            IsSavingCollection = false;
        }

        if (savedSuccessfully)
        {
            await LoadCollectionsAsync();
        }
    }

    [RelayCommand]
    public async Task ExportHiderXmlAsync(CollectionItemViewModel? item)
    {
        if (item == null) return;
        try
        {
            var topLevel = TopLevel.GetTopLevel(App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
            if (topLevel?.StorageProvider != null)
            {
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Hider XML (Batch Resource Editor format)",
                    DefaultExtension = "xml",
                    SuggestedFileName = $"HIDER_{item.Name}.xml",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("XML Files (*.xml)") { Patterns = new[] { "*.xml" } },
                        new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
                    }
                });

                if (file != null)
                {
                    string xmlContent = await _collectionService.ExportHiderXmlAsync(item.Id, item.Name, item.Model.LinkedSetIds, includedItemIds: item.Model.LinkedItemIds);
                    await using var stream = await file.OpenWriteAsync();
                    await using var writer = new StreamWriter(stream, Encoding.UTF8);
                    await writer.WriteAsync(xmlContent);

                    ShowCacheToast($"Exported hider XML to {file.Name}", isError: false);
                }
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Failed to export XML: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task ExportCurrentEditingHiderXmlAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
            if (topLevel?.StorageProvider != null)
            {
                string name = string.IsNullOrWhiteSpace(EditingCollectionName) ? "Collection" : EditingCollectionName.Trim();
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Hider XML (Batch Resource Editor format)",
                    DefaultExtension = "xml",
                    SuggestedFileName = $"HIDER_{name}.xml",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("XML Files (*.xml)") { Patterns = new[] { "*.xml" } },
                        new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
                    }
                });

                if (file != null)
                {
                    var selectedSets = AssignedSets.Select(s => s.SetId).ToList();
                    ulong collId = EditingCollectionId ?? 0;
                    string xmlContent = await _collectionService.ExportHiderXmlAsync(collId, name, selectedSets, includedItemIds: IncludedItems.Select(i => i.ItemId).ToList());
                    await using var stream = await file.OpenWriteAsync();
                    await using var writer = new StreamWriter(stream, Encoding.UTF8);
                    await writer.WriteAsync(xmlContent);

                    ShowCacheToast($"Exported hider XML to {file.Name}", isError: false);
                }
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Failed to export XML: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task DeleteCollectionAsync(CollectionItemViewModel? item)
    {
        if (item == null) return;
        try
        {
            bool ok = _collectionService.DeleteCollection(item.Id);
            if (ok)
            {
                ShowCacheToast($"Deleted collection '{item.Name}'.", isError: false);
                await LoadCollectionsAsync();
            }
            else
            {
                ShowCacheToast($"Failed to delete collection '{item.Name}'.", isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error deleting collection: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public void OpenCollectionsFolder()
    {
        try
        {
            string folder = _collectionService.GetCollectionsFolderPath();
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowCacheToast("Sims 3 Collections folder not found on disk.", isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Failed to open folder: {ex.Message}", isError: true);
        }
    }

    private void ApplyCollectionFilter()
    {
        var query = CollectionSearchQuery.Trim();

        var filtered = AllCollections.Where(c =>
        {
            if (!string.IsNullOrEmpty(query))
            {
                bool matchesName = c.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool matchesIcon = c.IconName.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matchesName && !matchesIcon) return false;
            }
            return true;
        }).ToList();

        FilteredCollections.ReplaceAll(filtered);

        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(IsNoCollectionsFound));
    }

    private void ApplyIconFilter()
    {
        var query = IconSearchQuery.Trim();

        var filtered = AllIcons.Where(ic =>
        {
            if (IconCategoryFilter != "All" && !ic.Category.Equals(IconCategoryFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(query))
            {
                bool matchesName = ic.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool matchesCat = ic.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matchesName && !matchesCat) return false;
            }
            return true;
        }).ToList();

        FilteredIcons.ReplaceAll(filtered);
    }

    #endregion
}
