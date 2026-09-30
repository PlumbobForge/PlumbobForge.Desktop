using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class HealthViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Sims3HealthService _healthService;
    private readonly Sims3CollectionService _collectionService;
    private readonly IOptions<PlumbobForgeOptions> _options;

    #region Navigation / Sections

    [ObservableProperty]
    private string _activeSection = "Cache"; // "Cache", "Conflicts", "Patterns", or "Collections"

    public bool IsCacheCleanerActive => ActiveSection == "Cache";
    public bool IsConflictScannerActive => ActiveSection == "Conflicts";
    public bool IsPatternFixerActive => ActiveSection == "Patterns";
    public bool IsCollectionCreatorActive => ActiveSection == "Collections";

    public string ActiveToolTitle => ActiveSection switch
    {
        "Conflicts" => LocalizationManager.Instance.GetString("tools.conflicts.title"),
        "Patterns" => LocalizationManager.Instance.GetString("tools.patterns.title"),
        "Collections" => LocalizationManager.Instance.GetString("tools.collections.title"),
        _ => LocalizationManager.Instance.GetString("tools.cache.title")
    };

    public string ActiveToolIcon => ActiveSection switch
    {
        "Conflicts" => "RegularShield",
        "Patterns" => "RegularPalette",
        "Collections" => "RegularFolder",
        _ => "RegularTrash"
    };

    [RelayCommand]
    public void SelectSection(string section)
    {
        ActiveSection = section;
        OnPropertyChanged(nameof(IsCacheCleanerActive));
        OnPropertyChanged(nameof(IsConflictScannerActive));
        OnPropertyChanged(nameof(IsPatternFixerActive));
        OnPropertyChanged(nameof(IsCollectionCreatorActive));
        OnPropertyChanged(nameof(ActiveToolTitle));
        OnPropertyChanged(nameof(ActiveToolIcon));

        if (_options?.Value != null && _options.Value.LastActiveTool != section)
        {
            _options.Value.LastActiveTool = section;
            _ = AppSettingsService.SaveOptionsAsync(_options.Value);
        }

        if (section == "Patterns" && !HasScannedPatterns && !IsScanningPatterns)
        {
            _ = ScanPatternsAsync();
        }
        else if (section == "Collections" && !IsLoadingCollections && !IsCollectionEditorOpen)
        {
            _ = LoadCollectionsAsync();
        }
    }

    #endregion

    #region Toast Notifications

    [ObservableProperty]
    private bool _isCacheToastVisible = false;

    [ObservableProperty]
    private bool _isCacheToastError = false;

    [ObservableProperty]
    private string _cacheToastMessage = string.Empty;

    public void ShowCacheToast(string message, bool isError)
    {
        CacheToastMessage = message;
        IsCacheToastError = isError;
        IsCacheToastVisible = true;

        Task.Delay(5000).ContinueWith(_ =>
        {
            IsCacheToastVisible = false;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    #endregion

    public HealthViewModel(
        IServiceProvider serviceProvider,
        Sims3HealthService healthService,
        Sims3CollectionService collectionService,
        IOptions<PlumbobForgeOptions> options)
    {
        _serviceProvider = serviceProvider;
        _healthService = healthService;
        _collectionService = collectionService;
        _options = options;

        SimpleConflictCardViewModel.OnTogglePackageRequested = (item, isEnabled) =>
        {
            _ = SetPackageEnabledAsync(item, isEnabled);
        };

        SimpleConflictCardViewModel.OnNavigateRequested = (item) =>
        {
            NavigateToPackageItem(item);
        };

        PatternIssueItemViewModel.OnFixRequested = (item) =>
        {
            _ = FixSinglePatternAsync(item);
        };

        PatternIssueItemViewModel.OnNavigateRequested = (item) =>
        {
            NavigateToPackageItem(item.MetaEntityId, item.SetId);
        };

        Sims3FolderPath = _healthService.GetSims3FolderPath();
        _ = ScanCachesAsync();
        InitCollectionIcons();

        var initialTool = options?.Value?.LastActiveTool;
        if (!string.IsNullOrWhiteSpace(initialTool) && (initialTool == "Cache" || initialTool == "Conflicts" || initialTool == "Patterns" || initialTool == "Collections"))
        {
            SelectSection(initialTool);
        }

        LocalizationManager.Instance.LanguageChanged += _ =>
        {
            OnPropertyChanged(nameof(ActiveToolTitle));
        };
    }
}
