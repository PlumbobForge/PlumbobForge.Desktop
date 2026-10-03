using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly PlumbobForgeOptions _options;
    private readonly PKGManager _pkgManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalizationService _localizer;
    private readonly UpdateService _updateService;

    [ObservableProperty]
    private string _statusToastMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusToastVisible = false;

    [ObservableProperty]
    private bool _isStatusToastError = false;

    [ObservableProperty]
    private bool _isSaving = false;

    private bool _isInitializing = false;

    #region Localization & Appearance

    [ObservableProperty]
    private LanguageOptionItemViewModel? _selectedLanguageOption;

    public ObservableCollection<LanguageOptionItemViewModel> LanguageOptions { get; } = new();

    public void SelectLanguage(LanguageOptionItemViewModel? lang)
    {
        if (lang == null) return;
        SelectedLanguageOption = lang;
        LocalizationManager.Instance.SetLanguage(lang.Code);
        _options.Language = lang.Code;
        _ = SaveSettingsInternalAsync(silent: true);
        OnPropertyChanged(nameof(CacheMethodDescription));
        OnPropertyChanged(nameof(CompressionDescription));
        OnPropertyChanged(nameof(CompressionLevelText));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    private string _selectedTheme = "Dark";

    public bool IsDarkTheme => string.Equals(SelectedTheme, "Dark", StringComparison.OrdinalIgnoreCase);
    public bool IsLightTheme => string.Equals(SelectedTheme, "Light", StringComparison.OrdinalIgnoreCase);
    public bool IsSystemTheme => string.Equals(SelectedTheme, "System", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    public void SetTheme(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme)) return;
        SelectedTheme = theme;
        ThemeService.ApplyTheme(theme);
        _options.Theme = theme;
        _ = SaveSettingsInternalAsync(silent: true);
    }

    public ObservableCollection<AccentColorItemViewModel> AccentOptions { get; } = new();

    [ObservableProperty]
    private string _selectedAccent = "Emerald";

    [RelayCommand]
    public void SetAccent(string? accentName)
    {
        if (string.IsNullOrWhiteSpace(accentName)) return;
        SelectedAccent = accentName;
        ThemeService.ApplyAccent(accentName);
        _options.AccentColor = accentName;

        foreach (var opt in AccentOptions)
        {
            opt.IsSelected = string.Equals(opt.Name, accentName, StringComparison.OrdinalIgnoreCase);
        }

        _ = SaveSettingsInternalAsync(silent: true);
    }

    [RelayCommand]
    public void SelectAccent(AccentColorItemViewModel? item)
    {
        if (item == null) return;
        SetAccent(item.Name);
    }

    #endregion

    #region General Options & Cache Settings

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDynamicCache))]
    [NotifyPropertyChangedFor(nameof(IsStaticCache))]
    [NotifyPropertyChangedFor(nameof(CacheMethodDescription))]
    private string _cacheMethod = "Dynamic";

    public bool IsDynamicCache => string.Equals(CacheMethod, "Dynamic", StringComparison.OrdinalIgnoreCase);
    public bool IsStaticCache => string.Equals(CacheMethod, "Static", StringComparison.OrdinalIgnoreCase);

    public string CacheMethodDescription => IsDynamicCache
        ? LocalizationManager.Instance.GetString("settings.engine.dynamic_cache_desc")
        : LocalizationManager.Instance.GetString("settings.engine.static_bundle_desc");

    [ObservableProperty]
    private bool _autoRebuildStaticCache = false;

    partial void OnAutoRebuildStaticCacheChanged(bool value)
    {
        if (_isInitializing) return;
        _options.AutoRebuildStaticCache = value;
        _ = SaveSettingsInternalAsync(silent: true);
    }

    // Slider: 0 = None (0), 1 = Balanced (1), 2 = High (3)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressionLevelText))]
    [NotifyPropertyChangedFor(nameof(CompressionDescription))]
    private double _compressionSliderValue = 1.0;

    public int CompressionLevel
    {
        get
        {
            int val = (int)Math.Round(CompressionSliderValue);
            return val switch
            {
                0 => 0,
                1 => 1,
                _ => 3
            };
        }
        set
        {
            CompressionSliderValue = value switch
            {
                0 => 0.0,
                1 => 1.0,
                _ => 2.0
            };
        }
    }

    public string CompressionLevelText => (int)Math.Round(CompressionSliderValue) switch
    {
        0 => LocalizationManager.Instance.GetString("settings.engine.none_label"),
        1 => LocalizationManager.Instance.GetString("settings.engine.balanced_label"),
        _ => LocalizationManager.Instance.GetString("settings.engine.high_label")
    };

    public string CompressionDescription => (int)Math.Round(CompressionSliderValue) switch
    {
        0 => LocalizationManager.Instance.GetString("settings.engine.none_desc"),
        1 => LocalizationManager.Instance.GetString("settings.engine.balanced_desc"),
        _ => LocalizationManager.Instance.GetString("settings.engine.high_desc")
    };

    partial void OnCompressionSliderValueChanged(double value)
    {
        if (_isInitializing) return;
        _options.CompressionLevel = CompressionLevel;
        _ = SaveSettingsInternalAsync(silent: true);
    }

    [ObservableProperty]
    private bool _enableAutoScan = true;

    partial void OnEnableAutoScanChanged(bool value)
    {
        if (_isInitializing) return;
        _options.EnableAutoScan = value;
        _ = SaveSettingsInternalAsync(silent: true);
    }

    partial void OnCacheMethodChanged(string value)
    {
        if (_isInitializing) return;
        _options.CacheMethod = value;
        _ = SaveSettingsInternalAsync(silent: true);
        _ = MarkAllSetsDirtyAsync();
    }

    [RelayCommand]
    public async Task SetDynamicCacheAsync()
    {
        if (string.Equals(CacheMethod, "Dynamic", StringComparison.OrdinalIgnoreCase)) return;
        CacheMethod = "Dynamic";
        _options.CacheMethod = "Dynamic";
        await SaveSettingsInternalAsync(silent: true);
        await MarkAllSetsDirtyAsync();
    }

    [RelayCommand]
    public async Task SetStaticCacheAsync()
    {
        if (string.Equals(CacheMethod, "Static", StringComparison.OrdinalIgnoreCase)) return;
        CacheMethod = "Static";
        _options.CacheMethod = "Static";
        await SaveSettingsInternalAsync(silent: true);
        await MarkAllSetsDirtyAsync();
    }

    private async Task MarkAllSetsDirtyAsync()
    {
        try
        {
            var sets = await _db.SetsEntities.ToListAsync();
            foreach (var set in sets)
            {
                set.CachedHash = null;
                set.Dirty = true;
            }
            await _db.SaveChangesAsync();
            _ = App.Services?.GetService<MainViewModel>()?.RefreshDirtyStateAsync();
        }
        catch { }
    }

    public ObservableCollection<string> ObservedFolders { get; } = new();

    [ObservableProperty]
    private int _totalSetsCount;

    [ObservableProperty]
    private int _totalItemsCount;

    #endregion

    #region Maintenance & Diagnostics

    [ObservableProperty]
    private bool _isMaintenanceRunning = false;

    [ObservableProperty]
    private string _maintenanceStatus = string.Empty;

    #endregion

    public SettingsViewModel(
        AppDbContext db,
        IOptions<PlumbobForgeOptions> options,
        PKGManager pkgManager,
        IServiceProvider serviceProvider,
        LocalizationService localizer,
        UpdateService updateService)
    {
        _db = db;
        _options = options.Value;
        _pkgManager = pkgManager;
        _serviceProvider = serviceProvider;
        _localizer = localizer;
        _updateService = updateService;

        _ = LoadSettingsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        _isInitializing = true;
        try
        {
            LanguageOptions.Clear();
            foreach (var lang in LocalizationManager.SupportedLanguages)
            {
                LanguageOptions.Add(new LanguageOptionItemViewModel(lang));
            }
            SelectedLanguageOption = LanguageOptions.FirstOrDefault(l => l.Code == LocalizationManager.Instance.CurrentLanguage)
                ?? LanguageOptions.FirstOrDefault(l => l.Code == "auto");

            SelectedTheme = string.IsNullOrWhiteSpace(_options.Theme) ? ThemeService.CurrentTheme : _options.Theme;
            SelectedAccent = string.IsNullOrWhiteSpace(_options.AccentColor) ? ThemeService.CurrentAccent : _options.AccentColor;

            AccentOptions.Clear();
            foreach (var accent in ThemeService.Accents)
            {
                AccentOptions.Add(new AccentColorItemViewModel(accent, string.Equals(accent.Name, SelectedAccent, StringComparison.OrdinalIgnoreCase)));
            }

            DocumentBaseDir = _options.DocumentBaseDir ?? string.Empty;
            GameFilesDir = _options.GameFilesDir ?? string.Empty;
            CacheMethod = string.IsNullOrEmpty(_options.CacheMethod) ? "Dynamic" : _options.CacheMethod;
            AutoRebuildStaticCache = _options.AutoRebuildStaticCache;
            CompressionLevel = _options.CompressionLevel;
            EnableAutoScan = _options.EnableAutoScan;

            ObservedFolders.Clear();
            if (_options.ObservedFolders != null)
            {
                foreach (var folder in _options.ObservedFolders)
                {
                    if (!string.IsNullOrWhiteSpace(folder) && !ObservedFolders.Contains(folder))
                    {
                        ObservedFolders.Add(folder);
                    }
                }
            }

            await LoadStatsAsync();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    [RelayCommand]
    public Task SaveSettingsAsync() => SaveSettingsInternalAsync(silent: false);

    public async Task SaveSettingsInternalAsync(bool silent = false)
    {
        if (_isInitializing) return;
        IsSaving = true;
        try
        {
            _options.Language = SelectedLanguageOption?.Code ?? "auto";
            _options.DocumentBaseDir = DocumentBaseDir;
            _options.GameFilesDir = GameFilesDir;
            _options.CacheMethod = CacheMethod;
            _options.AutoRebuildStaticCache = AutoRebuildStaticCache;
            _options.CompressionLevel = CompressionLevel;
            _options.EnableAutoScan = EnableAutoScan;
            _options.ObservedFolders = ObservedFolders.ToList();

            await AppSettingsService.SaveOptionsAsync(_options);

            try
            {
                var watcherService = _serviceProvider.GetService<DownloadsWatcherService>();
                watcherService?.ReloadWatchers(_options);
            }
            catch { }

            if (!silent)
            {
                ShowStatus(LocalizationManager.Instance.GetString("settings.status.saved"), isError: false);
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                ShowStatus(LocalizationManager.Instance.GetString("settings.status.error", ex.Message), isError: true);
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void ShowStatus(string message, bool isError)
    {
        StatusToastMessage = message;
        IsStatusToastError = isError;
        IsStatusToastVisible = true;
        _ = ClearStatusAfterDelayAsync();
    }

    private async Task ClearStatusAfterDelayAsync()
    {
        await Task.Delay(4000);
        IsStatusToastVisible = false;
        StatusToastMessage = string.Empty;
    }
}
