using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Utils;

namespace PlumbobForge.Desktop.ViewModels;

public partial class PatternIssueItemViewModel : ObservableObject
{
    public PatternIssueItem Model { get; }

    public string PackagePath => Model.PackagePath;
    public string FileName => Model.FileName;
    public string SetName => Model.SetName;
    public long? SetId => Model.SetId;
    public long? MetaEntityId => Model.MetaEntityId;
    public string PatternName => Model.PatternName;
    public string Category => Model.Category;
    public int CorruptedKeyCount => Model.CorruptedKeyCount;
    public List<string> SampleBrokenKeys => Model.SampleBrokenKeys;
    public string BrokenKeysSampleText => SampleBrokenKeys.Count > 0 ? string.Join(", ", SampleBrokenKeys.Take(2)) : string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isBroken;

    [ObservableProperty]
    private bool _isFixed;

    [ObservableProperty]
    private bool _isFixing;

    public PatternIssueItemViewModel(PatternIssueItem model)
    {
        Model = model;
        _isSelected = model.IsSelected;
        _isBroken = model.IsBroken;
        _isFixed = model.IsFixed;
    }

    public static Action<PatternIssueItemViewModel>? OnFixRequested { get; set; }
    public static Action<PatternIssueItemViewModel>? OnNavigateRequested { get; set; }

    [RelayCommand]
    public void Fix()
    {
        OnFixRequested?.Invoke(this);
    }

    [RelayCommand]
    public void NavigateToItem()
    {
        OnNavigateRequested?.Invoke(this);
    }
}

public partial class HealthViewModel
{
    #region Pattern Fixer Properties & Commands

    private readonly List<PatternIssueItemViewModel> _allPatternIssuesList = new();
    private CancellationTokenSource? _patternSearchCts;

    public RangeObservableCollection<PatternIssueItemViewModel> FilteredPatternIssues { get; } = new();

    [ObservableProperty]
    private bool _isScanningPatterns = false;

    [ObservableProperty]
    private bool _hasScannedPatterns = false;

    [ObservableProperty]
    private bool _isFixingPatterns = false;

    [ObservableProperty]
    private int _totalPatternsFound = 0;

    [ObservableProperty]
    private int _brokenPatternsCount = 0;

    [ObservableProperty]
    private int _fixedPatternsCount = 0;

    [ObservableProperty]
    private string _patternFilter = "Broken"; // "Broken", "All", "Fixed"

    [ObservableProperty]
    private string _patternSearchQuery = string.Empty;

    public bool IsPatternFilterBroken => PatternFilter == "Broken";
    public bool IsPatternFilterAll => PatternFilter == "All";
    public bool IsPatternFilterFixed => PatternFilter == "Fixed";

    public bool HasBrokenPatterns => BrokenPatternsCount > 0;
    public bool IsNoPatternIssues => HasScannedPatterns && BrokenPatternsCount == 0;
    public bool HasPatternSearchQuery => !string.IsNullOrEmpty(PatternSearchQuery);

