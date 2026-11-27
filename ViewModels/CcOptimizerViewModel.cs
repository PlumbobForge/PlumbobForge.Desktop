using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Controls.MeshPreview;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Utils;
using S3ForgeTools.GameFiles.Package;

namespace PlumbobForge.Desktop.ViewModels;

public sealed partial class CcLodOption : ObservableObject
{
    public int Lod { get; }
    public string Name { get; }
    public int Before { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged), nameof(CountText))]
    private int _after;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isLinked;

    public bool IsChanged => After < Before;
    public string BeforeText => Before.ToString("N0");
    public string CountText => IsChanged ? After.ToString("N0") : Before.ToString("N0");

    public CcLodOption(int lod, int before)
    {
        Lod = lod;
        Name = lod switch
        {
            CcMesh.Unlisted => LocalizationManager.Instance.GetString("optimizer.lod_unlisted"),
            0 => LocalizationManager.Instance.GetString("optimizer.lod_close"),
            1 => LocalizationManager.Instance.GetString("optimizer.lod_mid"),
            2 => LocalizationManager.Instance.GetString("optimizer.lod_far"),
            3 => LocalizationManager.Instance.GetString("optimizer.lod_farthest"),
            _ => LocalizationManager.Instance.GetString("optimizer.lod_name", lod)
        };
        Before = before;
        _after = before;
    }
}

public sealed partial class CcTextureOption : ObservableObject
{
    public string Mode { get; }
    public string Label { get; }
    public bool IsClay => Mode == CcOptimizerViewModel.ClayMode;
    // One of the package's raw images (Multiplier, Mask...), not a finished look.
    public bool IsLayer => !IsClay && Mode != CcMaterial.InGame;

    [ObservableProperty]
    private bool _isSelected;

    public CcTextureOption(string mode, string label)
    {
        Mode = mode;
        Label = label;
    }
}

public sealed partial class CcPresetOption : ObservableObject
{
    public int Index { get; }
    public string Label => (Index + 1).ToString();
    public IReadOnlyList<IBrush> Swatches { get; }
    public bool HasSwatches => Swatches.Count > 0;
    public string Tip => LocalizationManager.Instance.GetString("optimizer.preset_tip", Index + 1);

    [ObservableProperty]
    private bool _isSelected;

    public CcPresetOption(int index, CasPreset? preset = null)
    {
        Index = index;
        Swatches = preset == null ? Array.Empty<IBrush>() : CcPreview.Swatches(preset);
    }
}

// One CAS part, or a package's objects, with the settings it will be saved with.
public sealed partial class CcQueueItem : ObservableObject
{
    public const double DefaultRatio = 0.5;
    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    public MeshReportRowViewModel Row { get; }
    public long ItemId => Row.Model.MetaEntityId;
    public bool IsObject => !Row.Model.IsCas;
    // Objects share meshes between colour variants, so a package's objects are one entry.
    public TGI_Key CasPart => IsObject ? CcOptimizerService.AllObjects : Row.Model.ResourceKey;
    public int ObjectCount { get; init; } = 1;
    public bool IsVault => Row.Model.IsVault;
    public string Title => ObjectCount > 1 ? L("optimizer.title_more", Row.Title, ObjectCount - 1) : Row.Title;
    public string FileName => Row.FileName;

