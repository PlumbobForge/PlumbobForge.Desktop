using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Utils;
using S3ForgeTools.GameFiles.Package;

namespace PlumbobForge.Desktop.ViewModels;

public sealed class LookInsideSource
{
    public long ItemId { get; }
    public string FileName { get; }
    public PackageInspection Inspection { get; }
    /// <summary>Encrypted, damaged or partly missing items can be looked at but not rewritten.</summary>
    public bool EditBlocked { get; }

    public LookInsideSource(long itemId, string fileName, PackageInspection inspection)
    {
        ItemId = itemId;
        FileName = fileName;
        Inspection = inspection;
        EditBlocked = inspection.Warnings.Any(w => w.Key is "look_inside.warning_encrypted" or "look_inside.warning_corrupt" or "look_inside.warning_missing_shard");
    }
}

public sealed partial class LookInsideItemOption : ObservableObject
{
    public long? ItemId { get; }
    public string Label { get; }
    public bool IsAll => ItemId == null;

    private readonly ItemThumbnail? _thumbnail;
    public Bitmap? Thumbnail => _thumbnail?.Get();
    public bool ShowPackageIcon => !IsAll && Thumbnail == null;

    [ObservableProperty]
    private bool _isSelected;

    public LookInsideItemOption(long? itemId, string label)
    {
        ItemId = itemId;
        Label = label;
        if (itemId is long id)
        {
            _thumbnail = new ItemThumbnail(id, () =>
            {
                OnPropertyChanged(nameof(Thumbnail));
                OnPropertyChanged(nameof(ShowPackageIcon));
            });
        }
    }

    public override string ToString() => Label;
}

public partial class LookInsideViewModel : ObservableObject, IDisposable
{
    public const string FilterAll = "All";
    public const string FilterCas = "Cas";
    public const string FilterBuildBuy = "BuildBuy";

    private readonly IReadOnlyList<(long Id, string FileName)> _items;
    private readonly CancellationTokenSource _cts = new();
    private List<LookInsideSource> _sources = new();
    private List<LookInsideObjectItem> _allObjects = new();

    private static string L(string key) => LocalizationManager.Instance.GetString(key);
    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    public string FileName { get; }

    public bool HasMultipleItems => _items.Count > 1;
    public bool HasSingleItem => !HasMultipleItems;
    public List<LookInsideItemOption> ItemOptions { get; }

    [ObservableProperty]
    private LookInsideItemOption _selectedItemOption;

    public HashSet<long> SavedItemIds { get; } = new();

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _kindFilter = FilterAll;

    [ObservableProperty]
    private LookInsideObjectItem? _selectedObject;

    [ObservableProperty]
    private LookInsidePartOption? _selectedPart;

    public RangeObservableCollection<LookInsideObjectItem> VisibleObjects { get; } = new();
    public List<LookInsidePartOption> PartOptions { get; private set; } = new();
    public List<string> Warnings { get; private set; } = new();
    public List<LookInsideImage> PackPreviews { get; private set; } = new();

    public bool IsReady => !IsLoading && ErrorMessage == null;
    public bool HasError => ErrorMessage != null;
    public bool HasWarnings => Warnings.Count > 0;
    public bool HasMultipleParts => PartOptions.Count > 2;
    public bool HasPackPreviews => PackPreviews.Count > 0;
    public bool HasNoObjects => IsReady && _allObjects.Count == 0;
    public bool HasSelection => SelectedObject != null && !SelectedObject.IsAllItems;
    public bool IsAllItemsSelected => SelectedObject?.IsAllItems == true;

    private LookInsideObjectItem? _allItemsEntry;
    public LookInsideAllItems? AllItems { get; private set; }
    public bool ShowSelectPrompt => IsReady && SelectedObject == null && _allObjects.Count > 0;

