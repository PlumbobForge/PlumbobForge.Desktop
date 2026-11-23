using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.ViewModels;

// Tools use the window's width: past the breakpoint, long lists become a compact list with
// the selected item's details beside it, and card lists go two columns. Below it, today's single column.
public partial class HealthViewModel
{
    public const double WideLayoutMinWidth = 1300;
    public const double ToolsMaxWidth = 1600;

    [ObservableProperty]
    private bool _isWideLayout;

    public bool IsNarrowLayout => !IsWideLayout;

    // Card lists (Cache Cleaner, Pattern Fixer) go two columns when wide.
    public int CardColumns => IsWideLayout ? 2 : 1;

    // The optimizer's 3D view can use the whole window.
    public double ContentMaxWidth => IsOptimizerActive ? double.PositiveInfinity : ToolsMaxWidth;

    public void UpdateLayoutWidth(double contentWidth) => IsWideLayout = contentWidth >= WideLayoutMinWidth;

    partial void OnIsWideLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNarrowLayout));
        OnPropertyChanged(nameof(CardColumns));
        if (value)
        {
            SelectedConflictCard ??= FilteredConflictCards.FirstOrDefault();
            SelectedMeshRow ??= FilteredMeshRows.FirstOrDefault();
        }
        RefreshMeshPreview();
        OnCollectionLayoutWidthChanged(value);
    }

    partial void OnActiveSectionChanged(string value)
    {
        OnPropertyChanged(nameof(ContentMaxWidth));
        RefreshMeshPreview();
    }

    // After a list is filtered again, keep the same item selected. When it left the list (ignored,
    // filtered out), move to the one that took its place, so clearing items works like an inbox.
    private static T? KeepSelection<T>(IList<T> list, T? selected, int oldIndex, Func<T, T, bool>? same = null) where T : class
    {
        if (list.Count == 0) return null;
        if (selected != null && list.Contains(selected)) return selected;
        if (selected != null && same != null && list.FirstOrDefault(x => same(x, selected)) is { } match) return match;
        if (oldIndex < 0) return list[0];
        return list[Math.Min(oldIndex, list.Count - 1)];
    }

    #region Conflict Scanner

    [ObservableProperty]
    private SimpleConflictCardViewModel? _selectedConflictCard;

    partial void OnSelectedConflictCardChanged(SimpleConflictCardViewModel? value) => OnPropertyChanged(nameof(HasSelectedConflictCard));

    public bool HasSelectedConflictCard => SelectedConflictCard != null;

    // The list control clears its selection while the list is replaced, so the caller passes the old one in.
    private void SelectConflictAfterFilter(SimpleConflictCardViewModel? previous, int oldIndex)
    {
        if (!IsWideLayout && previous == null) return;
        SelectedConflictCard = KeepSelection(FilteredConflictCards, previous, oldIndex);
    }

    #endregion

    #region Mesh Report

    [ObservableProperty]
    private MeshReportRowViewModel? _selectedMeshRow;

    [ObservableProperty]
    private MeshViewerViewModel? _meshPreview;

    public bool HasSelectedMeshRow => SelectedMeshRow != null;

    private CancellationTokenSource? _meshPreviewCts;
    private PackageInspection? _meshPreviewInspection;
    private Task? _meshPreviewLoad;
    private MeshReportRowViewModel? _meshPreviewRow;
    private bool _reselectingMesh;

    partial void OnSelectedMeshRowChanged(MeshReportRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedMeshRow));
        if (!_reselectingMesh) RefreshMeshPreview();
    }

    private void SelectMeshAfterFilter(MeshReportRowViewModel? previous, int oldIndex)
    {
        if (!IsWideLayout && previous == null) return;
        SelectedMeshRow = KeepSelection(FilteredMeshRows, previous, oldIndex,
            (a, b) => a.Model.MetaEntityId == b.Model.MetaEntityId && a.Model.ResourceKey.Equals(b.Model.ResourceKey));
    }

    // The preview opens the selected item's file; it is only open while it can be seen.
    private void RefreshMeshPreview()
    {
        var row = IsWideLayout && IsMeshReportActive ? SelectedMeshRow : null;
        if (row != null && row == _meshPreviewRow && MeshPreview != null) return;
        ReleaseMeshPreview();
        if (row != null) _ = LoadMeshPreviewAsync(row);
    }

    private async Task LoadMeshPreviewAsync(MeshReportRowViewModel row)
    {
        var cts = new CancellationTokenSource();
        _meshPreviewCts = cts;
        _meshPreviewRow = row;
        try
        {
            // Arrowing through the list should not open every file on the way.
            await Task.Delay(150, cts.Token);
            using var scope = _serviceProvider.CreateScope();
            var inspection = await scope.ServiceProvider.GetRequiredService<PackageInspectorService>()
                .InspectAsync(row.Model.MetaEntityId, cts.Token);
            if (cts.IsCancellationRequested)
            {
                inspection.Dispose();
                return;
            }
            var viewer = new MeshViewerViewModel(inspection, row.Model.ResourceKey, row.Title, canOpenInOptimizer: false);
            _meshPreviewInspection = inspection;
            MeshPreview = viewer;
            _meshPreviewLoad = viewer.LoadAsync();
            await _meshPreviewLoad;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not open the mesh preview", ex, "HealthViewModel");
        }
    }

    // Also called before the optimizer opens files, so nothing here holds them.
    public void ReleaseMeshPreview()
    {
        _meshPreviewCts?.Cancel();
        _meshPreviewCts = null;
        _meshPreviewRow = null;
        var viewer = MeshPreview;
        var inspection = _meshPreviewInspection;
        var load = _meshPreviewLoad;
        MeshPreview = null;
        _meshPreviewInspection = null;
        _meshPreviewLoad = null;
        viewer?.Dispose();
        if (inspection == null) return;
        // The viewer may still be reading on a background thread: close the file once it has stopped.
        if (load == null || load.IsCompleted) inspection.Dispose();
        else load.ContinueWith(_ => inspection.Dispose(), TaskScheduler.Default);
    }

    #endregion
}
