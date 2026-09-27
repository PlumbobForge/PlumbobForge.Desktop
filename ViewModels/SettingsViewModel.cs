using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

public partial class LanguageOptionItemViewModel : ObservableObject
{
    public LanguageOption Option { get; }
    public string Code => Option.Code;
    public string DisplayName => Option.DisplayName;
    public string NativeName => Option.NativeName;

    [ObservableProperty]
    private bool _isSelected;

    public LanguageOptionItemViewModel(LanguageOption option, bool isSelected)
    {
        Option = option;
        _isSelected = isSelected;
    }
}

public partial class AccentColorItemViewModel : ObservableObject
{
    public AccentOption Option { get; }
    public string Name => Option.Name;
    public string DisplayName => Option.DisplayName;
    public string Hex => Option.Hex;
    public Avalonia.Media.IBrush SwatchBrush { get; }

    [ObservableProperty]
    private bool _isSelected;

    public AccentColorItemViewModel(AccentOption option, bool isSelected)
    {
        Option = option;
        SwatchBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(option.Hex));
        _isSelected = isSelected;
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AppDbContext _db;
    private readonly PKGManager _pkgManager;
    private readonly PlumbobForgeOptions _options;
    private readonly UpdateService _updateService;

    private bool _isInitializing = false;

    #region Appearance & Theme Settings

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    private string _selectedTheme = "Dark";

    public bool IsDarkTheme => string.Equals(SelectedTheme, "Dark", StringComparison.OrdinalIgnoreCase);
    public bool IsLightTheme => string.Equals(SelectedTheme, "Light", StringComparison.OrdinalIgnoreCase);
    public bool IsSystemTheme => string.Equals(SelectedTheme, "System", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedTheme, "Auto", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private LanguageOptionItemViewModel? _selectedLanguageOption;

    public ObservableCollection<LanguageOptionItemViewModel> LanguageOptions { get; } = new();

    [RelayCommand]
    public void SelectLanguage(LanguageOptionItemViewModel? lang)
    {
        if (lang == null) return;
        SelectedLanguageOption = lang;
    }

    partial void OnSelectedLanguageOptionChanged(LanguageOptionItemViewModel? value)
    {
        if (_isInitializing || value == null) return;
        _options.Language = value.Code;
        LocalizationManager.Instance.SetLanguage(value.Code);
        foreach (var item in LanguageOptions)
        {
            item.IsSelected = (item.Code == value.Code);
        }
        _ = SaveSettingsInternalAsync(silent: true);
    }

    [ObservableProperty]
    private string _selectedAccent = "Emerald";

    public ObservableCollection<AccentColorItemViewModel> AccentOptions { get; } = new();

    [RelayCommand]
    public void SetTheme(string theme)
    {
        SelectedTheme = theme;
        _options.Theme = theme;
        ThemeService.ApplyTheme(theme);
        _ = SaveSettingsInternalAsync(silent: true);
    }

    [RelayCommand]
    public void SetAccent(string accentName)
    {
        SelectedAccent = accentName;
        _options.AccentColor = accentName;
        ThemeService.ApplyAccent(accentName);

        foreach (var item in AccentOptions)
        {
            item.IsSelected = string.Equals(item.Name, accentName, StringComparison.OrdinalIgnoreCase);
        }

        _ = SaveSettingsInternalAsync(silent: true);
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

    public string CompressionLevelText => CompressionLevel switch
    {
        0 => LocalizationManager.Instance.GetString("settings.engine.none_label"),
        1 => LocalizationManager.Instance.GetString("settings.engine.balanced_label"),
        _ => LocalizationManager.Instance.GetString("settings.engine.high_label")
    };

    public string CompressionDescription => CompressionLevel switch
    {
        0 => LocalizationManager.Instance.GetString("settings.engine.none_desc"),
        1 => LocalizationManager.Instance.GetString("settings.engine.balanced_desc"),
        _ => LocalizationManager.Instance.GetString("settings.engine.high_desc")
    };

    [ObservableProperty]
    private bool _enableAutoScan = true;

    partial void OnEnableAutoScanChanged(bool value)
    {
        if (_isInitializing) return;
        _options.EnableAutoScan = value;
        _ = SaveSettingsInternalAsync(silent: true);
    }

    partial void OnCompressionSliderValueChanged(double value)
    {
        if (_isInitializing) return;
        _options.CompressionLevel = CompressionLevel;
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

            var mainVm = _serviceProvider.GetService<MainViewModel>();
            if (mainVm != null)
            {
                await mainVm.RefreshDirtyStateAsync();
            }
        }
        catch { }
    }

    public ObservableCollection<string> ObservedFolders { get; } = new();

    [ObservableProperty]
    private int _totalSetsCount = 0;

    [ObservableProperty]
    private int _totalItemsCount = 0;

    [ObservableProperty]
    private bool _isSaving = false;

    [ObservableProperty]
    private string _statusToastMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusToastVisible = false;

    [ObservableProperty]
    private bool _isStatusToastError = false;

    #endregion

    public SettingsViewModel(IServiceProvider serviceProvider, AppDbContext db, PKGManager pkgManager, IOptions<PlumbobForgeOptions> options, UpdateService updateService)
    {
        _serviceProvider = serviceProvider;
        _db = db;
        _pkgManager = pkgManager;
        _options = options.Value ?? new PlumbobForgeOptions();
        _updateService = updateService;

        var asmVersion = typeof(SettingsViewModel).Assembly.GetName().Version;
        if (asmVersion != null)
        {
            AppVersion = $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}";
        }

        LocalizationManager.Instance.LanguageChanged += _ =>
        {
            OnPropertyChanged(nameof(CacheMethodDescription));
            OnPropertyChanged(nameof(CompressionLevelText));
            OnPropertyChanged(nameof(CompressionDescription));
        };

        LoadInitialValues();
        _ = LoadStatsAsync();
    }