    public bool HasMultipleKinds { get; private set; }
    public string SummaryText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport), nameof(CanEdit), nameof(CanSave))]
    private bool _isExporting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport), nameof(CanEdit), nameof(CanSave))]
    private bool _isSaving;

    [ObservableProperty]
    private bool _showUnsavedPrompt;

    [ObservableProperty]
    private bool _showCacheNote;

    public bool ChangesSaved => SavedItemIds.Count > 0;

    private IEnumerable<LookInsideSource> SelectionSources =>
        SelectedObject == null ? Enumerable.Empty<LookInsideSource>()
        : SelectedObject.IsAllItems ? _sources
        : SelectedObject.Source != null ? new[] { SelectedObject.Source } : Enumerable.Empty<LookInsideSource>();

    // Editing is blocked for items that are encrypted, damaged or partly missing.
    private bool SelectionEditBlocked => _sources.Count == 1 ? _sources[0].EditBlocked : SelectionSources.Any(s => s.EditBlocked);

    public bool CanEdit => _sources.Count > 0 && !SelectionEditBlocked && !IsSaving && !IsExporting;
    public bool CanSave => _sources.Count > 0 && !IsSaving && !IsExporting && HasPendingChanges;
    public int PendingCount => _allObjects.Sum(o => o.Thumbnails.Count(t => t.IsChanged) + (o.HasToggleChange ? 1 : 0) + (o.HasCasFlagChange ? 1 : 0));

    [ObservableProperty]
    private bool _showOnlyOff;

    public int OffCount => _allObjects.Count(o => o.IsOff);
    public bool HasOffObjects => OffCount > 0;
    public string OffFilterText => L("look_inside.filter_off", OffCount);
    public bool AllObjectsOff => HasSingleItem && _allObjects.Any(o => o.Object != null) && _allObjects.Where(o => o.Object != null).All(o => o.IsOff);

    [RelayCommand]
    private void ToggleOffFilter() => ShowOnlyOff = !ShowOnlyOff;

    partial void OnShowOnlyOffChanged(bool value) => ApplyFilter();
    public bool HasPendingChanges => PendingCount > 0;
    public string PendingText => L("look_inside.pending_count", PendingCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenExportFolder))]
    private string? _lastExportFolder;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
    public bool CanOpenExportFolder => !IsExporting && LastExportFolder != null && Directory.Exists(LastExportFolder);
    public int ThumbnailCount { get; private set; }
    public bool HasAnyThumbnails => ThumbnailCount > 0;
    public bool CanExport => HasAnyThumbnails && !IsExporting && !IsSaving;

    public bool IsFilterAll => KindFilter == FilterAll;
    public bool IsFilterCas => KindFilter == FilterCas;
    public bool IsFilterBuildBuy => KindFilter == FilterBuildBuy;
    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    [RelayCommand]
    private void SetKindFilter(string filter) => KindFilter = filter;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    public LookInsideViewModel(long itemId, string fileName) : this(new[] { (itemId, fileName) })
    {
    }

    public LookInsideViewModel(IReadOnlyList<(long Id, string FileName)> items)
    {
        if (items.Count == 0) throw new ArgumentException("Look Inside needs at least one item", nameof(items));
        _items = items;
        FileName = items[0].FileName;
        ItemOptions = new List<LookInsideItemOption> { new(null, L("look_inside.all_selected_items", items.Count)) };
        ItemOptions.AddRange(items.Select(i => new LookInsideItemOption(i.Id, i.FileName)));
        _selectedItemOption = ItemOptions[0];
        _selectedItemOption.IsSelected = true;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        var sources = new List<LookInsideSource>();
        var failures = new List<string>();
        try
        {
            using var scope = App.Services?.CreateScope();
            var inspector = scope?.ServiceProvider.GetRequiredService<PackageInspectorService>()
                ?? throw new InvalidOperationException("Services are not available");
            foreach (var (id, fileName) in _items)
            {
                try
                {
                    var inspection = await inspector.InspectAsync(id, _cts.Token);
                    if (_cts.IsCancellationRequested)
                    {
                        inspection.Dispose();
                        break;
                    }
                    sources.Add(new LookInsideSource(id, fileName, inspection));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"Look Inside failed for item {id}", ex, "LookInsideViewModel");
                    failures.Add(ErrorText(ex));
                    if (_items.Count > 1) failures[^1] = $"{fileName}: {failures[^1]}";
                }
            }

            if (_cts.IsCancellationRequested)
            {
                foreach (var s in sources) s.Inspection.Dispose();
                return;
            }
            _sources = sources;
            if (sources.Count == 0)
            {
                // Nothing could be read: show the (first) reason.
                ErrorMessage = failures.FirstOrDefault() ?? L("look_inside.error_read", string.Empty);
                return;
            }
            Populate(failures);
        }
        catch (Exception ex)
        {
            foreach (var s in sources) s.Inspection.Dispose();
            AppLogger.LogError("Look Inside failed", ex, "LookInsideViewModel");
            ErrorMessage = ErrorText(ex);
        }
        finally
        {
            IsLoading = false;
            NotifyStateChanged();
        }
    }

    private static string ErrorText(Exception ex) => ex switch
    {
        FileNotFoundException => L("look_inside.error_missing_file"),
        // Truncated or empty packages fail while reading the header or index
        EndOfStreamException or InvalidDataException => L("look_inside.error_damaged"),
        _ => L("look_inside.error_read", ex.Message)
    };

    private void Populate(List<string> failures)
    {
        bool multiItem = HasMultipleItems;
        _allObjects = new List<LookInsideObjectItem>();
        var unlinked = new List<LookInsideObjectItem>();
        foreach (var source in _sources)
        {
            var inspection = source.Inspection;
            Func<InspectedResource, byte[]> reader = inspection.Read;
            bool multiPart = inspection.Parts.Count > 1;
            var partLabels = inspection.Parts.ToDictionary(p => p.Index, p => p.Label);

            foreach (var o in inspection.Objects)
            {
                string? partLabel = multiPart && partLabels.TryGetValue(o.Resource.PartIndex, out var label) ? label : null;
                // With several items, say which item each object comes from.
                string? origin = multiItem ? (partLabel != null ? $"{source.FileName} · {partLabel}" : source.FileName) : partLabel;
                _allObjects.Add(new LookInsideObjectItem(o, reader, origin) { Source = source });
            }
            if (inspection.UnlinkedThumbnails.Count > 0)
            {
                unlinked.Add(LookInsideObjectItem.ForUnlinked(inspection.UnlinkedThumbnails, reader, source, multiItem ? source.FileName : null));
            }
        }
        _allObjects = _allObjects
            .OrderBy(o => o.Object!.Kind)
            .ThenBy(o => o.Title, StringComparer.CurrentCultureIgnoreCase)
            .Concat(unlinked)
            .ToList();
        foreach (var obj in _allObjects) obj.Changed += NotifyPendingChanged;
        RebuildAllItemsEntry();

        Warnings = _sources
            .SelectMany(s => s.Inspection.Warnings.Select(w => multiItem ? $"{s.FileName}: {L(w.Key, w.Args)}" : L(w.Key, w.Args)))
            .Concat(failures.Select(f => L("look_inside.item_not_read", f)))
            .ToList();
        PackPreviews = _sources.SelectMany(s => s.Inspection.PackPreviews).Select(bytes => new LookInsideImage(() => bytes)).ToList();

        int cas = _sources.Sum(s => s.Inspection.Objects.Count(o => o.Kind == InspectedObjectKind.Cas));
        int bb = _sources.Sum(s => s.Inspection.Objects.Count(o => o.Kind == InspectedObjectKind.BuildBuy));
        var parts = new List<string>();
        if (multiItem) parts.Add(L("look_inside.summary_items", _sources.Count));
        else if (_sources[0].Inspection.Parts.Count > 1) parts.Add(L("look_inside.summary_parts", _sources[0].Inspection.Parts.Count));
        if (cas > 0) parts.Add(L("look_inside.summary_cas", cas));
        if (bb > 0) parts.Add(L("look_inside.summary_bb", bb));
        parts.Add(CacheGroupInfo.FormatBytes(_sources.Sum(s => s.Inspection.TotalBytes)));
        SummaryText = string.Join("  ·  ", parts);
        HasMultipleKinds = cas > 0 && bb > 0;
        if (!HasMultipleKinds) KindFilter = FilterAll;
        ThumbnailCount = _sources.Sum(s => s.Inspection.Objects.Sum(o => o.Thumbnails.Count) + s.Inspection.UnlinkedThumbnails.Count + s.Inspection.PackPreviews.Count);

        RebuildPartOptions();
        ApplyFilter();
        SelectedObject = VisibleObjects.FirstOrDefault(o => !o.IsAllItems);
    }

    private IEnumerable<LookInsideObjectItem> ObjectsOfChosenItem =>
        SelectedItemOption?.ItemId is long id ? _allObjects.Where(o => o.Source?.ItemId == id) : _allObjects;

    private void RebuildAllItemsEntry()
    {
        var realObjects = ObjectsOfChosenItem.Where(o => o.Object != null).ToList();
        if (realObjects.Count > 1)
        {
            AllItems = new LookInsideAllItems(realObjects);
            _allItemsEntry = LookInsideObjectItem.ForAll(realObjects);
        }
        else
        {
            AllItems = null;
            _allItemsEntry = null;
        }
        OnPropertyChanged(nameof(AllItems));
    }

    private void RebuildPartOptions()
    {
        var source = SelectedItemOption?.ItemId is long id ? _sources.FirstOrDefault(s => s.ItemId == id)
            : _sources.Count == 1 ? _sources[0] : null;
        PartOptions = new List<LookInsidePartOption> { new(-1, L("look_inside.all_packages")) };
        if (source != null) PartOptions.AddRange(source.Inspection.Parts.Select(p => new LookInsidePartOption(p.Index, p.Label)));
        SelectedPart = PartOptions[0];
        OnPropertyChanged(nameof(PartOptions));
        OnPropertyChanged(nameof(HasMultipleParts));
    }

    partial void OnSelectedItemOptionChanged(LookInsideItemOption value)
    {
        foreach (var option in ItemOptions) option.IsSelected = ReferenceEquals(option, value);
        if (_sources.Count == 0) return;
        RebuildAllItemsEntry();
        RebuildPartOptions();
        ApplyFilter();
        if (SelectedObject == null || !VisibleObjects.Contains(SelectedObject))
        {
            SelectedObject = VisibleObjects.FirstOrDefault(o => !o.IsAllItems);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        ApplyFilter();
    }

    partial void OnKindFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterCas));
        OnPropertyChanged(nameof(IsFilterBuildBuy));
        ApplyFilter();
    }

    partial void OnSelectedPartChanged(LookInsidePartOption? value) => ApplyFilter();

    partial void OnSelectedObjectChanged(LookInsideObjectItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsAllItemsSelected));
        OnPropertyChanged(nameof(ShowSelectPrompt));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsEditBlocked));
    }

    private void ApplyFilter()
    {
        string query = SearchText.Trim();
        int part = SelectedPart?.Index ?? -1;
        var filtered = ObjectsOfChosenItem.Where(o =>
        {
            if (o.Object == null)
            {
                return KindFilter == FilterAll && part < 0 && query.Length == 0 && !ShowOnlyOff;
            }
            if (ShowOnlyOff && !o.IsOff) return false;
            if (KindFilter == FilterCas && o.Object.Kind != InspectedObjectKind.Cas) return false;
            if (KindFilter == FilterBuildBuy && o.Object.Kind != InspectedObjectKind.BuildBuy) return false;
            if (part >= 0 && o.Object.Resource.PartIndex != part) return false;
            return query.Length == 0 || o.Matches(query);
        });
        if (_allItemsEntry != null && KindFilter == FilterAll && part < 0 && query.Length == 0 && !ShowOnlyOff)
        {
            filtered = filtered.Prepend(_allItemsEntry);
        }
        VisibleObjects.ReplaceAll(filtered);
        if (SelectedObject != null && !VisibleObjects.Contains(SelectedObject))
        {
            SelectedObject = null;
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(HasMultipleParts));
        OnPropertyChanged(nameof(PartOptions));
        OnPropertyChanged(nameof(SelectedPart));
        OnPropertyChanged(nameof(PackPreviews));
        OnPropertyChanged(nameof(HasPackPreviews));
        OnPropertyChanged(nameof(HasNoObjects));
        OnPropertyChanged(nameof(ShowSelectPrompt));
        OnPropertyChanged(nameof(HasMultipleKinds));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasAnyThumbnails));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsEditBlocked));
        NotifyPendingChanged();
    }

    public bool IsEditBlocked => IsReady && SelectionEditBlocked;

    private void NotifyPendingChanged()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(PendingText));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(OffCount));
        OnPropertyChanged(nameof(HasOffObjects));
        OnPropertyChanged(nameof(OffFilterText));
        OnPropertyChanged(nameof(AllObjectsOff));
        AllItems?.Refresh();
        if (!HasPendingChanges) ShowUnsavedPrompt = false;
        // Nothing is off any more: the "turned off" list would be empty.
        if (ShowOnlyOff && !HasOffObjects) ShowOnlyOff = false;
    }

    #region Editing

    private LookInsideSource? SourceOf(LookInsideThumbnailItem item) =>
        _allObjects.FirstOrDefault(o => o.Thumbnails.Contains(item))?.Source;

    public async Task StageReplaceAsync(LookInsideThumbnailItem item, string imagePath)
    {
        await StageImageAsync(new[] { item }, imagePath);
    }

    public async Task StageReplaceAllAsync(LookInsideObjectItem obj, string imagePath)
    {
        await StageImageAsync(obj.Thumbnails.ToList(), imagePath);
    }

    public async Task StageAddAsync(LookInsideObjectItem obj, string imagePath)
    {
        if (!CanEdit || !obj.CanAddThumbnails) return;
        var slots = obj.AddNewSlots();
        if (!await StageImageAsync(slots, imagePath))
        {
            obj.DiscardChanges();
        }
    }

    public void StageRemove(LookInsideThumbnailItem item)
    {
        if (CanEdit) item.StageRemove();
    }

    public void Undo(LookInsideThumbnailItem item)
    {
        SelectedObject?.Undo(item);
    }

    public void DiscardChanges()
    {
        foreach (var obj in _allObjects) obj.DiscardChanges();
        StatusMessage = null;
    }

    private async Task<bool> StageImageAsync(IReadOnlyList<LookInsideThumbnailItem> targets, string imagePath)
    {
        if (_sources.Count == 0 || !CanEdit || targets.Count == 0) return false;
        var readers = targets.Select(t => t.IsNewSlot ? null : SourceOf(t)?.Inspection).ToList();
        try
        {
            var images = await Task.Run(() =>
            {
                byte[] source = File.ReadAllBytes(imagePath);
                if (ThumbnailImageTools.ReadSize(source) == null) throw new InvalidDataException("Not an image");
                return targets.Select((t, i) =>
                {
                    var (width, height) = t.IsNewSlot
                        ? (t.NewSlotSize, t.NewSlotSize)
                        : (readers[i] != null ? ThumbnailImageTools.ReadSize(readers[i]!.Read(t.Thumbnail.Resource)) : null) ?? (256, 256);
                    return ThumbnailImageTools.CreateThumbnail(source, width, height);
                }).ToList();
            });
            for (int i = 0; i < targets.Count; i++) targets[i].StageNewImage(images[i]);
            StatusMessage = null;
            return true;
        }
        catch (InvalidDataException)
        {
            StatusMessage = L("look_inside.image_invalid");
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Could not prepare thumbnail image {imagePath}", ex, "LookInsideViewModel");
            StatusMessage = L("look_inside.image_invalid");
        }
        return false;
    }

    private sealed record ItemSave(LookInsideSource Source, List<PartEdits> Parts, List<ObjectToggle> Toggles, int ThumbnailCount, int CasCount);

    public async Task SaveAsync()
    {
        if (_sources.Count == 0 || !CanSave) return;

        if (Sims3GameProcess.IsGameRunning())
        {
            StatusMessage = L("look_inside.game_running");
            return;
        }

        // Group the staged changes by item; CAS flag edits patch the CASP, so they are read now, while the items are open.
        var saves = new List<ItemSave>();
        foreach (var source in _sources)
        {
            var inspection = source.Inspection;
            var objects = _allObjects.Where(o => o.Source == source).ToList();

            var byPart = new Dictionary<int, List<ResourceEdit>>();
            foreach (var t in objects.SelectMany(o => o.Thumbnails).Where(t => t.IsChanged))
            {
                if (t.Change != ThumbnailChange.Remove && t.NewData == null) continue;
                int part = t.Thumbnail.Resource.PartIndex;
                if (!byPart.TryGetValue(part, out var list)) byPart[part] = list = new List<ResourceEdit>();
                list.Add(new ResourceEdit(t.Thumbnail.Resource.Key, t.Change == ThumbnailChange.Remove ? null : t.NewData));
            }
            int thumbnailCount = byPart.Values.Sum(l => l.Count);

            int casCount = 0;
            try
            {
                foreach (var casPart in CasFlagsService.BuildEdits(inspection, objects.Where(o => o.HasCasFlagChange).Select(o => (o.Object!, o.StagedCasCategory))))
                {
                    if (!byPart.TryGetValue(casPart.PartIndex, out var list)) byPart[casPart.PartIndex] = list = new List<ResourceEdit>();
                    list.AddRange(casPart.Edits);
                    casCount += casPart.Edits.Count;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Could not prepare CAS flag changes for item {source.ItemId}", ex, "LookInsideViewModel");
                StatusMessage = L("look_inside.save_failed", HasMultipleItems ? $"{source.FileName}: {ex.Message}" : ex.Message);
                return;
            }

            var parts = byPart
                .Select(p => new PartEdits(p.Key, inspection.Parts.FirstOrDefault(x => x.Index == p.Key)?.MetaEntityId, p.Value))
                .ToList();
            var toggles = objects
                .Where(o => o.HasToggleChange)
                .Select(o => new ObjectToggle(o.Object!.Resource.Key, o.IsEnabledInGame))
                .ToList();
            if (parts.Count > 0 || toggles.Count > 0)
            {
                saves.Add(new ItemSave(source, parts, toggles, thumbnailCount, casCount));
            }
        }

        int count = saves.Sum(s => s.ThumbnailCount + s.CasCount + s.Toggles.Count);
        bool needsRebuild = saves.Any(s => (s.Toggles.Count > 0 || s.CasCount > 0) && !s.Source.Inspection.IsVault);
        bool thumbnailsSaved = false;
        string? selectedKey = SelectedObject?.KeyText;

        IsSaving = true;
        ShowUnsavedPrompt = false;
        StatusMessage = L("look_inside.saving");
        string? failure = null;

        // The library files must not be held open while they are replaced.
        var sources = _sources;
        _sources = new List<LookInsideSource>();
        foreach (var s in sources) s.Inspection.Dispose();

        using (var scope = App.Services?.CreateScope())
        {
            foreach (var save in saves)
            {
                long itemId = save.Source.ItemId;
                bool contentSaved = false;
                try
                {
                    var editor = scope?.ServiceProvider.GetRequiredService<PackageEditService>()
                        ?? throw new InvalidOperationException("Services are not available");
                    // Thumbnails and CAS flags first: if their rewrite fails, nothing at all has changed.
                    if (save.Parts.Count > 0)
                    {
                        if (save.CasCount > 0) await scope!.ServiceProvider.GetRequiredService<CasFlagsService>().ApplyEditsAsync(itemId, save.Parts);
                        else await editor.ApplyAsync(itemId, save.Parts);
                        contentSaved = true;
                        SavedItemIds.Add(itemId);
                        if (save.ThumbnailCount > 0) thumbnailsSaved = true;
                    }
                    if (save.Toggles.Count > 0) await editor.SetObjectsEnabledAsync(itemId, save.Toggles);
                    SavedItemIds.Add(itemId);
                }
                catch (GameRunningException)
                {
                    failure ??= L("look_inside.game_running_unsaved");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"Could not save Look Inside changes for item {itemId}", ex, "LookInsideViewModel");
                    string message = HasMultipleItems ? $"{save.Source.FileName}: {ex.Message}" : ex.Message;
                    failure ??= contentSaved
                        ? L("look_inside.save_toggles_failed", message)
                        : L("look_inside.save_failed", message);
                }
            }
        }

        await LoadAsync();
        IsSaving = false;
        if (selectedKey != null)
        {
            SelectedObject = VisibleObjects.FirstOrDefault(o => o.KeyText == selectedKey) ?? SelectedObject;
        }
        StatusMessage = failure
            ?? (needsRebuild ? L("look_inside.saved_rebuild", count) : L("look_inside.saved", count));
        ShowCacheNote = thumbnailsSaved;
    }

    #endregion

    public string GetSuggestedFileName(LookInsideThumbnailItem item) =>
        SourceOf(item) is { } source ? ThumbnailExporter.GetSuggestedFileName(source.Inspection, item.Owner, item.Thumbnail) : "thumbnail.png";

    /// <summary>Saves one thumbnail, as stored in the package, to <paramref name="destinationPath"/>.</summary>
    public async Task ExportThumbnailAsync(LookInsideThumbnailItem item, string destinationPath)
    {
        var source = SourceOf(item);
        if (source == null || IsExporting) return;

        IsExporting = true;
        try
        {
            await Task.Run(() => ThumbnailExporter.ExportOne(source.Inspection, item.Thumbnail, destinationPath), _cts.Token);
            StatusMessage = L("look_inside.export_done_one", Path.GetFileName(destinationPath));
            LastExportFolder = Path.GetDirectoryName(destinationPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.LogError($"Could not export a thumbnail of item {source.ItemId} to {destinationPath}", ex, "LookInsideViewModel");
            StatusMessage = L("look_inside.export_failed", ex.Message);
        }
        finally
        {
            IsExporting = false;
            OnPropertyChanged(nameof(CanOpenExportFolder));
        }
    }


    public async Task ExportManyAsync(string folder, LookInsideObjectItem? onlyObject = null)
    {
        if (_sources.Count == 0 || IsExporting) return;

        var targets = onlyObject != null
            ? (onlyObject.Source != null ? new List<LookInsideSource> { onlyObject.Source } : new List<LookInsideSource>())
            : SelectedItemOption?.ItemId is long id ? _sources.Where(s => s.ItemId == id).ToList() : _sources.ToList();
        if (targets.Count == 0) return;

        IsExporting = true;
        StatusMessage = null;
        var progress = new Progress<(int Done, int Total)>(p => StatusMessage = L("look_inside.exporting", p.Done, p.Total));
        try
        {
            var only = onlyObject?.Thumbnails.Where(t => !t.IsNewSlot).Select(t => (t.Owner, t.Thumbnail)).ToList();
            var (exported, failed, lastFolder) = await Task.Run(() =>
            {
                int done = 0, bad = 0;
                string? last = null;
                foreach (var source in targets)
                {
                    string target = only == null ? ThumbnailExporter.CreateExportFolder(folder, source.FileName) : folder;
                    var result = ThumbnailExporter.ExportMany(source.Inspection, target, only, progress, _cts.Token);
                    done += result.Exported;
                    bad += result.Failed;
                    last = result.Folder;
                }
                return (done, bad, last);
            }, _cts.Token);

            // Several items each get their own folder: open the folder that holds them.
            LastExportFolder = targets.Count > 1 ? folder : lastFolder;
            StatusMessage = failed == 0
                ? L("look_inside.export_done", exported, LastExportFolder ?? folder)
                : L("look_inside.export_done_with_failures", exported, failed);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Could not export thumbnails to {folder}", ex, "LookInsideViewModel");
            StatusMessage = L("look_inside.export_failed", ex.Message);
        }
        finally
        {
            IsExporting = false;
            OnPropertyChanged(nameof(CanOpenExportFolder));
        }
    }

    public void OpenLastExportFolder()
    {
        if (LastExportFolder == null || !Directory.Exists(LastExportFolder)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{LastExportFolder}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var s in _sources) s.Inspection.Dispose();
        _sources = new List<LookInsideSource>();
    }
}

