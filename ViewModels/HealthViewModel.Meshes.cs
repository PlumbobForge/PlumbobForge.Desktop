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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Utils;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public partial class MeshReportRowViewModel : ObservableObject
{
    private static string L(string key) => LocalizationManager.Instance.GetString(key);

    public MeshReportRow Model { get; }

    public string Title { get; }
    public string Subtitle { get; }
    public string FileName => Model.FileName;
    public string SetText { get; }
    public bool IsCas => Model.IsCas;
    public bool IsBuildBuy => !Model.IsCas;
    public int Triangles => Model.Triangles;
    public string TrianglesText => Model.Triangles.ToString("N0");
    public string VerticesText => Model.Vertices.ToString("N0");
    public string LevelsText => Model.LodCount.ToString();
    public string DetailText { get; }
    public string WeightText => MeshFormat.WeightName(Model.Level);
    public bool IsLight => Model.Level == MeshWeightLevel.Light;
    public bool IsMedium => Model.Level == MeshWeightLevel.Medium;
    public bool IsHeavy => Model.Level == MeshWeightLevel.Heavy;
    public bool IsVeryHeavy => Model.Level == MeshWeightLevel.VeryHeavy;
    public bool IsHeavyOrWorse => Model.Level >= MeshWeightLevel.Heavy;

    [ObservableProperty]
    private bool _isOff;

    private Bitmap? _thumbnail;
    private bool _thumbnailRequested;

    public Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested && Model.Thumbnail != null)
            {
                _thumbnailRequested = true;
                _ = LoadThumbnailAsync(Model.Thumbnail);
            }
            return _thumbnail;
        }
    }

    public bool ShowKindIcon => _thumbnail == null;

    private async Task LoadThumbnailAsync(byte[] data)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var ms = new MemoryStream(data, writable: false);
                return Bitmap.DecodeToWidth(ms, 72);
            });
            Dispatcher.UIThread.Post(() =>
            {
                _thumbnail = bitmap;
                OnPropertyChanged(nameof(Thumbnail));
                OnPropertyChanged(nameof(ShowKindIcon));
            });
        }
        catch
        {
            // Not an image the app can show: the kind icon stays.
        }
    }

    public MeshReportRowViewModel(MeshReportRow model)
    {
        Model = model;
        _isOff = model.IsOff;

        string category = model.IsCas
            ? L($"filters.categories.{model.CategoryKey}")
            : L($"look_inside.catalog.{model.CategoryKey}");
        if (model.IsCas && model.ClothingType != null && !string.Equals(model.ClothingType, category, StringComparison.OrdinalIgnoreCase))
        {
            category = $"{category} · {model.ClothingType}";
        }

        Title = model.Name ?? model.ClothingType ?? L(model.IsCas ? "look_inside.unnamed_cas" : "look_inside.unnamed_object");
        Subtitle = category;
        SetText = string.IsNullOrEmpty(model.SetName) ? string.Empty : model.SetName;
        DetailText = LocalizationManager.Instance.GetString("tools.meshes.row_detail", model.Vertices.ToString("N0"), model.LodCount);
    }

    public static Func<MeshReportRowViewModel, Task>? OnLookInsideRequested { get; set; }
    public static Action<MeshReportRowViewModel>? OnShowInLibraryRequested { get; set; }
    public static Func<MeshReportRowViewModel, Task>? OnOptimizeRequested { get; set; }

    [RelayCommand]
    private Task LookInside() => OnLookInsideRequested?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand]
    private void ShowInLibrary() => OnShowInLibraryRequested?.Invoke(this);

    [RelayCommand]
    private Task Optimize() => OnOptimizeRequested?.Invoke(this) ?? Task.CompletedTask;
}

public partial class HealthViewModel
{
    #region Mesh Report

    public const string MeshFilterHeavy = "Heavy";
    public const string MeshFilterAll = "All";
    public const string MeshKindAll = "All";
    public const string MeshKindCas = "Cas";
    public const string MeshKindBuildBuy = "BuildBuy";

