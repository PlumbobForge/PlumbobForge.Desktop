using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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

// One package the CC optimizer changed, as found from its backups.
public sealed partial class CcOptimizedEntry : ObservableObject
{
    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    public long ItemId { get; }
    public MeshReportRowViewModel Row { get; }
    public IReadOnlyList<MeshReportRow> Rows { get; }
    public DateTime OptimizedAt { get; }
    public string FullPath { get; }
    public int ChangedCount { get; }
    public int Before { get; }
    public int After { get; }

    public string Title => ChangedCount > 1 ? L("optimizer.title_more", Row.Title, ChangedCount - 1) : Row.Title;
    public string Subtitle => Row.Subtitle;
    public string Location => string.IsNullOrEmpty(Row.SetText) ? Row.FileName : $"{Row.SetText} · {Row.FileName}";
    public string FileName => Row.FileName;
    public bool HasCounts => Before > 0;
    public string BeforeText => MeshFormat.Compact(Before);
    public string AfterText => MeshFormat.Compact(After);
    public string CountsTip => L("optimizer.history_counts_tip", Before.ToString("N0"), After.ToString("N0"));
    public string DateText => FormatDate(OptimizedAt);
    public string DateTip => OptimizedAt.ToString("f", CultureInfo.CurrentCulture);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    private bool _isConfirmingRestore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    private bool _isRestoring;

    public bool ShowActions => !IsConfirmingRestore && !IsRestoring;

    public CcOptimizedEntry(long itemId, DateTime optimizedAt, string fullPath, IReadOnlyList<MeshReportRow> rows)
    {
        ItemId = itemId;
        OptimizedAt = optimizedAt;
        FullPath = fullPath;
        Rows = rows;
        var changed = rows.Where(r => r.TrianglesBefore is int b && b != r.Triangles).ToList();
        ChangedCount = changed.Count;
        Before = changed.Sum(r => r.TrianglesBefore ?? 0);
        After = changed.Sum(r => r.Triangles);
        var main = changed.OrderByDescending(r => r.TrianglesBefore).FirstOrDefault()
            ?? rows.OrderByDescending(r => r.Triangles).First();
        Row = new MeshReportRowViewModel(main);
    }

    public static string FormatDate(DateTime at)
    {
        var culture = CultureInfo.CurrentCulture;
        var today = DateTime.Today;
        if (at.Date == today) return L("optimizer.history_today", at.ToString("t", culture));
        if (at.Date == today.AddDays(-1)) return L("optimizer.history_yesterday", at.ToString("t", culture));
        string format = at.Year == today.Year ? "d MMM" : "d MMM yyyy";
        return at.ToString(format, culture);
    }
}

public sealed partial class CcOptimizerViewModel
{
    public const string SortNewest = "newest";
    public const string SortMostSaved = "saved";
    public const string SortName = "name";

    private readonly List<CcOptimizedEntry> _history = new();
    // Scans by item and when it was optimized, so coming back to the list is instant.
    private readonly Dictionary<long, (DateTime At, CcOptimizedEntry? Entry)> _historyCache = new();
    private int _historyVersion;