public static class LookInsideConverters
{
    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush?> ChangedBorder =
        new(changed => FindBrush(changed ? "AccentBrush" : "BorderSubtle"));

    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, double> RemovedOpacity =
        new(removed => removed ? 0.3 : 1.0);

    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, double> OffOpacity =
        new(off => off ? 0.5 : 1.0);

    private static Avalonia.Media.IBrush? FindBrush(string key) =>
        Avalonia.Application.Current is { } app && Avalonia.Controls.ResourceNodeExtensions.TryFindResource(app, key, app.ActualThemeVariant, out var value)
            ? value as Avalonia.Media.IBrush
            : null;
}

public sealed record LookInsidePartOption(int Index, string Label)
{
    public override string ToString() => Label;
}

public partial class LookInsideImage : ObservableObject
{
    private static readonly SemaphoreSlim DecodeGate = new(4, 4);
    private readonly Func<byte[]> _read;
    private Bitmap? _bitmap;
    private bool _requested;

    [ObservableProperty]
    private bool _failed;

    [ObservableProperty]
    private string _dimensionsText = string.Empty;

    public int ByteLength { get; private set; }

    public LookInsideImage(Func<byte[]> read)
    {
        _read = read;
    }

    public Bitmap? Bitmap
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _ = LoadAsync();
            }
            return _bitmap;
        }
    }

    private async Task LoadAsync()
    {
        await DecodeGate.WaitAsync();
        try
        {
            var (bitmap, length) = await Task.Run(() =>
            {
                byte[] bytes = _read();
                using var ms = new MemoryStream(bytes, writable: false);
                return (new Bitmap(ms), bytes.Length);
            });
            Dispatcher.UIThread.Post(() =>
            {
                _bitmap = bitmap;
                ByteLength = length;
                DimensionsText = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}";
                OnPropertyChanged(nameof(Bitmap));
            });
        }
        catch
        {
            Dispatcher.UIThread.Post(() => Failed = true);
        }
        finally
        {
            DecodeGate.Release();
        }
    }
}

