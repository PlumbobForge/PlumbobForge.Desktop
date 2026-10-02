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
using IconPacks.Avalonia.BoxIcons;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
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

    public CollectionSetOptionViewModel(CollectionSetOption model, bool isSelected = false)
    {
        Model = model;
        _isSelected = isSelected;
        ThemeService.AccentChanged += () =>
        {
            OnPropertyChanged(nameof(IconBrush));
            OnPropertyChanged(nameof(HasCustomColor));
        };
    }
}

public partial class HealthViewModel
{
    #region Collection Creator Properties & Commands

    public ObservableCollection<CollectionItemViewModel> AllCollections { get; } = new();
    public RangeObservableCollection<CollectionItemViewModel> FilteredCollections { get; } = new();
    public ObservableCollection<CollectionSetOptionViewModel> AvailableSetOptions { get; } = new();
    public ObservableCollection<CollectionSetOptionViewModel> AssignedSets { get; } = new();
    public RangeObservableCollection<CollectionSetOptionViewModel> AvailableToAddSets { get; } = new();
    public ObservableCollection<CollectionIconItemViewModel> AllIcons { get; } = new();
    public RangeObservableCollection<CollectionIconItemViewModel> FilteredIcons { get; } = new();

    [ObservableProperty]
    private bool _isLoadingCollections = false;

    [ObservableProperty]
    private bool _hasLoadedCollections = false;

    [ObservableProperty]
    private bool _isCollectionEditorOpen = false;

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

    public int SelectedSetsItemCount => AssignedSets.Sum(s => s.ItemCount);
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
    public bool IsIconCategoryFurniture => IconCategoryFilter == "Furniture";
    public bool IsIconCategorySkills => IconCategoryFilter == "Skills";
    public bool IsIconCategoryCareers => IconCategoryFilter == "Careers";
    public bool IsIconCategoryPets => IconCategoryFilter == "Pets";
    public bool IsIconCategoryAdventures => IconCategoryFilter == "Adventures";
    public bool IsIconCategorySymbols => IconCategoryFilter == "Symbols";

    partial void OnIconCategoryFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsIconCategoryAll));
        OnPropertyChanged(nameof(IsIconCategoryFurniture));
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
        AllIcons.Clear();
        var icons = _collectionService.GetStandardIcons();
        foreach (var ic in icons)
        {
            var vm = new CollectionIconItemViewModel(ic);
            if (SelectedIcon != null && vm.Key.Instance == SelectedIcon.Key.Instance)
            {
                vm.IsActiveSelection = true;
            }
            AllIcons.Add(vm);
        }
        ApplyIconFilter();
    }

    [RelayCommand]
    public void SetIconCategoryFilter(string category)
    {
        IconCategoryFilter = category;
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
            AllCollections.Clear();
            int totalItems = 0;

            foreach (var c in list)
            {
                totalItems += c.ItemCount;
                AllCollections.Add(new CollectionItemViewModel(c));
            }

            TotalCollectionsCount = AllCollections.Count;
            TotalCollectionItemsCount = totalItems;
            HasLoadedCollections = true;

            InitCollectionIcons();
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

        await LoadAvailableSetOptionsAsync();

        OnPropertyChanged(nameof(IsEditingExistingCollection));
        OnPropertyChanged(nameof(CollectionEditorTitle));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));

        IsCollectionEditorOpen = true;
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

        await LoadAvailableSetOptionsAsync(item.Model.LinkedSetIds);

        OnPropertyChanged(nameof(IsEditingExistingCollection));
        OnPropertyChanged(nameof(CollectionEditorTitle));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));

        IsCollectionEditorOpen = true;
    }

    private async Task LoadAvailableSetOptionsAsync(List<long>? preSelectedSetIds = null)
    {
        AvailableSetOptions.Clear();
        AssignedSets.Clear();
        SetSearchQuery = string.Empty;

        var setOptions = await _collectionService.GetAvailableSetOptionsAsync();
        foreach (var s in setOptions)
        {
            if (preSelectedSetIds != null && preSelectedSetIds.Contains(s.SetId))
            {
                s.IsSelected = true;
            }

            var vm = new CollectionSetOptionViewModel(s);
            AvailableSetOptions.Add(vm);
            if (vm.IsSelected)
            {
                AssignedSets.Add(vm);
            }
        }

        RefreshAvailableToAddSets();
        OnPropertyChanged(nameof(HasAssignedSets));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));
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
        AssignedSets.Clear();
        foreach (var opt in AvailableSetOptions)
        {
            opt.IsSelected = true;
            AssignedSets.Add(opt);
        }
        RefreshAvailableToAddSets();
        OnPropertyChanged(nameof(HasAssignedSets));
        OnPropertyChanged(nameof(SelectedSetsItemCount));
        OnPropertyChanged(nameof(SelectedSetsCount));
    }

    [RelayCommand]
    public void DeselectAllSetsForCollection()
    {
        AssignedSets.Clear();
        foreach (var opt in AvailableSetOptions)
        {
            opt.IsSelected = false;
        }
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
                EditingHideFromCatalog);

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
                    string xmlContent = await _collectionService.ExportHiderXmlAsync(item.Id, item.Name, item.Model.LinkedSetIds);
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
                    string xmlContent = await _collectionService.ExportHiderXmlAsync(collId, name, selectedSets);
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