    public ObservableCollection<CcOptimizedEntry> HistoryItems { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQueue))]
    private bool _showHistory;

    [ObservableProperty] private bool _isLoadingHistory;
    [ObservableProperty] private string _historyProgressText = string.Empty;
    [ObservableProperty] private string? _historyStatus;
    [ObservableProperty] private string _historySearch = string.Empty;
    [ObservableProperty] private string _historySort = SortNewest;
    [ObservableProperty] private int _historyCount;

    public bool ShowQueue => !ShowHistory;
    public bool HasHistoryStatus => !string.IsNullOrEmpty(HistoryStatus);
    public bool HasHistorySearch => !string.IsNullOrEmpty(HistorySearch);
    public bool ShowHistoryList => HistoryItems.Count > 0;
    public bool ShowHistoryEmpty => !IsLoadingHistory && _history.Count == 0;
    public bool ShowHistoryNoMatch => !IsLoadingHistory && _history.Count > 0 && HistoryItems.Count == 0;
    public string HistoryTabText => HistoryCount > 0 ? L("optimizer.history_tab_count", HistoryCount) : L("optimizer.history_tab");
    public string HistoryTotalText => L("optimizer.history_total", MeshFormat.Compact(Math.Max(0, _history.Sum(e => e.Before - e.After))));
    public bool HasHistoryTotal => _history.Any(e => e.Before > e.After);
    public string HistorySortText => L($"optimizer.history_sort_{HistorySort}");
    public bool IsSortNewest => HistorySort == SortNewest;
    public bool IsSortMostSaved => HistorySort == SortMostSaved;
    public bool IsSortName => HistorySort == SortName;

    partial void OnHistoryCountChanged(int value) => OnPropertyChanged(nameof(HistoryTabText));
    partial void OnHistoryStatusChanged(string? value) => OnPropertyChanged(nameof(HasHistoryStatus));
    partial void OnIsLoadingHistoryChanged(bool value) => RaiseHistoryList();

    partial void OnHistorySearchChanged(string value)
    {
        OnPropertyChanged(nameof(HasHistorySearch));
        FilterHistory();
    }

    partial void OnHistorySortChanged(string value)
    {
        OnPropertyChanged(nameof(HistorySortText));
        OnPropertyChanged(nameof(IsSortNewest));
        OnPropertyChanged(nameof(IsSortMostSaved));
        OnPropertyChanged(nameof(IsSortName));
        FilterHistory();
    }

    partial void OnShowHistoryChanged(bool value)
    {
        if (value) _ = LoadHistoryAsync();
    }

    // Count for the tab before the list was opened; scanning may later drop items whose backups went stale.
    public void CountHistory()
    {
        try { HistoryCount = MeshBackupStore.OptimizedItems().Count; }
        catch (Exception) { }
    }

    [RelayCommand]
    private void ShowQueueView() => ShowHistory = false;

    [RelayCommand]
    private void ShowHistoryView() => ShowHistory = true;

    [RelayCommand]
    private void SetHistorySort(string sort) => HistorySort = sort;

    [RelayCommand]
    private void ClearHistorySearch() => HistorySearch = string.Empty;

    public async Task LoadHistoryAsync()
    {
        int version = Interlocked.Increment(ref _historyVersion);
        IsLoadingHistory = true;
        try
        {
            var found = await Task.Run(MeshBackupStore.OptimizedItems);
            if (version != _historyVersion) return;
            HistoryCount = found.Count;

            Dictionary<long, string> paths;
            using (var scope = App.Services?.CreateScope())
            {
                var db = scope?.ServiceProvider.GetRequiredService<AppDbContext>()
                    ?? throw new InvalidOperationException("Services are not available");
                var ids = found.Keys.ToList();
                paths = (await db.AllMetaEntities.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(_cts.Token))
                    .ToDictionary(m => m.Id, m => m.CompleteFileName);
            }

            var entries = new List<CcOptimizedEntry>();
            int done = 0;
            using (var scope = App.Services?.CreateScope())
            {
                var report = scope?.ServiceProvider.GetRequiredService<MeshReportService>()
                    ?? throw new InvalidOperationException("Services are not available");
                foreach (var (id, at) in found.OrderByDescending(f => f.Value))
                {
                    HistoryProgressText = L("optimizer.history_loading", ++done, found.Count);
                    if (!paths.TryGetValue(id, out var path)) continue;
                    if (!_historyCache.TryGetValue(id, out var cached) || cached.At != at)
                    {
                        var rows = await report.ScanOptimizedItemAsync(id, _cts.Token);
                        cached = (at, rows is { Count: > 0 } ? new CcOptimizedEntry(id, at, path, rows) : null);
                        _historyCache[id] = cached;
                    }
                    if (version != _historyVersion) return;
                    if (cached.Entry != null) entries.Add(cached.Entry);
                }
            }

            _history.Clear();
            _history.AddRange(entries);
            HistoryCount = _history.Count;
            FilterHistory();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not list optimized CC", ex, "CcOptimizerViewModel");
            HistoryStatus = L("optimizer.history_failed", ex.Message);
        }
        finally
        {
            if (version == _historyVersion)
            {
                IsLoadingHistory = false;
                HistoryProgressText = string.Empty;
            }
        }
    }

    private void FilterHistory()
    {
        string q = HistorySearch.Trim();
        IEnumerable<CcOptimizedEntry> list = _history;
        if (q.Length > 0)
        {
            list = list.Where(e => e.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || e.FileName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || e.Location.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || e.Subtitle.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        }
        list = HistorySort switch
        {
            SortMostSaved => list.OrderByDescending(e => e.Before - e.After).ThenByDescending(e => e.OptimizedAt),
            SortName => list.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => list.OrderByDescending(e => e.OptimizedAt),
        };
        HistoryItems.Clear();
        foreach (var e in list) HistoryItems.Add(e);
        RaiseHistoryList();
    }

    private void RaiseHistoryList()
    {
        OnPropertyChanged(nameof(ShowHistoryList));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
        OnPropertyChanged(nameof(ShowHistoryNoMatch));
        OnPropertyChanged(nameof(HistoryTotalText));
        OnPropertyChanged(nameof(HasHistoryTotal));
    }

    // Saves and restores change the backups; the list catches up the next time it is shown.
    private void OnHistoryChanged()
    {
        CountHistory();
        if (ShowHistory) _ = LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task RetuneEntry(CcOptimizedEntry? entry)
    {
        if (entry == null || !CanEditList) return;
        ShowHistory = false;
        await AddItemsAsync(new[] { entry.ItemId });
        // Already in the list: adding it again does not select it.
        var queued = Items.FirstOrDefault(i => i.ItemId == entry.ItemId);
        if (queued != null) SelectItem(queued);
    }

    [RelayCommand]
    private void AskRestoreEntry(CcOptimizedEntry? entry)
    {
        if (entry == null) return;
        foreach (var e in _history) e.IsConfirmingRestore = e == entry;
    }

    [RelayCommand]
    private void CancelRestoreEntry(CcOptimizedEntry? entry)
    {
        if (entry != null) entry.IsConfirmingRestore = false;
    }

    [RelayCommand]
    private async Task RestoreEntry(CcOptimizedEntry? entry)
    {
        if (entry == null || entry.IsRestoring || IsSaving) return;
        entry.IsConfirmingRestore = false;
        if (Sims3GameProcess.IsGameRunning())
        {
            HistoryStatus = L("optimizer.game_running");
            return;
        }

        entry.IsRestoring = true;
        HistoryStatus = null;
        // The editor may hold the same file open.
        var selected = SelectedItem;
        bool releasedEditor = selected?.ItemId == entry.ItemId;
        if (releasedEditor)
        {
            try { await _work.WaitAsync(_cts.Token); } catch (OperationCanceledException) { entry.IsRestoring = false; return; }
            try { ClearEditor(holdingWork: true); } finally { _work.Release(); }
        }

        bool restored = false;
        try
        {
            using var scope = App.Services?.CreateScope();
            var service = scope?.ServiceProvider.GetRequiredService<CcOptimizerService>()
                ?? throw new InvalidOperationException("Services are not available");
            restored = await service.RestoreAsync(entry.ItemId, null, _cts.Token);
            HistoryStatus = restored
                ? (entry.Row.Model.IsVault ? L("optimizer.history_restored", entry.FileName) : $"{L("optimizer.history_restored", entry.FileName)} {L("optimizer.saved_rebuild")}")
                : L("optimizer.history_nothing_to_restore", entry.FileName);
        }
        catch (GameRunningException)
        {
            HistoryStatus = L("optimizer.game_running");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Could not restore the original of item {entry.ItemId}", ex, "CcOptimizerViewModel");
            HistoryStatus = L("optimizer.history_restore_failed", ex.Message);
        }
        finally
        {
            entry.IsRestoring = false;
        }

        _historyCache.Remove(entry.ItemId);
        _history.Remove(entry);
        HistoryCount = _history.Count;
        FilterHistory();
        if (restored) ItemsSaved?.Invoke(new[] { entry.ItemId });
        if (releasedEditor && selected != null && Items.Contains(selected)) await LoadAsync(selected);
    }

    // After Look Inside, which can restore parts of an item.
    public async Task RefreshEntryAsync(long itemId)
    {
        _historyCache.Remove(itemId);
        if (ShowHistory) await LoadHistoryAsync();
        else CountHistory();
    }

    public static string? RevealPath(CcOptimizedEntry entry) => File.Exists(entry.FullPath) ? entry.FullPath : null;
}