public enum ThumbnailChange
{
    None,
    Replace,
    Remove,
    Add
}

public sealed partial class LookInsideThumbnailItem : ObservableObject
{
    private LookInsideImage? _newImage;

    public InspectedObject? Owner { get; init; }
    public InspectedThumbnail Thumbnail { get; }
    public LookInsideImage Image { get; }
    public string SizeLabel { get; }
    public string Caption { get; }
    public string Details { get; }
    public bool IsMatchedByPackage => Thumbnail.IsMatchedByPackage;

    public bool IsNewSlot { get; }
    public int NewSlotSize { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged), nameof(IsRemoved), nameof(CanRemove), nameof(ShowRemove), nameof(ChangeText), nameof(HasChangeText), nameof(DisplayImage))]
    private ThumbnailChange _change;

    public bool ShowRemove => !IsNewSlot && Change == ThumbnailChange.None;
    public bool CanExport => !IsNewSlot;

    public byte[]? NewData { get; private set; }

    public LookInsideImage DisplayImage => _newImage ?? Image;
    public bool IsChanged => Change != ThumbnailChange.None;
    public bool IsRemoved => Change == ThumbnailChange.Remove;
    public bool CanRemove => !IsNewSlot && Change != ThumbnailChange.Remove;
    public bool HasChangeText => IsChanged;
    public string ChangeText => Change switch
    {
        ThumbnailChange.Replace => LocalizationManager.Instance.GetString("look_inside.pending_replace"),
        ThumbnailChange.Remove => LocalizationManager.Instance.GetString("look_inside.pending_remove"),
        ThumbnailChange.Add => LocalizationManager.Instance.GetString("look_inside.pending_add"),
        _ => string.Empty
    };

    public event Action? Changed;

    public void StageNewImage(byte[] png)
    {
        NewData = png;
        _newImage = new LookInsideImage(() => png);
        Change = IsNewSlot ? ThumbnailChange.Add : ThumbnailChange.Replace;
        OnPropertyChanged(nameof(DisplayImage));
        Changed?.Invoke();
    }

    public void StageRemove()
    {
        if (IsNewSlot) return;
        NewData = null;
        _newImage = null;
        Change = ThumbnailChange.Remove;
        Changed?.Invoke();
    }

    public void Undo()
    {
        NewData = null;
        _newImage = null;
        Change = ThumbnailChange.None;
        Changed?.Invoke();
    }

    public LookInsideThumbnailItem(InspectedObject owner, TGI_Key key, int size)
        : this(NewSlot(owner, key), _ => throw new InvalidOperationException("New thumbnail has no stored image"), 0, isNewSlot: true)
    {
        Owner = owner;
        NewSlotSize = size;
    }

    private static InspectedThumbnail NewSlot(InspectedObject owner, TGI_Key key)
    {
        Sims3ResourceTypes.TryGetThumbnailFamily(key.Type, out uint family, out int sizeIndex);
        var resource = new InspectedResource { Key = key, PartIndex = owner.Resource.PartIndex };
        return new InspectedThumbnail { Resource = resource, SizeIndex = sizeIndex, FamilyBase = family };
    }

    /// <param name="variant">1-based position of the thumbnail's group among the object's groups, or 0 when there is only one.</param>
    public LookInsideThumbnailItem(InspectedThumbnail thumbnail, Func<InspectedResource, byte[]> reader, int variant, bool isNewSlot = false)
    {
        Thumbnail = thumbnail;
        IsNewSlot = isNewSlot;
        Image = new LookInsideImage(() => reader(thumbnail.Resource));
        var loc = LocalizationManager.Instance;
        SizeLabel = thumbnail.SizeIndex switch
        {
            0 => loc.GetString("look_inside.size_small"),
            1 => loc.GetString("look_inside.size_medium"),
            2 => loc.GetString("look_inside.size_large"),
            _ => loc.GetString("look_inside.size_extra_large")
        };
        Caption = variant > 0 ? $"{SizeLabel} · {loc.GetString("look_inside.variant", variant)}" : SizeLabel;
        Details = isNewSlot
            ? $"{thumbnail.Resource.TypeTag} {thumbnail.Resource.Key.Type:X8}-{thumbnail.Group:X8}"
            : $"{thumbnail.Resource.TypeTag} {thumbnail.Resource.Key.Type:X8}-{thumbnail.Group:X8} · {CacheGroupInfo.FormatBytes(thumbnail.Resource.ResourceLength)}";
    }

    public static List<LookInsideThumbnailItem> CreateAll(InspectedObject owner, Func<InspectedResource, byte[]> reader) =>
        owner.Thumbnails
            .Select(t => new LookInsideThumbnailItem(t, reader, ThumbnailExporter.GetVariantNumber(owner, t)) { Owner = owner })
            .ToList();
}