    partial void OnPatternFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsPatternFilterBroken));
        OnPropertyChanged(nameof(IsPatternFilterAll));
        OnPropertyChanged(nameof(IsPatternFilterFixed));
        ApplyPatternFilter();
    }

    partial void OnPatternSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasPatternSearchQuery));
        _patternSearchCts?.Cancel();
        _patternSearchCts = new CancellationTokenSource();
        var token = _patternSearchCts.Token;

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
                            ApplyPatternFilter();
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    [RelayCommand]
    public void SetPatternFilter(string filter)
    {
        PatternFilter = filter;
    }

    [RelayCommand]
    public void ClearPatternSearch()
    {
        PatternSearchQuery = string.Empty;
    }

    [RelayCommand]
    public void SelectAllPatterns()
    {
        foreach (var item in FilteredPatternIssues)
        {
            if (item.IsBroken)
                item.IsSelected = true;
        }
    }

    [RelayCommand]
    public void DeselectAllPatterns()
    {
        foreach (var item in FilteredPatternIssues)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    public async Task ScanPatternsAsync()
    {
        if (IsScanningPatterns || IsFixingPatterns) return;
        IsScanningPatterns = true;
        try
        {
            var summary = await _healthService.ScanPatternsAsync();
            _allPatternIssuesList.Clear();

            var list = new List<PatternIssueItemViewModel>(summary.TotalPatternsFound);
            foreach (var item in summary.BrokenPatterns)
            {
                list.Add(new PatternIssueItemViewModel(item));
            }
            foreach (var item in summary.CleanPatterns)
            {
                list.Add(new PatternIssueItemViewModel(item));
            }

            _allPatternIssuesList.AddRange(list);

            TotalPatternsFound = summary.TotalPatternsFound;
            BrokenPatternsCount = summary.BrokenPatternsCount;
            FixedPatternsCount = summary.FixedPatternsCount;
            HasScannedPatterns = true;

            ApplyPatternFilter();
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error scanning patterns: {ex.Message}", isError: true);
        }
        finally
        {
            IsScanningPatterns = false;
        }
    }

    [RelayCommand]
    public async Task FixSelectedPatternsAsync()
    {
        var selected = _allPatternIssuesList.Where(p => p.IsSelected && p.IsBroken).ToList();
        if (selected.Count == 0 || IsFixingPatterns) return;

        IsFixingPatterns = true;
        try
        {
            foreach (var item in selected) item.IsFixing = true;

            var paths = selected.Select(p => p.PackagePath).ToList();
            var result = await _healthService.FixPatternPackagesAsync(paths);

            foreach (var item in selected)
            {
                item.IsFixing = false;
                item.IsBroken = false;
                item.IsFixed = true;
                item.IsSelected = false;
            }

            BrokenPatternsCount = _allPatternIssuesList.Count(p => p.IsBroken);
            FixedPatternsCount += result.FixedCount;

            ShowCacheToast($"Successfully repaired {result.FixedCount} pattern package(s).", isError: false);
            ApplyPatternFilter();
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error repairing patterns: {ex.Message}", isError: true);
        }
        finally
        {
            IsFixingPatterns = false;
        }
    }

    [RelayCommand]
    public async Task FixAllBrokenPatternsAsync()
    {
        var broken = _allPatternIssuesList.Where(p => p.IsBroken).ToList();
        if (broken.Count == 0 || IsFixingPatterns) return;

        IsFixingPatterns = true;
        try
        {
            foreach (var item in broken) item.IsFixing = true;

            var paths = broken.Select(p => p.PackagePath).ToList();
            var result = await _healthService.FixPatternPackagesAsync(paths);

            foreach (var item in broken)
            {
                item.IsFixing = false;
                item.IsBroken = false;
                item.IsFixed = true;
                item.IsSelected = false;
            }

            BrokenPatternsCount = _allPatternIssuesList.Count(p => p.IsBroken);
            FixedPatternsCount += result.FixedCount;

            ShowCacheToast($"Successfully repaired {result.FixedCount} broken pattern package(s).", isError: false);
            ApplyPatternFilter();
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error repairing patterns: {ex.Message}", isError: true);
        }
        finally
        {
            IsFixingPatterns = false;
        }
    }

    public async Task FixSinglePatternAsync(PatternIssueItemViewModel item)
    {
        if (item.IsFixing || !item.IsBroken) return;
        item.IsFixing = true;
        try
        {
            bool ok = await Task.Run(() => _healthService.FixPatternPackage(item.PackagePath));
            if (ok)
            {
                item.IsBroken = false;
                item.IsFixed = true;
                item.IsSelected = false;
                BrokenPatternsCount = _allPatternIssuesList.Count(p => p.IsBroken);
                FixedPatternsCount++;
                ShowCacheToast($"Fixed pattern: {item.PatternName}", isError: false);
                ApplyPatternFilter();
            }
            else
            {
                ShowCacheToast($"Failed to repair: {item.FileName}", isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            item.IsFixing = false;
        }
    }

    private void ApplyPatternFilter()
    {
        var query = PatternSearchQuery.Trim();

        var filtered = _allPatternIssuesList.Where(p =>
        {
            if (PatternFilter == "Broken" && !p.IsBroken) return false;
            if (PatternFilter == "Fixed" && !p.IsFixed) return false;

            if (!string.IsNullOrEmpty(query))
            {
                bool matchesName = p.PatternName.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool matchesFile = p.FileName.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool matchesSet = p.SetName.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool matchesCat = p.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matchesName && !matchesFile && !matchesSet && !matchesCat) return false;
            }
            return true;
        }).ToList();

        FilteredPatternIssues.ReplaceAll(filtered);
        OnPropertyChanged(nameof(IsNoPatternIssues));
    }

    #endregion
}