    private void LoadInitialValues()
    {
        _isInitializing = true;
        try
        {
            SelectedTheme = string.IsNullOrWhiteSpace(_options.Theme) ? "Dark" : _options.Theme;
            SelectedAccent = string.IsNullOrWhiteSpace(_options.AccentColor) ? "Emerald" : _options.AccentColor;

            LanguageOptions.Clear();
            string currentLang = string.IsNullOrWhiteSpace(_options.Language) ? "auto" : _options.Language;
            foreach (var lang in LocalizationManager.SupportedLanguages)
            {
                bool isSel = lang.Code.Equals(currentLang, StringComparison.OrdinalIgnoreCase);
                LanguageOptions.Add(new LanguageOptionItemViewModel(lang, isSel));
            }

            SelectedLanguageOption = LanguageOptions.FirstOrDefault(l =>
                l.Code.Equals(currentLang, StringComparison.OrdinalIgnoreCase)) ?? LanguageOptions.FirstOrDefault();

            AccentOptions.Clear();
            foreach (var accent in ThemeService.Accents)
            {
                AccentOptions.Add(new AccentColorItemViewModel(accent, string.Equals(accent.Name, SelectedAccent, StringComparison.OrdinalIgnoreCase)));
            }

            DocumentBaseDir = _options.DocumentBaseDir ?? string.Empty;
            GameFilesDir = _options.GameFilesDir ?? string.Empty;
            CacheMethod = string.IsNullOrEmpty(_options.CacheMethod) ? "Dynamic" : _options.CacheMethod;
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
                ShowStatus("Settings saved successfully!", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to save settings: {ex.Message}", isError: true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void ShowStatus(string message, bool isError = false)
    {
        StatusToastMessage = message;
        IsStatusToastError = isError;
        IsStatusToastVisible = true;

        Task.Delay(3500).ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsStatusToastVisible = false;
            });
        });
    }
}