    public double TopRatio { get; private set; } = DefaultRatio;
    public bool IsHair => string.Equals(Row.Model.ClothingType, "Hair", StringComparison.OrdinalIgnoreCase);
    public bool LockEdges { get; private set; }
    public bool? LinkSecondLod { get; private set; }
    public Dictionary<int, double> LowerRatios { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(IsReduced))]
    private int _before;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(IsReduced))]
    private int? _after;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsChecked))]
    private bool _isReviewed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(WillSave), nameof(IsChecked))]
    private bool _isSaved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasProblem), nameof(WillSave))]
    private string? _problem;

    public bool HasProblem => Problem != null;
    public bool IsChecked => IsReviewed && !IsSaved;
    public bool IsReduced => After is int a && a < Before;
    public bool WillSave => !IsSaved && !HasProblem && DiffersFromFile;

    // What the package holds now, as ratios of the original.
    private double _fileTop = 1;
    private Dictionary<int, double> _fileLower = new();
    private bool _fileLock;
    private bool? _fileLink;
    public bool MayBeOptimized { get; private set; }
    private bool _measured;
    private bool _touched;

    private bool IsReducing => TopRatio < 0.995 || LowerRatios.Values.Any(r => r < 0.995);
    private static bool Same(double a, double b) => Math.Round(a * 100) == Math.Round(b * 100);
    private bool DiffersFromFile =>
        !Same(TopRatio, _fileTop)
        || LowerRatios.Keys.Union(_fileLower.Keys).Any(l => !Same(LowerRatios.GetValueOrDefault(l, 1), _fileLower.GetValueOrDefault(l, 1)))
        || (IsReducing && (LockEdges != _fileLock || LinkSecondLod != _fileLink));
    public string PercentText => $"{Math.Round(TopRatio * 100)}%";
    public string CountText => After is int a && a < Before ? $"{Before:N0} → {a:N0}" : Before.ToString("N0");
    public string StatusText => Problem
        ?? (IsSaved ? L("optimizer.status_saved") : IsReviewed ? L("optimizer.status_checked") : L("optimizer.status_unchecked", PercentText));

    public CcQueueItem(MeshReportRowViewModel row)
    {
        Row = row;
        _before = row.Triangles;
        LockEdges = !IsHair;
        _fileLock = LockEdges;
        // Optimized before: start at 100% until opened.
        if (MeshBackupStore.HasEntries(ItemId))
        {
            MayBeOptimized = true;
            TopRatio = 1;
        }
    }

    public void SetTopRatio(double ratio) { TopRatio = ratio; _touched = true; Changed(); }
    public void SetLowerRatio(int lod, double ratio) { LowerRatios[lod] = ratio; _touched = true; Changed(); }
    public void SetLockEdges(bool value) { LockEdges = value; Changed(); }
    public void SetLinkSecondLod(bool value) { LinkSecondLod = value; Changed(); }

    public void CopySettingsFrom(CcQueueItem other)
    {
        TopRatio = other.TopRatio;
        LockEdges = other.LockEdges;
        LinkSecondLod = other.LinkSecondLod;
        _touched = true;
        After = null;
        Changed();
    }

    // Sets the sliders to where the last optimization left them (first open only).
    public void UseStoredRatios(CcAnalysis analysis)
    {
        if (_measured) return;
        _measured = true;
        var lods = analysis.EditableLods;
        _fileTop = lods.Count > 0 ? analysis.StoredRatioFor(lods[0]) : 1;
        _fileLower = lods.Skip(1).ToDictionary(l => l, analysis.StoredRatioFor);
        _fileLock = LockEdges;
        _fileLink = false;
        if (!_touched)
        {
            TopRatio = _fileTop;
            LowerRatios.Clear();
            foreach (var (lod, ratio) in _fileLower) LowerRatios[lod] = ratio;
            LinkSecondLod = false;
        }
        Changed();
    }

    public void MarkSaved()
    {
        _fileTop = TopRatio;
        _fileLower = new Dictionary<int, double>(LowerRatios);
        _fileLock = LockEdges;
        _fileLink = LinkSecondLod;
        _touched = false;
        IsSaved = true;
        Changed();
    }

    private void Changed()
    {
        if (IsSaved && DiffersFromFile) IsSaved = false;
        OnPropertyChanged(nameof(WillSave));
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(StatusText));
    }

    public void ApplyTo(CcAnalysis analysis, CcOptimizePlan plan)
    {
        plan.LockEdges = LockEdges;
        if (LinkSecondLod is bool link) plan.LinkSecondLod = link;
        var lods = analysis.EditableLods;
        if (lods.Count > 0) plan.Ratios[lods[0]] = TopRatio;
        foreach (var (lod, ratio) in LowerRatios)
        {
            if (lods.Count == 0 || lod != lods[0]) plan.Ratios[lod] = ratio;
        }
    }

    public CcOptimizePlan BuildPlan(CcAnalysis analysis)
    {
        var plan = CcOptimizePlan.SuggestedFor(analysis);
        ApplyTo(analysis, plan);
        return plan;
    }
}

public sealed partial class CcOptimizerViewModel : ObservableObject, IDisposable
{
    public const string ClayMode = "clay";
    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    private readonly CancellationTokenSource _cts = new();
    // One reduction at a time: CcMesh caches its last result.
    private readonly SemaphoreSlim _work = new(1, 1);
    private int _version;
    private int _loadVersion;

    private PackageInspection? _inspection;
    private CcAnalysis? _analysis;
    private CcQueueItem? _loaded;
    private CcOptimizePlan _plan = new();
    private List<CasPreset> _presets = new();
    private readonly Dictionary<InspectedResource, RgbaImage> _decoded = new();
    private readonly Dictionary<(int Preset, string Mode), (RgbaImage? Image, bool Cutout)> _textures = new();
    private Dictionary<CcMesh, (MeshPreviewTexture Texture, bool Cutout)>? _objectTextures;

    public event Action<IReadOnlyCollection<long>>? ItemsSaved;