    private readonly List<MeshReportRowViewModel> _allMeshRows = new();
    private CancellationTokenSource? _meshScanCts;
    private CancellationTokenSource? _meshSearchCts;

    public PlumbobForge.Desktop.Utils.RangeObservableCollection<MeshReportRowViewModel> FilteredMeshRows { get; } = new();

    [ObservableProperty]
    private bool _isScanningMeshes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMeshList))]
    private bool _hasScannedMeshes;

    [ObservableProperty]
    private int _meshScanDone;

    [ObservableProperty]
    private int _meshScanTotal = 1;

    [ObservableProperty]
    private int _totalMeshesCounted;

    [ObservableProperty]
    private int _heavyMeshCount;

    [ObservableProperty]
    private string _meshNotes = string.Empty;

    [ObservableProperty]
    private string _meshWeightFilter = MeshFilterHeavy;

    [ObservableProperty]
    private string _meshKindFilter = MeshKindAll;

    [ObservableProperty]
    private string _meshSearchQuery = string.Empty;

    public bool IsMeshFilterHeavy => MeshWeightFilter == MeshFilterHeavy;
    public bool IsMeshFilterAll => MeshWeightFilter == MeshFilterAll;
    public bool IsMeshKindAll => MeshKindFilter == MeshKindAll;
    public bool IsMeshKindCas => MeshKindFilter == MeshKindCas;
    public bool IsMeshKindBuildBuy => MeshKindFilter == MeshKindBuildBuy;
    public bool HasMeshSearchQuery => !string.IsNullOrEmpty(MeshSearchQuery);
    public bool HasMeshNotes => !string.IsNullOrEmpty(MeshNotes);
    public bool HasMeshKinds => ScopedMeshRows.Any(r => r.IsCas) && ScopedMeshRows.Any(r => r.IsBuildBuy);
    public string MeshScanProgressText => LocalizationManager.Instance.GetString("tools.meshes.progress", MeshScanDone.ToString("N0"), MeshScanTotal.ToString("N0"));

    public bool ShowMeshEmpty => HasScannedMeshes && FilteredMeshRows.Count == 0;
    public bool ShowMeshList => HasScannedMeshes && FilteredMeshRows.Count > 0;
    public string MeshEmptyTitle => LocalizationManager.Instance.GetString(IsMeshEmptyBecauseNoneHeavy ? "tools.meshes.healthy_title" : "tools.meshes.empty_title");
    public string MeshEmptyText => LocalizationManager.Instance.GetString(IsMeshEmptyBecauseNoneHeavy ? "tools.meshes.healthy_desc" : "tools.meshes.empty_desc");
    private bool IsMeshEmptyBecauseNoneHeavy => TotalMeshesCounted > 0 && HeavyMeshCount == 0 && IsMeshFilterHeavy && !HasMeshSearchQuery;

    partial void OnMeshScanDoneChanged(int value) => OnPropertyChanged(nameof(MeshScanProgressText));
    partial void OnMeshScanTotalChanged(int value) => OnPropertyChanged(nameof(MeshScanProgressText));
    partial void OnMeshNotesChanged(string value) => OnPropertyChanged(nameof(HasMeshNotes));

    partial void OnMeshWeightFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsMeshFilterHeavy));
        OnPropertyChanged(nameof(IsMeshFilterAll));
        ApplyMeshFilter();
    }

    partial void OnMeshKindFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsMeshKindAll));
        OnPropertyChanged(nameof(IsMeshKindCas));
        OnPropertyChanged(nameof(IsMeshKindBuildBuy));
        RaiseOptimizerCounts();
        ApplyMeshFilter();
    }

    partial void OnMeshSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasMeshSearchQuery));
        _meshSearchCts?.Cancel();
        _meshSearchCts = new CancellationTokenSource();
        var token = _meshSearchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                if (!token.IsCancellationRequested)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested) ApplyMeshFilter();
                    });
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    [RelayCommand]
    public void SetMeshWeightFilter(string filter) => MeshWeightFilter = filter;

    [RelayCommand]
    public void SetMeshKindFilter(string filter) => MeshKindFilter = filter;

    [RelayCommand]
    public void ClearMeshSearch() => MeshSearchQuery = string.Empty;

    [RelayCommand]
    public Task ScanMeshesAsync() => ScanMeshesCoreAsync(null);

    private async Task ScanMeshesCoreAsync(IReadOnlyCollection<long>? onlyItems)
    {
        if (IsScanningMeshes) return;
        IsScanningMeshes = true;
        MeshScanDone = 0;
        MeshScanTotal = 1;
        _meshScanCts?.Cancel();
        _meshScanCts = new CancellationTokenSource();
        var ct = _meshScanCts.Token;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<MeshReportService>();
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                MeshScanTotal = Math.Max(1, p.Total);
                MeshScanDone = p.Done;
            });

            var summary = await service.ScanAsync(progress, ct, onlyItems);
            _meshRowsArePartial = onlyItems != null;

            _allMeshRows.Clear();
            _allMeshRows.AddRange(summary.Rows.Select(r => new MeshReportRowViewModel(r)));
            RecountMeshes();

            var notes = new List<string>();
            if (summary.ExternalMeshObjects > 0)
                notes.Add(LocalizationManager.Instance.GetString("tools.meshes.external_note", summary.ExternalMeshObjects.ToString("N0")));
            if (summary.PackagesUnreadable > 0)
                notes.Add(LocalizationManager.Instance.GetString("tools.meshes.unreadable_note", summary.PackagesUnreadable.ToString("N0")));
            MeshNotes = string.Join("  ", notes);

            HasScannedMeshes = true;
            OnPropertyChanged(nameof(HasMeshKinds));
            if (HeavyMeshCount == 0 && IsMeshFilterHeavy && TotalMeshesCounted > 0)
            {
                // Nothing to look at under "Heavy": the message says so, and "All" is one click away.
            }
            ApplyMeshFilter();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Mesh report failed", ex, "HealthViewModel");
            ShowCacheToast(LocalizationManager.Instance.GetString("tools.meshes.scan_failed", ex.Message), isError: true);
        }
        finally
        {
            IsScanningMeshes = false;
        }
    }

    private void ApplyMeshFilter()
    {
        string query = MeshSearchQuery.Trim();
        var filtered = ScopedMeshRows.Where(r =>
        {
            if (MeshWeightFilter == MeshFilterHeavy && !r.IsHeavyOrWorse) return false;
            if (MeshKindFilter == MeshKindCas && !r.IsCas) return false;
            if (MeshKindFilter == MeshKindBuildBuy && !r.IsBuildBuy) return false;
            if (query.Length == 0) return true;
            return r.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.SetText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        var previous = SelectedMeshRow;
        int oldIndex = previous == null ? -1 : FilteredMeshRows.IndexOf(previous);
        // Refiltering must not close and reopen the preview of a row that stays.
        _reselectingMesh = true;
        try
        {
            FilteredMeshRows.ReplaceAll(filtered);
            SelectMeshAfterFilter(previous, oldIndex);
        }
        finally
        {
            _reselectingMesh = false;
        }
        RefreshMeshPreview();
        OnPropertyChanged(nameof(ShowMeshEmpty));
        OnPropertyChanged(nameof(ShowMeshList));
        OnPropertyChanged(nameof(MeshEmptyTitle));
        OnPropertyChanged(nameof(MeshEmptyText));
    }

    #region Scope: the report for some items only (Open In > Mesh Report in the library)
    private HashSet<long>? _meshScope;
    // The rows came from a scan of the scope alone, so without the scope there is no full report to show.
    private bool _meshRowsArePartial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMeshScope))]
    private string? _meshScopeLabel;

    public bool HasMeshScope => _meshScope != null;

    private IEnumerable<MeshReportRowViewModel> ScopedMeshRows =>
        _meshScope == null ? _allMeshRows : _allMeshRows.Where(r => _meshScope.Contains(r.Model.MetaEntityId));

    private void RecountMeshes()
    {
        var rows = ScopedMeshRows.ToList();
        TotalMeshesCounted = rows.Count;
        HeavyMeshCount = rows.Count(r => r.IsHeavyOrWorse);
        OnPropertyChanged(nameof(HasMeshKinds));
        RaiseOptimizerCounts();
    }

    // Shows the report for these items: filtered from a full scan if there is one, otherwise scanned on their own.
    // It starts on All, since the point is to see these items, light ones included.
    public async Task ShowMeshesForAsync(IReadOnlyCollection<long> itemIds, string label)
    {
        SelectSection("Meshes");
        _meshScope = itemIds.ToHashSet();
        MeshScopeLabel = label;
        MeshSearchQuery = string.Empty;
        MeshWeightFilter = MeshFilterAll;
        if (HasScannedMeshes && !_meshRowsArePartial)
        {
            RecountMeshes();
            ApplyMeshFilter();
            return;
        }
        if (IsScanningMeshes) _meshScanCts?.Cancel();
        await WaitForMeshScanToStopAsync();
        await ScanMeshesCoreAsync(itemIds);
    }

    [RelayCommand]
    public void ClearMeshScope()
    {
        _meshScope = null;
        MeshScopeLabel = null;
        MeshWeightFilter = MeshFilterHeavy;
        if (_meshRowsArePartial)
        {
            _allMeshRows.Clear();
            _meshRowsArePartial = false;
            HasScannedMeshes = false;
            MeshNotes = string.Empty;
        }
        RecountMeshes();
        ApplyMeshFilter();
    }

    private async Task WaitForMeshScanToStopAsync()
    {
        for (int i = 0; i < 100 && IsScanningMeshes; i++) await Task.Delay(50);
    }
    #endregion

    private void InitMeshReport()
    {
        MeshReportRowViewModel.OnShowInLibraryRequested = row =>
            NavigateToPackageItem(row.Model.MetaEntityId, row.Model.SetId, row.Model.IsModsItem ? row.Model.MetaEntityId : null);
        MeshReportRowViewModel.OnLookInsideRequested = async row =>
        {
            // Look Inside may switch objects off and rewrite the file.
            ReleaseMeshPreview();
            if (await DialogHelper.ShowLookInsideAsync(row.Model.MetaEntityId, row.FileName)) await RescanMeshItemAsync(row.Model.MetaEntityId);
            await RefreshMeshOffStateAsync(row.Model.MetaEntityId);
            RefreshMeshPreview();
        };
        MeshReportRowViewModel.OnOptimizeRequested = row =>
        {
            ReleaseMeshPreview();
            OpenInOptimizer(new[] { row.Model });
            return Task.CompletedTask;
        };
    }

    private async Task RescanMeshItemAsync(long metaEntityId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var rows = await scope.ServiceProvider.GetRequiredService<MeshReportService>().ScanItemAsync(metaEntityId);
            if (rows == null) return;
            _allMeshRows.RemoveAll(r => r.Model.MetaEntityId == metaEntityId);
            _allMeshRows.AddRange(rows.Select(r => new MeshReportRowViewModel(r)));
            _allMeshRows.Sort((a, b) => b.Triangles.CompareTo(a.Triangles));
            RecountMeshes();
            ApplyMeshFilter();
        }
        catch
        {
        }
    }

    // Look Inside can switch objects off; the report shows that without scanning everything again.
    private async Task RefreshMeshOffStateAsync(long metaEntityId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var off = (await db.DisabledObjects.AsNoTracking()
                    .Where(d => d.MetaEntityId == metaEntityId)
                    .Select(d => new { d.Type, d.Group, d.Instance })
                    .ToListAsync())
                .Select(d => (d.Type, d.Group, d.Instance))
                .ToHashSet();
            // Rows know their object only by name and size, so match on the resource key kept in the model.
            foreach (var row in _allMeshRows.Where(r => r.Model.MetaEntityId == metaEntityId))
            {
                var key = row.Model.ResourceKey;
                row.IsOff = off.Contains((key.Type, key.Group, key.Instance));
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not refresh switched-off objects", ex, "HealthViewModel");
        }
    }

    #endregion
}
