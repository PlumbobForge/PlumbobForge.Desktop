using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class CacheCategoryItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string WhenToClear { get; }
    public string WhyClear { get; }
    public string WhyNotClear { get; }
    public string RegenerationImpact { get; }
    public bool IsRecommended { get; }
    public string? SafetyNotice { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isInfoExpanded = false;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private long _totalSizeBytes;

    public string FormattedSize => CacheGroupInfo.FormatBytes(TotalSizeBytes);

    public CacheCategoryItemViewModel(CacheGroupInfo info)
    {
        Id = info.Id;
        Title = info.Title;
        Description = info.Description;
        WhenToClear = info.WhenToClear;
        WhyClear = info.WhyClear;
        WhyNotClear = info.WhyNotClear;
        RegenerationImpact = info.RegenerationImpact;
        IsRecommended = info.IsRecommended;
        IsSelected = info.IsSelected;
        SafetyNotice = info.SafetyNotice;
        FileCount = info.FileCount;
        TotalSizeBytes = info.TotalSizeBytes;
    }

    [RelayCommand]
    public void ToggleInfo()
    {
        IsInfoExpanded = !IsInfoExpanded;
    }
}

public partial class HealthViewModel
{
    #region Cache Cleaner Properties & Commands

    public ObservableCollection<CacheCategoryItemViewModel> CacheCategories { get; } = new();

    [ObservableProperty]
    private string _sims3FolderPath = string.Empty;

    [ObservableProperty]
    private bool _isScanningCaches = false;

    [ObservableProperty]
    private bool _isCleaningCaches = false;

    [ObservableProperty]
    private long _totalCacheSizeBytes = 0;

    [ObservableProperty]
    private long _selectedCacheSizeBytes = 0;

    [ObservableProperty]
    private int _totalCacheFilesCount = 0;

    [ObservableProperty]
    private int _selectedCacheFilesCount = 0;

    public string FormattedTotalCacheSize => CacheGroupInfo.FormatBytes(TotalCacheSizeBytes);
    public string FormattedSelectedCacheSize => CacheGroupInfo.FormatBytes(SelectedCacheSizeBytes);

    [RelayCommand]
    public async Task ScanCachesAsync()
    {
        if (IsScanningCaches) return;

        IsScanningCaches = true;
        try
        {
            var groups = await _healthService.GetCacheStatusAsync();
            CacheCategories.Clear();

            foreach (var g in groups)
            {
                var vm = new CacheCategoryItemViewModel(g);
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(CacheCategoryItemViewModel.IsSelected))
                    {
                        RecalculateCacheTotals();
                    }
                };
                CacheCategories.Add(vm);
            }

            RecalculateCacheTotals();
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Error scanning caches: {ex.Message}", isError: true);
        }
        finally
        {
            IsScanningCaches = false;
        }
    }

    private void RecalculateCacheTotals()
    {
        long totalBytes = 0;
        int totalFiles = 0;
        long selBytes = 0;
        int selFiles = 0;

        foreach (var cat in CacheCategories)
        {
            totalBytes += cat.TotalSizeBytes;
            totalFiles += cat.FileCount;

            if (cat.IsSelected)
            {
                selBytes += cat.TotalSizeBytes;
                selFiles += cat.FileCount;
            }
        }

        TotalCacheSizeBytes = totalBytes;
        TotalCacheFilesCount = totalFiles;
        SelectedCacheSizeBytes = selBytes;
        SelectedCacheFilesCount = selFiles;

        OnPropertyChanged(nameof(FormattedTotalCacheSize));
        OnPropertyChanged(nameof(FormattedSelectedCacheSize));
    }

    [RelayCommand]
    public void SelectRecommendedCaches()
    {
        foreach (var cat in CacheCategories)
        {
            cat.IsSelected = cat.IsRecommended;
        }
        RecalculateCacheTotals();
    }

    [RelayCommand]
    public void SelectAllCaches()
    {
        foreach (var cat in CacheCategories)
        {
            cat.IsSelected = true;
        }
        RecalculateCacheTotals();
    }

    [RelayCommand]
    public void DeselectAllCaches()
    {
        foreach (var cat in CacheCategories)
        {
            cat.IsSelected = false;
        }
        RecalculateCacheTotals();
    }

    [RelayCommand]
    public async Task CleanSelectedCachesAsync()
    {
        if (IsCleaningCaches || IsScanningCaches) return;

        var selectedIds = CacheCategories.Where(c => c.IsSelected && c.FileCount > 0).Select(c => c.Id).ToList();
        if (selectedIds.Count == 0)
        {
            ShowCacheToast("No caches selected to clean.", isError: false);
            return;
        }

        IsCleaningCaches = true;
        try
        {
            var result = await _healthService.ClearCachesAsync(selectedIds);
            if (result.FilesDeleted > 0)
            {
                ShowCacheToast($"Successfully cleaned {result.FilesDeleted} cache files ({result.FormattedBytesReclaimed} reclaimed)!", isError: false);
            }
            else
            {
                ShowCacheToast("Cache clean finished. No files were deleted.", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Failed to clean caches: {ex.Message}", isError: true);
        }
        finally
        {
            IsCleaningCaches = false;
        }

        await ScanCachesAsync();
    }

    [RelayCommand]
    public void OpenSims3Folder()
    {
        try
        {
            string path = Sims3FolderPath;
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ShowCacheToast($"Unable to open folder: {ex.Message}", isError: true);
        }
    }

    #endregion
}