public sealed partial class CasFlagOption : ObservableObject
{
    public string Label { get; }
    public uint Bit { get; }

    [ObservableProperty]
    private bool _isChecked;

    public event Action? Changed;

    public CasFlagOption(string label, uint bit, bool isChecked)
    {
        Label = label;
        Bit = bit;
        _isChecked = isChecked;
    }

    partial void OnIsCheckedChanged(bool value) => Changed?.Invoke();
}

public sealed class LookInsideObjectItem : ObservableObject
{
    private readonly string _searchBlob;
    private bool _isEnabledInGame;

    public bool IsEnabledInGame
    {
        get => _isEnabledInGame;
        set
        {
            if (Object == null || !SetProperty(ref _isEnabledInGame, value)) return;
            OnPropertyChanged(nameof(IsOff));
            OnPropertyChanged(nameof(HasToggleChange));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(ShowOffNote));
            Changed?.Invoke();
        }
    }

    public bool CanToggle => Object != null;
    public bool IsOff => Object != null && !IsEnabledInGame;
    public bool HasToggleChange => Object != null && IsEnabledInGame == Object.IsDisabled;
    public bool ShowOffNote => IsOff;
    public string OffNoteText => LocalizationManager.Instance.GetString(IsCas ? "look_inside.off_note_cas" : "look_inside.off_note_bb");

    public InspectedObject? Object { get; }
    public LookInsideSource? Source { get; init; }
    public string Title { get; }
    public string Subtitle { get; }
    public bool IsCas => Object?.Kind == InspectedObjectKind.Cas;
    public bool IsBuildBuy => Object?.Kind == InspectedObjectKind.BuildBuy;
    public bool IsUnlinkedGroup => Object == null && !IsAllItems;
    public bool IsAllItems { get; }
    public IReadOnlyList<LookInsideImage> MosaicImages { get; } = Array.Empty<LookInsideImage>();
    public bool ShowAllItemsIcon => IsAllItems && MosaicImages.Count == 0;

    public System.Collections.ObjectModel.ObservableCollection<LookInsideThumbnailItem> Thumbnails { get; }
    public bool HasThumbnails => Thumbnails.Count > 0;
    public bool HasNoThumbnails => Thumbnails.Count == 0;
    public LookInsideImage? Preview => PickPreview(Thumbnails);
    public bool HasChanges => HasToggleChange || HasCasFlagChange || Thumbnails.Any(t => t.IsChanged);

    private bool _validForRandom;

    public IReadOnlyList<CasFlagOption> OutfitOptions { get; } = Array.Empty<CasFlagOption>();

    public bool ValidForRandom
    {
        get => _validForRandom;
        set
        {
            if (SetProperty(ref _validForRandom, value)) NotifyCasFlagsChanged();
        }
    }

    public bool CanEditCasFlags => IsCas && !CouldNotRead && !Object!.HasDuplicateKey;
    public bool ShowDuplicateKeyNote => IsCas && !CouldNotRead && Object!.HasDuplicateKey;
    public string RandomText => LocalizationManager.Instance.GetString(ValidForRandom ? "look_inside.random_yes" : "look_inside.random_no");
    public uint StagedCasCategory
    {
        get
        {
            if (Object == null) return 0;
            uint value = Object.CasCategory & ~CasFlagsService.EditableBits;
            foreach (var option in OutfitOptions) if (option.IsChecked) value |= option.Bit;
            if (ValidForRandom) value |= S3ForgeTools.GameFiles.Resources.CasCategoryFlags.ValidForRandom;
            return value;
        }
    }
    public bool HasCasFlagChange => CanEditCasFlags && StagedCasCategory != Object!.CasCategory;

    private void ResetCasFlags()
    {
        if (!CanEditCasFlags) return;
        foreach (var option in OutfitOptions) option.IsChecked = (Object!.CasCategory & option.Bit) != 0;
        ValidForRandom = (Object!.CasCategory & S3ForgeTools.GameFiles.Resources.CasCategoryFlags.ValidForRandom) != 0;
    }

    private void NotifyCasFlagsChanged()
    {
        OnPropertyChanged(nameof(HasCasFlagChange));
        OnPropertyChanged(nameof(HasChanges));
        Changed?.Invoke();
    }
    public bool CanAddThumbnails => Object != null && !Object.CouldNotRead && Thumbnails.Count == 0 && ThumbnailImageTools.GetNewThumbnailSlots(Object).Count > 0;
    public LookInsideThumbnailItem? SingleThumbnail => Thumbnails.Count == 1 ? Thumbnails[0] : null;
    public bool HasSingleThumbnail => Thumbnails.Count == 1;
    public bool HasThumbnailGrid => Thumbnails.Count > 1;
    public bool ShowHeaderPreview => Thumbnails.Count != 1;
    public bool HasExportableThumbnails => Thumbnails.Any(t => !t.IsNewSlot);
    public string ThumbnailsHeader => LocalizationManager.Instance.GetString("look_inside.thumbnails_count", Thumbnails.Count);

    public List<LookInsideThumbnailItem> AddNewSlots()
    {
        var added = new List<LookInsideThumbnailItem>();
        if (Object == null) return added;
        foreach (var (key, size) in ThumbnailImageTools.GetNewThumbnailSlots(Object))
        {
            var slot = new LookInsideThumbnailItem(Object, key, size);
            Watch(slot);
            Thumbnails.Add(slot);
            added.Add(slot);
        }
        NotifyThumbnailsChanged();
        return added;
    }

    public void DiscardChanges()
    {
        foreach (var slot in Thumbnails.Where(t => t.IsNewSlot).ToList()) Thumbnails.Remove(slot);
        foreach (var t in Thumbnails.Where(t => t.IsChanged)) t.Undo();
        if (Object != null) IsEnabledInGame = !Object.IsDisabled;
        ResetCasFlags();
        NotifyThumbnailsChanged();
    }

    public void Undo(LookInsideThumbnailItem item)
    {
        if (item.IsNewSlot)
        {
            foreach (var slot in Thumbnails.Where(t => t.IsNewSlot).ToList()) Thumbnails.Remove(slot);
            NotifyThumbnailsChanged();
        }
        else
        {
            item.Undo();
        }
    }

    public event Action? Changed;

    private void Watch(LookInsideThumbnailItem item) => item.Changed += NotifyThumbnailsChanged;

    private void NotifyThumbnailsChanged()
    {
        OnPropertyChanged(nameof(HasThumbnails));
        OnPropertyChanged(nameof(HasNoThumbnails));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanAddThumbnails));
        OnPropertyChanged(nameof(SingleThumbnail));
        OnPropertyChanged(nameof(HasSingleThumbnail));
        OnPropertyChanged(nameof(HasThumbnailGrid));
        OnPropertyChanged(nameof(ShowHeaderPreview));
        OnPropertyChanged(nameof(HasExportableThumbnails));
        OnPropertyChanged(nameof(ThumbnailsHeader));
        Changed?.Invoke();
    }

    public string? CategoryText { get; }
    public string? InternalName { get; }
    public string? Description { get; }
    public string? PriceText { get; }
    public string? AgesText { get; }
    public string? GendersText { get; }
    public string? OutfitsText { get; }
    public string? PartLabel { get; }
    public string KeyText { get; }
    public bool CouldNotRead => Object?.CouldNotRead == true;
    public bool AnyMatchedByPackage => Thumbnails.Any(t => t.IsMatchedByPackage);

    public bool HasInternalName => !string.IsNullOrEmpty(InternalName);
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool HasPrice => PriceText != null;
    public bool HasAges => !string.IsNullOrEmpty(AgesText);
    public bool HasGenders => !string.IsNullOrEmpty(GendersText);
    public bool HasOutfits => !CanEditCasFlags && !string.IsNullOrEmpty(OutfitsText);
    public bool HasPartLabel => !string.IsNullOrEmpty(PartLabel);
    public bool HasKey => Object != null;

    public LookInsideObjectItem(InspectedObject obj, Func<InspectedResource, byte[]> reader, string? partLabel)
    {
        var loc = LocalizationManager.Instance;
        Object = obj;
        PartLabel = partLabel;
        _isEnabledInGame = !obj.IsDisabled;

        if (obj.Kind == InspectedObjectKind.Cas)
        {
            string category = loc.GetString($"filters.categories.{obj.CategoryKey}");
            CategoryText = obj.ClothingType != null && !string.Equals(obj.ClothingType, category, StringComparison.OrdinalIgnoreCase)
                ? $"{category} · {obj.ClothingType}"
                : category;
            Title = obj.Name ?? obj.ClothingType ?? loc.GetString("look_inside.unnamed_cas");
            AgesText = JoinLocalized("filters.ages", obj.Ages);
            GendersText = JoinLocalized("filters.genders", obj.Genders);
            OutfitsText = JoinLocalized("filters.outfits", obj.OutfitCategories);
            if (!obj.CouldNotRead)
            {
                OutfitOptions = CasFlagsService.OutfitBits
                    .Select(b => new CasFlagOption(loc.GetString($"filters.outfits.{b.Key}"), b.Bit, (obj.CasCategory & b.Bit) != 0))
                    .ToList();
                foreach (var option in OutfitOptions) option.Changed += NotifyCasFlagsChanged;
                _validForRandom = (obj.CasCategory & S3ForgeTools.GameFiles.Resources.CasCategoryFlags.ValidForRandom) != 0;
            }
        }
        else
        {
            CategoryText = loc.GetString($"look_inside.catalog.{obj.CategoryKey}");
            Title = obj.Name ?? obj.InternalName ?? loc.GetString("look_inside.unnamed_object");
            InternalName = obj.InternalName != obj.Name ? obj.InternalName : null;
            Description = obj.Description != obj.Name ? obj.Description : null;
            PriceText = obj.Price is float price ? $"§{price:0.##}" : null;
        }

        if (obj.CouldNotRead)
        {
            Title = obj.Kind == InspectedObjectKind.Cas ? loc.GetString("look_inside.unnamed_cas") : loc.GetString("look_inside.unnamed_object");
        }

        var k = obj.Resource.Key;
        KeyText = $"{k.Type:X8}-{k.Group:X8}-{k.Instance:X16}";
        Subtitle = partLabel != null ? $"{CategoryText} · {partLabel}" : CategoryText ?? string.Empty;

        Thumbnails = new(LookInsideThumbnailItem.CreateAll(obj, reader));
        foreach (var t in Thumbnails) Watch(t);
        _searchBlob = string.Join(" ", Title, CategoryText, InternalName, Description, KeyText, partLabel).ToLowerInvariant();
    }

    private LookInsideObjectItem(IReadOnlyList<InspectedThumbnail> unlinked, Func<InspectedResource, byte[]> reader, string? origin)
    {
        var loc = LocalizationManager.Instance;
        Title = loc.GetString("look_inside.other_thumbnails");
        Subtitle = loc.GetString("look_inside.other_thumbnails_count", unlinked.Count);
        if (origin != null) Subtitle = $"{Subtitle} · {origin}";
        Description = loc.GetString("look_inside.other_thumbnails_desc");
        KeyText = string.Empty;
        Thumbnails = new(unlinked.Select(t => new LookInsideThumbnailItem(t, reader, variant: 0)));
        foreach (var t in Thumbnails) Watch(t);
        _searchBlob = string.Empty;
    }

    public static LookInsideObjectItem ForUnlinked(IReadOnlyList<InspectedThumbnail> unlinked, Func<InspectedResource, byte[]> reader, LookInsideSource source, string? origin) =>
        new(unlinked, reader, origin) { Source = source };

    private LookInsideObjectItem(IReadOnlyList<LookInsideObjectItem> objects)
    {
        var loc = LocalizationManager.Instance;
        IsAllItems = true;
        Title = loc.GetString("look_inside.all_items");
        Subtitle = loc.GetString("look_inside.all_items_count", objects.Count);
        KeyText = "*all*";
        Thumbnails = new();
        MosaicImages = LookInsideAllItems.PickMosaic(objects);
        _searchBlob = string.Empty;
    }

    public static LookInsideObjectItem ForAll(IReadOnlyList<LookInsideObjectItem> objects) => new(objects);

    public bool Matches(string query) => _searchBlob.Contains(query.ToLowerInvariant(), StringComparison.Ordinal);

    private static LookInsideImage? PickPreview(IReadOnlyList<LookInsideThumbnailItem> thumbnails)
    {
        var shown = thumbnails.Where(t => !t.IsRemoved && (!t.IsNewSlot || t.NewData != null)).ToList();
        if (shown.Count == 0) return null;
        uint firstGroup = shown[0].Thumbnail.Group;
        return shown.Where(t => t.Thumbnail.Group == firstGroup).OrderByDescending(t => t.Thumbnail.SizeIndex).First().DisplayImage;
    }

    private static string? JoinLocalized(string prefix, IReadOnlyList<string> values)
    {
        if (values.Count == 0) return null;
        return string.Join(", ", values.Select(v => LocalizationManager.Instance.GetString($"{prefix}.{v}")));
    }
}