    public ObservableCollection<CcQueueItem> Items { get; } = new();
    public ObservableCollection<CcLodOption> Lods { get; } = new();
    public ObservableCollection<CcTextureOption> TextureOptions { get; } = new();
    // Plain and in-game looks, then the package's raw images behind "Texture layers".
    public ObservableCollection<CcTextureOption> MainTextureOptions { get; } = new();
    public ObservableCollection<CcTextureOption> TextureLayers { get; } = new();
    public ObservableCollection<CcPresetOption> Presets { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ShowEditor))]
    private CcQueueItem? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEditor))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(ShowEditor))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanEditRatio), nameof(CanEditList))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditList))]
    private bool _isAdding;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditRatio), nameof(IsSelectedLinked))]
    private CcLodOption? _selectedLod;

    [ObservableProperty]
    private double _ratioPercent = 100;

    [ObservableProperty]
    private bool _lockEdges = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditRatio), nameof(IsSelectedLinked))]
    private bool _linkSecondLod;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingOriginal))]
    private bool _showOptimized = true;

    [ObservableProperty]
    private bool _showWireframe;

    [ObservableProperty]
    private bool _useTransparency = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLayerSelected), nameof(LayersText))]
    private CcTextureOption? _selectedTexture;

    [ObservableProperty]
    private CcPresetOption? _selectedPreset;

    [ObservableProperty]
    private IReadOnlyList<MeshPreviewItem>? _previewMeshes;

    [ObservableProperty]
    private MeshPreviewTexture? _previewTexture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTransparencyToggle))]
    private bool _textureHasCutout;

    [ObservableProperty] private string _beforeText = "";
    [ObservableProperty] private string _afterText = "";
    [ObservableProperty] private string _savingText = "";
    [ObservableProperty] private string _weightText = "";
    [ObservableProperty] private MeshWeightLevel _weightLevel;
    [ObservableProperty] private bool _showLimitNote;
    [ObservableProperty] private bool _showDoubleSidedNote;

    public bool HasItems => Items.Count > 0;
    public bool IsListEmpty => Items.Count == 0 && !IsAdding;
    public bool HasSelection => SelectedItem != null;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool ShowEditor => SelectedItem != null && !IsLoading && !HasError;
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool CanEditList => !IsSaving && !IsAdding;
    public int SaveCount => Items.Count(i => i.WillSave);
    public bool CanSave => SaveCount > 0 && !IsSaving && !IsAdding;
    public string SaveText => L("optimizer.save_count", SaveCount);
    public string ListSummary => L("optimizer.list_summary", Items.Count, SaveCount);
    public bool CanApplyToAll => SelectedItem != null && Items.Count > 1 && !IsSaving;
    public bool IsSelectedLinked => SelectedLod != null && _analysis != null && LinkSecondLod && _plan.IsLinked(_analysis, SelectedLod.Lod);
    public bool CanEditRatio => SelectedLod != null && !IsSaving && !IsSelectedLinked;
    public bool HasSeveralLods => Lods.Count > 1;
    public bool ShowingOriginal => !ShowOptimized;
    public bool HasLinkOption => Lods.Count >= 2;
    public string LinkText => L(Lods.Count > 2 ? "optimizer.link_lods" : "optimizer.link_lod");
    public string LinkNote => L(_analysis?.SuggestLinkSecondLod == true ? "optimizer.link_note_suggested" : "optimizer.link_note");
    public string LockEdgesNote => L(_analysis?.SuggestLockEdges == false ? "optimizer.lock_edges_note_hair" : "optimizer.lock_edges_note");
    public bool ShowResetSettings => _analysis != null
        && (LockEdges != _analysis.SuggestLockEdges || (HasLinkOption && LinkSecondLod != _analysis.SuggestLinkSecondLod));
    public bool HasTextureLayers => TextureLayers.Count > 0;
    public bool IsLayerSelected => SelectedTexture is { IsLayer: true };
    public string LayersText => IsLayerSelected ? SelectedTexture!.Label : L("optimizer.texture_layers");
    public bool HasTextures => TextureOptions.Count > 1;
    public bool HasPresetChoice => Presets.Count > 1;
    public bool ShowTransparencyToggle => TextureHasCutout;
    public bool IsLight => WeightLevel == MeshWeightLevel.Light;
    public bool IsMedium => WeightLevel == MeshWeightLevel.Medium;
    public bool IsHeavy => WeightLevel == MeshWeightLevel.Heavy;
    public bool IsVeryHeavy => WeightLevel == MeshWeightLevel.VeryHeavy;
    public string WeightTip => MeshFormat.WeightTip(isCas: _analysis?.IsObject != true);
    public string LimitNoteText => L(LockEdges ? "optimizer.limit_note_locked" : "optimizer.limit_note");

    partial void OnWeightLevelChanged(MeshWeightLevel value)
    {
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsMedium));
        OnPropertyChanged(nameof(IsHeavy));
        OnPropertyChanged(nameof(IsVeryHeavy));
    }

    public CcOptimizerViewModel()
    {
        Items.CollectionChanged += (_, _) => RaiseListProperties();
        HistoryItems.CollectionChanged += (_, _) => RaiseHistoryList();
        CountHistory();
    }

    private void RaiseListProperties()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(SaveCount));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(ListSummary));
        OnPropertyChanged(nameof(CanApplyToAll));
    }

    partial void OnIsAddingChanged(bool value) => RaiseListProperties();
    partial void OnIsSavingChanged(bool value) => RaiseListProperties();

    public Func<Task>? AddItemsHandler { get; set; }

    [RelayCommand]
    private Task AddItems() => CanEditList ? AddItemsHandler?.Invoke() ?? Task.CompletedTask : Task.CompletedTask;

    // Objects of one package become one entry.
    public void AddRows(IEnumerable<MeshReportRow> rows)
    {
        CcQueueItem? first = null;
        var added = new List<CcQueueItem>();
        var given = rows.ToList();
        var entries = given.Where(r => r.IsCas).Select(r => (Row: r, Objects: 1))
            .Concat(given.Where(r => !r.IsCas)
                .GroupBy(r => r.MetaEntityId)
                .Select(g => (Row: g.OrderByDescending(r => r.Triangles).First(), Objects: g.Select(r => r.ResourceKey).Distinct().Count())));
        foreach (var (row, objects) in entries)
        {
            var existing = Items.FirstOrDefault(i => i.ItemId == row.MetaEntityId && (row.IsCas ? !i.IsObject && i.CasPart.Equals(row.ResourceKey) : i.IsObject));
            if (existing != null) { first ??= existing; continue; }
            var item = new CcQueueItem(new MeshReportRowViewModel(row)) { ObjectCount = objects };
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CcQueueItem.WillSave)) RaiseListProperties();
            };
            added.Add(item);
            first ??= item;
        }
        if (added.Count > 0)
        {
            var all = Items.Concat(added).OrderByDescending(i => i.Before).ToList();
            Items.Clear();
            foreach (var i in all) Items.Add(i);
            first = added.OrderByDescending(i => i.Before).First();
        }
        if (first != null && (added.Count > 0 || SelectedItem == null)) SelectItem(first);
    }

    public async Task AddItemsAsync(IReadOnlyCollection<long> itemIds, ISet<(long ItemId, TGI_Key Key)>? only = null)
    {
        if (itemIds.Count == 0 || IsAdding) return;
        IsAdding = true;
        var rows = new List<MeshReportRow>();
        int done = 0, skipped = 0;
        try
        {
            using var scope = App.Services?.CreateScope();
            var report = scope?.ServiceProvider.GetRequiredService<MeshReportService>()
                ?? throw new InvalidOperationException("Services are not available");
            foreach (long id in itemIds)
            {
                ProgressText = L("optimizer.adding", ++done, itemIds.Count);
                var found = await report.ScanItemAsync(id, _cts.Token);
                if (found == null) { skipped++; continue; }
                bool Wanted(MeshReportRow r) => only == null || only.Contains((r.MetaEntityId, r.ResourceKey));
                var cas = found.Where(r => r.IsCas && Wanted(r)).ToList();
                var objects = found.Where(r => !r.IsCas).ToList();
                if (!objects.Any(Wanted)) objects.Clear();
                if (cas.Count == 0 && objects.Count == 0) { skipped++; continue; }
                rows.AddRange(cas);
                rows.AddRange(objects);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("CC optimizer could not add items", ex, "CcOptimizerViewModel");
            StatusMessage = L("optimizer.add_failed", ex.Message);
        }
        finally
        {
            IsAdding = false;
        }
        AddRows(rows);
        if (skipped > 0) StatusMessage = L("optimizer.skipped_items", skipped);
    }

    [RelayCommand]
    private void SelectItem(CcQueueItem? item)
    {
        if (item == null || item == SelectedItem) return;
        foreach (var i in Items) i.IsSelected = i == item;
        SelectedItem = item;
        OnPropertyChanged(nameof(CanApplyToAll));
        _ = LoadAsync(item);
    }

    [RelayCommand]
    private void RemoveItem(CcQueueItem? item)
    {
        if (item == null || !CanEditList) return;
        int index = Items.IndexOf(item);
        Items.Remove(item);
        if (item == SelectedItem)
        {
            ClearEditor();
            SelectedItem = null;
            if (Items.Count > 0) SelectItem(Items[Math.Min(index, Items.Count - 1)]);
        }
    }

    [RelayCommand]
    private void ClearList()
    {
        if (!CanEditList) return;
        Items.Clear();
        ClearEditor();
        SelectedItem = null;
        StatusMessage = null;
    }

    [RelayCommand]
    private void RemoveSaved()
    {
        if (!CanEditList) return;
        foreach (var item in Items.Where(i => i.IsSaved).ToList()) RemoveItem(item);
    }

    [RelayCommand]
    private void ApplyToAll()
    {
        if (SelectedItem is not { } source || !CanApplyToAll) return;
        int count = 0;
        foreach (var item in Items.Where(i => i != source && !i.IsSaved && !i.HasProblem))
        {
            item.CopySettingsFrom(source);
            count++;
        }
        StatusMessage = L("optimizer.applied_to_all", source.PercentText, count);
        RaiseListProperties();
    }

    private async Task LoadAsync(CcQueueItem item)
    {
        int load = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        ErrorMessage = null;
        ClearEditor();
        try
        {
            using var scope = App.Services?.CreateScope();
            var inspector = scope?.ServiceProvider.GetRequiredService<PackageInspectorService>()
                ?? throw new InvalidOperationException("Services are not available");
            var inspection = await inspector.InspectAsync(item.ItemId, _cts.Token);
            var (analysis, presets) = await Task.Run(() =>
            {
                inspection.UseOriginals(MeshBackupStore.ValidOriginals(item.ItemId, inspection));
                var a = CcOptimizerService.Analyse(inspection, item.CasPart, _cts.Token);
                return (a, a.ReadPresets(inspection));
            }, _cts.Token);
            if (_cts.IsCancellationRequested || load != _loadVersion) { inspection.Dispose(); return; }

            _inspection = inspection;
            _analysis = analysis;
            _loaded = item;
            _presets = presets;
            OnPropertyChanged(nameof(WeightTip));

            if (analysis.Meshes.Count == 0 || analysis.Meshes.All(m => !m.CanReduce))
            {
                item.Problem = L(analysis.Meshes.Count == 0 ? "optimizer.no_meshes_short" : "optimizer.nothing_to_reduce_short");
                ErrorMessage = L(analysis.Meshes.Count == 0 ? "optimizer.no_meshes" : "optimizer.nothing_to_reduce");
                return;
            }

            if (analysis.IsFromOriginals) item.UseStoredRatios(analysis);
            _plan = item.BuildPlan(analysis);
#pragma warning disable MVVMTK0034
            _lockEdges = _plan.LockEdges;
            _linkSecondLod = _plan.LinkSecondLod;
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(LockEdges));
            OnPropertyChanged(nameof(LinkSecondLod));
            OnPropertyChanged(nameof(LimitNoteText));

            foreach (int lod in analysis.EditableLods) Lods.Add(new CcLodOption(lod, analysis.RealTrianglesIn(lod)));
            OnPropertyChanged(nameof(HasLinkOption));
            RaiseSettingNotes();
            OnPropertyChanged(nameof(LinkText));
            OnPropertyChanged(nameof(HasSeveralLods));
            if (Lods.Count > 0) item.Before = Lods[0].Before;

            for (int i = 0; i < presets.Count; i++) Presets.Add(new CcPresetOption(i, presets[i]));
            OnPropertyChanged(nameof(HasPresetChoice));
            SelectPresetCore(Presets.FirstOrDefault());
            BuildTextureOptions();

            SelectLodCore(Lods.FirstOrDefault());
            item.IsReviewed = true;
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (load != _loadVersion) return;
            AppLogger.LogError($"CC optimizer could not open item {item.ItemId}", ex, "CcOptimizerViewModel");
            item.Problem = L("optimizer.error_short");
            ErrorMessage = L("optimizer.error_read", ex.Message);
        }
        finally
        {
            if (load == _loadVersion) IsLoading = false;
        }
    }

    // holdingWork: the caller holds _work.
    private void ClearEditor(bool holdingWork = false)
    {
        Interlocked.Increment(ref _version);
        var old = _inspection;
        _inspection = null;
        if (old != null)
        {
            if (holdingWork) old.Dispose();
            else _ = DisposeWhenIdleAsync(old);
        }
        _analysis = null;
        _loaded = null;
        _presets = new();
        _decoded.Clear();
        _textures.Clear();
        _objectTextures = null;
        Lods.Clear();
        Presets.Clear();
        SelectedLod = null;
        PreviewMeshes = null;
        PreviewTexture = null;
        TextureHasCutout = false;
        ErrorMessage = null;
        OnPropertyChanged(nameof(HasSeveralLods));
        OnPropertyChanged(nameof(HasLinkOption));
        RaiseSettingNotes();
        OnPropertyChanged(nameof(HasPresetChoice));
    }

    private string? _chosenTexture;

    private void BuildTextureOptions()
    {
        var preset = SelectedPreset != null && SelectedPreset.Index < _presets.Count ? _presets[SelectedPreset.Index] : null;
        var options = CcPreview.TextureOptions(_inspection, _analysis, preset);
        TextureOptions.Clear();
        MainTextureOptions.Clear();
        TextureLayers.Clear();
        foreach (var o in options)
        {
            TextureOptions.Add(o);
            (o.IsLayer ? TextureLayers : MainTextureOptions).Add(o);
        }
        OnPropertyChanged(nameof(HasTextureLayers));
        OnPropertyChanged(nameof(HasTextures));
        SelectTextureCore(CcPreview.Pick(options, _chosenTexture));
    }

    [RelayCommand]
    private void SelectLod(CcLodOption option)
    {
        SelectLodCore(option);
        _ = RefreshAsync();
    }

    private void SelectLodCore(CcLodOption? option)
    {
        foreach (var l in Lods) l.IsSelected = l == option;
        SelectedLod = option;
        _settingRatio = true;
        RatioPercent = option == null || _analysis == null ? 100 : Math.Round(_plan.RatioFor(_analysis, option.Lod) * 100);
        _settingRatio = false;
    }

    private bool _settingRatio;

    partial void OnRatioPercentChanged(double value)
    {
        if (_settingRatio || SelectedLod == null || _analysis == null || _loaded == null || IsSelectedLinked) return;
        double ratio = Math.Clamp(Math.Round(value) / 100.0, 0.05, 1.0);
        _plan.Ratios[SelectedLod.Lod] = ratio;
        if (Lods.Count > 0 && SelectedLod == Lods[0]) _loaded.SetTopRatio(ratio);
        else _loaded.SetLowerRatio(SelectedLod.Lod, ratio);
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void SetRatio(string percent)
    {
        if (CanEditRatio && double.TryParse(percent, out var p)) RatioPercent = p;
    }

    partial void OnLockEdgesChanged(bool value)
    {
        _plan.LockEdges = value;
        _loaded?.SetLockEdges(value);
        OnPropertyChanged(nameof(LimitNoteText));
        RaiseSettingNotes();
        _ = RefreshAsync();
    }

    partial void OnLinkSecondLodChanged(bool value)
    {
        _plan.LinkSecondLod = value;
        _loaded?.SetLinkSecondLod(value);
        RaiseSettingNotes();
        foreach (var l in Lods) l.IsLinked = _analysis != null && _plan.IsLinked(_analysis, l.Lod);
        _ = RefreshAsync();
    }

    partial void OnShowOptimizedChanged(bool value) => _ = RefreshAsync(previewOnly: true);

    [RelayCommand]
    private void ShowBefore() => ShowOptimized = false;

    [RelayCommand]
    private void ShowAfter() => ShowOptimized = true;

    partial void OnUseTransparencyChanged(bool value)
    {
        _objectTextures = null;
        _ = UpdateTextureAsync();
    }

    [RelayCommand]
    private void SelectTexture(CcTextureOption option)
    {
        _chosenTexture = option.Mode;
        SelectTextureCore(option);
        _ = UpdateTextureAsync();
    }

    private void RaiseSettingNotes()
    {
        OnPropertyChanged(nameof(LinkNote));
        OnPropertyChanged(nameof(LockEdgesNote));
        OnPropertyChanged(nameof(ShowResetSettings));
    }

    [RelayCommand]
    private void ResetSettings()
    {
        if (_analysis == null) return;
        LockEdges = _analysis.SuggestLockEdges;
        if (HasLinkOption) LinkSecondLod = _analysis.SuggestLinkSecondLod;
    }

    [RelayCommand]
    private void ToggleWireframe() => ShowWireframe = !ShowWireframe;

    [RelayCommand]
    private void ToggleTransparency() => UseTransparency = !UseTransparency;

    private void SelectTextureCore(CcTextureOption? option)
    {
        foreach (var t in TextureOptions) t.IsSelected = t == option;
        SelectedTexture = option;
    }

    [RelayCommand]
    private void SelectPreset(CcPresetOption option)
    {
        SelectPresetCore(option);
        BuildTextureOptions();
        _ = UpdateTextureAsync();
    }

    private void SelectPresetCore(CcPresetOption? option)
    {
        foreach (var p in Presets) p.IsSelected = p == option;
        SelectedPreset = option;
    }

    private async Task RefreshAsync(bool previewOnly = false)
    {
        var analysis = _analysis;
        var selected = SelectedLod;
        var item = _loaded;
        if (analysis == null || selected == null || item == null) return;
        int version = Interlocked.Increment(ref _version);
        bool showOptimized = ShowOptimized;
        foreach (var l in Lods) l.IsLinked = _plan.IsLinked(analysis, l.Lod);
        OnPropertyChanged(nameof(IsSelectedLinked));
        OnPropertyChanged(nameof(CanEditRatio));

        IsWorking = true;
        try
        {
            await _work.WaitAsync(_cts.Token);
            try
            {
                if (version != _version) return;
                var plan = SnapshotPlan();
                var lodIds = Lods.Select(l => l.Lod).ToList();
                if (analysis.UsesObjectTextures) await PrepareObjectTexturesAsync(analysis);
                var objectTextures = analysis.UsesObjectTextures ? _objectTextures : null;
                var result = await Task.Run(() =>
                {
                    var after = previewOnly ? null : lodIds.ToDictionary(l => l, l => plan.TrianglesAfter(analysis, l) - (analysis.TrianglesIn(l) - analysis.RealTrianglesIn(l)));
                    double ratio = plan.RatioFor(analysis, selected.Lod);
                    // Drawn: what the preview shows (an object's flat parts too, not its shadows). Counted: what the
                    // optimizer reduces.
                    var shown = analysis.PreviewMeshesIn(selected.Lod).ToList();
                    var counted = analysis.MeshesIn(selected.Lod).Where(m => !m.IsPlaceholder).ToList();
                    var reduced = shown.Select(m => m.Reduce(ratio, plan.LockEdges)).ToList();
                    bool hasStrands = shown.Any(m => m.Geom.IsHairShader);
                    var items = shown.Select((m, i) =>
                        {
                            var own = CcPreview.OwnTexture(m, objectTextures);
                            bool allowCutout = CcPreview.AllowCutout(m, hasStrands, objectTextures);
                            return showOptimized
                                ? MeshPreviewItem.FromGeom(reduced[i].Geom, i, reduced[i].DoubleSided, allowCutout, own)
                                : MeshPreviewItem.FromGeom(m.Geom, i, m.IsDoubleSided, allowCutout, own);
                        })
                        .ToList();
                    int before = counted.Sum(m => m.Triangles), afterTris = counted.Sum(m => m.Reduce(ratio, plan.LockEdges).Triangles);
                    bool doubleSided = shown.Any(m => m.IsDoubleSided);
                    // Locked edges can stop the simplifier early, on any level.
                    bool limited = after?.Any(p =>
                    {
                        double r = plan.RatioFor(analysis, p.Key);
                        int own = analysis.RealTrianglesIn(p.Key);
                        return r < 1 && p.Value > Math.Floor(own * r) * 1.15;
                    }) ?? (ratio < 1 && afterTris > Math.Floor(before * ratio) * 1.15);
                    return (after, ratio, items, before, afterTris, doubleSided, limited);
                }, _cts.Token);
                if (version != _version) return;

                if (result.after != null)
                {
                    foreach (var l in Lods) l.After = result.after[l.Lod];
                    if (Lods.Count > 0) item.After = Lods[0].After;
                }
                if (IsSelectedLinked)
                {
                    _settingRatio = true;
                    RatioPercent = Math.Round(result.ratio * 100);
                    _settingRatio = false;
                }
                PreviewMeshes = result.items;
                BeforeText = result.before.ToString("N0");
                AfterText = result.afterTris.ToString("N0");
                int pct = result.before > 0 ? (int)Math.Round(100.0 * (result.before - result.afterTris) / result.before) : 0;
                SavingText = pct > 0 ? L("optimizer.saving", pct) : L("optimizer.unchanged");
                WeightLevel = MeshWeight.Of(!analysis.IsObject, result.afterTris);
                WeightText = MeshFormat.WeightName(WeightLevel);
                if (!previewOnly) ShowLimitNote = result.limited;
                ShowDoubleSidedNote = result.doubleSided;
                if (!analysis.UsesObjectTextures && PreviewTexture == null && SelectedTexture is { IsClay: false }) await UpdateTextureCoreAsync();
            }
            finally
            {
                _work.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogError("CC optimizer preview failed", ex, "CcOptimizerViewModel");
            StatusMessage = L("optimizer.preview_failed", ex.Message);
        }
        finally
        {
            if (version == _version) IsWorking = false;
        }
    }

    private async Task UpdateTextureAsync()
    {
        if (_analysis is { UsesObjectTextures: true })
        {
            _objectTextures = null;
            await RefreshAsync(previewOnly: true);
            return;
        }
        try
        {
            await _work.WaitAsync(_cts.Token);
            try { await UpdateTextureCoreAsync(); }
            finally { _work.Release(); }
        }
        catch (OperationCanceledException) { }
    }

    // Called with _work held.
    private async Task PrepareObjectTexturesAsync(CcAnalysis analysis)
    {
        var inspection = _inspection;
        bool ingame = SelectedTexture is { IsClay: false };
        if (inspection == null || !ingame)
        {
            _objectTextures = new();
            TextureHasCutout = false;
            return;
        }
        if (_objectTextures != null) return;
        bool transparency = UseTransparency;
        var decoded = _decoded;
        try
        {
            string mode = SelectedTexture!.Mode;
            var colours = SelectedPreset != null && SelectedPreset.Index < _presets.Count ? _presets[SelectedPreset.Index] : null;
            var built = await Task.Run(() => CcPreview.ObjectTextures(inspection, analysis, decoded, transparency, mode, colours), _cts.Token);
            if (analysis != _analysis) return;
            _objectTextures = built;
            TextureHasCutout = built.Values.Any(t => t.Item2);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("CC optimizer could not build the object textures", ex, "CcOptimizerViewModel");
            _objectTextures = new();
            TextureHasCutout = false;
            StatusMessage = L("optimizer.texture_failed", ex.Message);
        }
    }

    private async Task UpdateTextureCoreAsync()
    {
        var inspection = _inspection;
        var analysis = _analysis;
        var mode = SelectedTexture?.Mode;
        int preset = SelectedPreset?.Index ?? -1;
        if (inspection == null || analysis == null || mode == null || mode == ClayMode || preset < 0 || preset >= _presets.Count)
        {
            PreviewTexture = null;
            TextureHasCutout = false;
            return;
        }
        try
        {
            if (!_textures.TryGetValue((preset, mode), out var entry))
            {
                var presetData = _presets[preset];
                var decoded = _decoded;
                entry = await Task.Run(() => CcPreview.CasTexture(inspection, analysis, presetData, mode, decoded), _cts.Token);
                if (analysis != _analysis) return;
                _textures[(preset, mode)] = entry;
            }
            TextureHasCutout = entry.Cutout;
            PreviewTexture = entry.Image == null ? null
                : new MeshPreviewTexture(entry.Image.Width, entry.Image.Height, entry.Image.Rgba, entry.Cutout && UseTransparency);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"CC optimizer could not build the {mode} texture", ex, "CcOptimizerViewModel");
            PreviewTexture = null;
            StatusMessage = L("optimizer.texture_failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave) return;
        var targets = Items.Where(i => i.WillSave).ToList();
        if (Sims3GameProcess.IsGameRunning())
        {
            StatusMessage = L("optimizer.game_running");
            return;
        }

        IsSaving = true;
        StatusMessage = null;
        var selected = SelectedItem;
        // Files must not be open while they are replaced.
        try { await _work.WaitAsync(_cts.Token); } catch (OperationCanceledException) { IsSaving = false; return; }
        try { ClearEditor(holdingWork: true); } finally { _work.Release(); }

        var savedIds = new HashSet<long>();
        int saved = 0, failed = 0;
        bool needsRebuild = false;
        string? stop = null;
        try
        {
            using var scope = App.Services?.CreateScope();
            var service = scope?.ServiceProvider.GetRequiredService<CcOptimizerService>()
                ?? throw new InvalidOperationException("Services are not available");
            for (int n = 0; n < targets.Count; n++)
            {
                var item = targets[n];
                ProgressText = L("optimizer.saving_progress", n + 1, targets.Count, item.Title);
                try
                {
                    await service.OptimizeAsync(item.ItemId, item.CasPart, item.ApplyTo, _cts.Token);
                    item.MarkSaved();
                    item.Problem = null;
                    savedIds.Add(item.ItemId);
                    saved++;
                    if (!item.IsVault) needsRebuild = true;
                }
                catch (GameRunningException)
                {
                    stop = L("optimizer.game_running_partial", saved);
                    break;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"CC optimizer could not save item {item.ItemId}", ex, "CcOptimizerViewModel");
                    item.Problem = L("optimizer.save_failed_short");
                    failed++;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("CC optimizer save failed", ex, "CcOptimizerViewModel");
            stop = L("optimizer.save_failed", ex.Message);
        }
        finally
        {
            IsSaving = false;
            ProgressText = string.Empty;
            RaiseListProperties();
        }

        string summary = stop ?? (failed > 0 ? L("optimizer.saved_some", saved, failed) : L("optimizer.saved_items", saved));
        StatusMessage = saved > 0 && needsRebuild && stop == null ? $"{summary} {L("optimizer.saved_rebuild")}" : summary;
        if (savedIds.Count > 0)
        {
            ItemsSaved?.Invoke(savedIds);
            OnHistoryChanged();
        }

        if (selected != null && Items.Contains(selected))
        {
            SelectedItem = selected;
            await LoadAsync(selected);
        }
    }

    private CcOptimizePlan SnapshotPlan()
    {
        var copy = new CcOptimizePlan { LinkSecondLod = _plan.LinkSecondLod, LockEdges = _plan.LockEdges };
        foreach (var (lod, ratio) in _plan.Ratios) copy.Ratios[lod] = ratio;
        return copy;
    }

    // Closed once running work is done.
    private async Task DisposeWhenIdleAsync(PackageInspection inspection)
    {
        try
        {
            await _work.WaitAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            inspection.Dispose();
            return;
        }
        try { inspection.Dispose(); }
        finally { _work.Release(); }
    }

    private void ReleaseInspection()
    {
        _inspection?.Dispose();
        _inspection = null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        ReleaseInspection();
    }
}
