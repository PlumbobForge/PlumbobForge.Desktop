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
    private string _activeSection = "Cache"; // "Cache", "Conflicts", "Patterns", "Collections" or "Meshes"

    public bool IsCacheCleanerActive => ActiveSection == "Cache";
    public bool IsConflictScannerActive => ActiveSection == "Conflicts";
    public bool IsPatternFixerActive => ActiveSection == "Patterns";
    public bool IsCollectionCreatorActive => ActiveSection == "Collections";
    public bool IsMeshReportActive => ActiveSection == "Meshes";

    public string ActiveToolTitle => ActiveSection switch
    {
        "Conflicts" => LocalizationManager.Instance.GetString("tools.conflicts.title"),
        "Patterns" => LocalizationManager.Instance.GetString("tools.patterns.title"),
        "Collections" => LocalizationManager.Instance.GetString("tools.collections.title"),
        "Meshes" => LocalizationManager.Instance.GetString("tools.meshes.title"),
        _ => LocalizationManager.Instance.GetString("tools.cache.title")
    };

    public string ActiveToolIcon => ActiveSection switch
    {
        "Conflicts" => "RegularShield",
        "Patterns" => "RegularPalette",
        "Collections" => "RegularLayers",
        "Meshes" => "RegularCube",
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
        OnPropertyChanged(nameof(IsMeshReportActive));
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
        else if (section == "Meshes" && !HasScannedMeshes && !IsScanningMeshes)
        {
            _ = ScanMeshesAsync();
        }
    }

    #endregion

    #region Pinned Tools

    public static readonly string[] Sections = { "Cache", "Conflicts", "Patterns", "Collections", "Meshes" };

    public static string SectionTitleKey(string section) => section switch
    {
        "Conflicts" => "tools.conflicts.title",
        "Patterns" => "tools.patterns.title",
        "Collections" => "tools.collections.title",
        "Meshes" => "tools.meshes.title",
        _ => "tools.cache.title"
    };

    public static PlumbobForge.Desktop.Controls.Icons.PackIconBoxIconsKind SectionIcon(string section) => section switch
    {
        "Conflicts" => PlumbobForge.Desktop.Controls.Icons.PackIconBoxIconsKind.RegularShield,
        "Patterns" => PlumbobForge.Desktop.Controls.Icons.PackIconBoxIconsKind.RegularPalette,
        "Collections" => PlumbobForge.Desktop.Controls.Icons.PackIconBoxIconsKind.RegularLayers,
        "Meshes" => PlumbobForge.Desktop.Controls.Icons.PackIconBoxIconsKind.RegularCube,
        _ => PlumbobForge.Desktop.Controls.Icons.PackIconBoxIconsKind.RegularTrash
    };

    // Raised after a section is pinned or unpinned; MainViewModel rebuilds the title-bar tabs.
    public event Action? PinnedToolsChanged;

    public System.Collections.Generic.IReadOnlyList<string> PinnedTools =>
        _options?.Value?.PinnedTools is { } list
            ? list.FindAll(s => Array.IndexOf(Sections, s) >= 0)
            : Array.Empty<string>();

    public bool IsSectionPinned(string section) => _options?.Value?.PinnedTools?.Contains(section) == true;

    public bool IsCachePinned => IsSectionPinned("Cache");
    public bool IsConflictsPinned => IsSectionPinned("Conflicts");
    public bool IsPatternsPinned => IsSectionPinned("Patterns");
    public bool IsCollectionsPinned => IsSectionPinned("Collections");
    public bool IsMeshesPinned => IsSectionPinned("Meshes");

    public string CachePinTip => PinTip("Cache");
    public string ConflictsPinTip => PinTip("Conflicts");
    public string PatternsPinTip => PinTip("Patterns");
    public string CollectionsPinTip => PinTip("Collections");
    public string MeshesPinTip => PinTip("Meshes");

    private string PinTip(string section) =>
        LocalizationManager.Instance.GetString(IsSectionPinned(section) ? "tools.unpin_tab" : "tools.pin_tab");

    [RelayCommand]
    public void TogglePinSection(string section)
    {
        var options = _options?.Value;
        if (options == null || Array.IndexOf(Sections, section) < 0) return;

        options.PinnedTools ??= new();
        if (!options.PinnedTools.Remove(section))
        {
            options.PinnedTools.Add(section);
        }
        _ = AppSettingsService.SaveOptionsAsync(options);

        RaisePinProperties();
        PinnedToolsChanged?.Invoke();
    }

    private void RaisePinProperties()
    {
        OnPropertyChanged(nameof(IsCachePinned));
        OnPropertyChanged(nameof(IsConflictsPinned));
        OnPropertyChanged(nameof(IsPatternsPinned));
        OnPropertyChanged(nameof(IsCollectionsPinned));
        OnPropertyChanged(nameof(IsMeshesPinned));
        OnPropertyChanged(nameof(CachePinTip));
        OnPropertyChanged(nameof(ConflictsPinTip));
        OnPropertyChanged(nameof(PatternsPinTip));
        OnPropertyChanged(nameof(CollectionsPinTip));
        OnPropertyChanged(nameof(MeshesPinTip));
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
        InitMeshReport();

        var initialTool = options?.Value?.LastActiveTool;
        if (!string.IsNullOrWhiteSpace(initialTool) && (initialTool == "Cache" || initialTool == "Conflicts" || initialTool == "Patterns" || initialTool == "Collections" || initialTool == "Meshes"))
        {
            SelectSection(initialTool);
        }

        LocalizationManager.Instance.LanguageChanged += _ =>
        {
            OnPropertyChanged(nameof(ActiveToolTitle));
            RaisePinProperties();
        };
    }
}
