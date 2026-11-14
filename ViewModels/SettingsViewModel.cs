using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
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

public record BaseColorPresetItem(string Name, string Hex, IBrush Brush);

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

    public event Action<string>? RequestScrollAndHighlight;

    public void ScrollToAndHighlightSection(string sectionName)
    {
        RequestScrollAndHighlight?.Invoke(sectionName);
    }

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
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    [NotifyPropertyChangedFor(nameof(IsCustomTheme))]
    [NotifyPropertyChangedFor(nameof(IsPresetTheme))]

    private string _selectedTheme = "Dark";

    public bool IsDarkTheme => string.Equals(SelectedTheme, "Dark", StringComparison.OrdinalIgnoreCase);
    public bool IsLightTheme => string.Equals(SelectedTheme, "Light", StringComparison.OrdinalIgnoreCase);
    public bool IsSystemTheme => string.Equals(SelectedTheme, "System", StringComparison.OrdinalIgnoreCase);
    public bool IsCustomTheme => string.Equals(SelectedTheme, "Custom", StringComparison.OrdinalIgnoreCase);
    public bool IsPresetTheme => !IsCustomTheme;


    [RelayCommand]
    public void SetTheme(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme)) return;
        SelectedTheme = theme;
        if (IsCustomTheme)
        {
            _options.IsCustomTheme = true;
            _options.Theme = "Custom";
            ApplyCustomThemeNow();
        }
        else
        {
            _options.IsCustomTheme = false;
            _options.Theme = theme;

            ThemeService.ApplyTheme(theme);
            ThemeService.ApplyAccent(SelectedAccent);
        }

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

    // --- Custom Theme Generator State & Commands ---
    [ObservableProperty]
    private string _customBaseHex = "#1e293b";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomLight))]
    private bool _customIsDark = true;

    public bool IsCustomLight => !CustomIsDark;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomIntensityDisplay))]
    private double _customThemeIntensity = 45.0;

    public string CustomIntensityDisplay => $"{Math.Round(CustomThemeIntensity)}%";

    partial void OnCustomThemeIntensityChanged(double value)
    {
        if (_isInitializing) return;
        OnCustomColorChanged(immediate: false);
    }

    [ObservableProperty]
    private bool _useCustomAccent = false;

    partial void OnUseCustomAccentChanged(bool value)
    {
        if (_isInitializing) return;
        OnCustomColorChanged();
    }

    public Color CustomBaseColorValue
    {
        get => ColorPaletteGenerator.TryParseHex(CustomBaseHex, out var c) ? c : Color.Parse("#1e293b");
        set
        {
            var hex = ColorPaletteGenerator.ColorToHex(value);
            if (!string.Equals(CustomBaseHex, hex, StringComparison.OrdinalIgnoreCase))
            {
                CustomBaseHex = hex;
                OnCustomColorChanged();
                OnPropertyChanged();
            }
        }
    }

    public Color CustomAccentColorValue
    {
        get => ColorPaletteGenerator.TryParseHex(CustomAccentHex, out var c) ? c : Color.Parse("#10b981");
        set
        {
            var hex = ColorPaletteGenerator.ColorToHex(value);
            if (!string.Equals(CustomAccentHex, hex, StringComparison.OrdinalIgnoreCase))
            {
                CustomAccentHex = hex;
                OnCustomColorChanged();
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private string _customAccentHex = "#10b981";

    public ObservableCollection<BaseColorPresetItem> BaseColorPresets { get; } = new();

    // Dynamic Live Preview Brushes
    [ObservableProperty] private IBrush _customBaseBrush = new SolidColorBrush(Color.Parse("#1e293b"));
    [ObservableProperty] private IBrush _customAccentBrush = new SolidColorBrush(Color.Parse("#10b981"));
    [ObservableProperty] private IBrush _customBgDarkBrush = new SolidColorBrush(Color.Parse("#0f172a"));
    [ObservableProperty] private IBrush _customBgCardBrush = new SolidColorBrush(Color.Parse("#111827"));
    [ObservableProperty] private IBrush _customBorderBrush = new SolidColorBrush(Color.Parse("#1e293b"));
    [ObservableProperty] private IBrush _customTextMainBrush = new SolidColorBrush(Color.Parse("#f8fafc"));
    [ObservableProperty] private IBrush _customTextMutedBrush = new SolidColorBrush(Color.Parse("#94a3b8"));

    [RelayCommand]
    public void SelectPresetThemeMode() => SetCustomThemeEnabled(false);

    [RelayCommand]
    public void SelectCustomThemeMode() => SetCustomThemeEnabled(true);

    [RelayCommand]
    public void EnableCustomTheme(object? param)
    {
        bool enable = param switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            _ => false
        };
        SetCustomThemeEnabled(enable);
    }

    public void SetCustomThemeEnabled(bool enable)
    {
        SetTheme(enable ? "Custom" : (_options.Theme == "Custom" ? "Dark" : _options.Theme));
    }

    [RelayCommand]
    public void SetCustomBaseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        CustomBaseHex = hex.Trim();
        OnCustomColorChanged(immediate: true);
    }

    [RelayCommand]
    public void SelectCustomDark() => SetCustomIsDark(true);

    [RelayCommand]
    public void SelectCustomLight() => SetCustomIsDark(false);

    [RelayCommand]
    public void SetCustomThemeDark(object? param)
    {
        bool isDark = param switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) ? parsed : true,
            _ => true
        };
        SetCustomIsDark(isDark);
    }

    public void SetCustomIsDark(bool isDark)
    {
        CustomIsDark = isDark;
        PopulateBasePresets();
        OnCustomColorChanged(immediate: true);
    }

    [RelayCommand]
    public void SelectAutogenAccent() => SetUseCustomAccent(false);

    [RelayCommand]
    public void SelectSeparateAccent() => SetUseCustomAccent(true);

    [RelayCommand]
    public void ToggleUseCustomAccent(object? param)
    {
        bool useCustom = param switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            _ => false
        };
        SetUseCustomAccent(useCustom);
    }

    public void SetUseCustomAccent(bool useCustom)
    {
        UseCustomAccent = useCustom;
        OnCustomColorChanged(immediate: true);
    }

    [RelayCommand]
    public void SetCustomAccentColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        CustomAccentHex = hex.Trim();
        OnCustomColorChanged(immediate: true);
    }
    private DispatcherTimer? _customThemeDebounceTimer;

    public void OnCustomColorChanged(bool immediate = false)
    {
        OnPropertyChanged(nameof(CustomBaseColorValue));
        OnPropertyChanged(nameof(CustomAccentColorValue));
        RefreshCustomThemePalette();

        if (immediate)
        {
            _customThemeDebounceTimer?.Stop();
            CommitCustomTheme();
            return;
        }

        if (_customThemeDebounceTimer == null)
        {
            _customThemeDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _customThemeDebounceTimer.Tick += (s, e) =>
            {
                _customThemeDebounceTimer.Stop();
                CommitCustomTheme();
            };
        }
        else
        {
            _customThemeDebounceTimer.Stop();
        }

        _customThemeDebounceTimer.Start();
    }

    private void CommitCustomTheme()
    {
        if (IsCustomTheme)
        {
            ApplyCustomThemeNow();
        }
        _ = SaveSettingsInternalAsync(silent: true);
    }

    private void ApplyCustomThemeNow()
    {
        ThemeService.ApplyCustomTheme(CustomBaseHex, CustomIsDark, UseCustomAccent, CustomAccentHex, CustomThemeIntensity);
    }

    private void PopulateBasePresets()
    {
        BaseColorPresets.Clear();
        var presets = CustomIsDark
            ? new (string, string)[]
            {
                ("Slate", "#1e293b"),
                ("Navy", "#0f172a"),
                ("Obsidian", "#121212"),
                ("Midnight", "#0b132b"),
                ("Forest", "#062e24"),
                ("Purple", "#1e1035"),
                ("Espresso", "#1c1917"),
                ("Wine", "#2b0d14"),
                ("Ocean", "#0c2540")
            }
            : new (string, string)[]
            {
                ("Cloud", "#f8fafc"),
                ("Silver", "#f1f5f9"),
                ("Sand", "#faf5ee"),
                ("Lavender", "#f5f3ff"),
                ("Sage", "#f0fdf4"),
                ("Sky", "#f0f9ff"),
                ("Rose", "#fff1f2"),
                ("Amber", "#fffbeb")
            };

        foreach (var (name, hex) in presets)
        {
            BaseColorPresets.Add(new BaseColorPresetItem(name, hex, new SolidColorBrush(Color.Parse(hex))));
        }
    }

    private void RefreshCustomThemePalette()
    {
        if (!ColorPaletteGenerator.TryParseHex(CustomBaseHex, out var baseColor))
        {
            baseColor = Color.Parse(CustomIsDark ? "#1e293b" : "#f1f5f9");
        }

        Color? customAccent = null;
        if (UseCustomAccent && ColorPaletteGenerator.TryParseHex(CustomAccentHex, out var parsedAccent))
        {
            customAccent = parsedAccent;
        }

        var palette = ColorPaletteGenerator.Generate(baseColor, CustomIsDark, customAccent, CustomThemeIntensity);

        CustomBaseBrush = new SolidColorBrush(baseColor);
        CustomAccentBrush = new SolidColorBrush(palette.Accent);
        CustomBgDarkBrush = new SolidColorBrush(palette.BgDark);
        CustomBgCardBrush = new SolidColorBrush(palette.BgCard);
        CustomBorderBrush = new SolidColorBrush(palette.BorderMain);
        CustomTextMainBrush = new SolidColorBrush(palette.TextMain);
        CustomTextMutedBrush = new SolidColorBrush(palette.TextMuted);
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

    [ObservableProperty]
    private bool _enableSingleSourceVault = false;

    partial void OnAutoRebuildStaticCacheChanged(bool value)
    {
        if (_isInitializing) return;
        _options.AutoRebuildStaticCache = value;
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

    [ObservableProperty]
    private bool _autoClearGameCaches = false;

    partial void OnAutoClearGameCachesChanged(bool value)
    {
        if (_isInitializing) return;
        _options.AutoClearGameCaches = value;
        var monitor = _serviceProvider.GetService<IOptionsMonitor<PlumbobForgeOptions>>();
        if (monitor?.CurrentValue != null) monitor.CurrentValue.AutoClearGameCaches = value;
        _ = SaveSettingsInternalAsync(silent: true);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextureCapNone))]
    [NotifyPropertyChangedFor(nameof(IsTextureCap2048))]
    [NotifyPropertyChangedFor(nameof(IsTextureCap1024))]
    private int _textureMaxDimension = 2048;

    public bool IsTextureCapNone => TextureMaxDimension <= 0;
    public bool IsTextureCap2048 => TextureMaxDimension == 2048;
    public bool IsTextureCap1024 => TextureMaxDimension == 1024;

    [ObservableProperty]
    private bool _optimizeTextures = false;

    partial void OnOptimizeTexturesChanged(bool value)
    {
        if (_isInitializing) return;
        _ = ApplyTextureSettingsAsync();
    }

    [RelayCommand]
    public async Task SetTextureCapAsync(string value)
    {
        if (!int.TryParse(value, out int cap) || cap == TextureMaxDimension) return;
        TextureMaxDimension = cap;
        await ApplyTextureSettingsAsync();
    }


    private async Task ApplyTextureSettingsAsync()
    {
        _options.OptimizeTextures = OptimizeTextures;
        _options.TextureMaxDimension = TextureMaxDimension;
        var monitor = _serviceProvider.GetService<IOptionsMonitor<PlumbobForgeOptions>>();
        if (monitor?.CurrentValue != null)
        {
            monitor.CurrentValue.OptimizeTextures = OptimizeTextures;
            monitor.CurrentValue.TextureMaxDimension = TextureMaxDimension;
        }
        await SaveSettingsInternalAsync(silent: true);
        _serviceProvider.GetService<ContentManagerViewModel>()?.NotifyTextureOptimizationChanged();

        if (!EnableSingleSourceVault)
        {
            await MarkAllSetsDirtyAsync();
            return;
        }

        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel { ShowCloseAppCheckbox = false };
        modal.Start(LocalizationManager.Instance.GetString("settings.textures.applying_title"));
        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            const string stepId = "optimize_textures";
            modal.StartStep(stepId, LocalizationManager.Instance.GetString("settings.textures.applying_step"), progress: 0.0);

            int optimized = await Task.Run(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var vaultService = scope.ServiceProvider.GetRequiredService<VaultService>();
                int count = await vaultService.OptimizeTexturesAsync(onProgress: (done, total) =>
                {
                    double pct = total > 0 ? (double)done / total : 1.0;
                    modal.UpdateStep(stepId, badge: $"{done}/{total}", progress: pct);
                });
                await vaultService.RefreshTextureIndexesAsync();
                return count;
            });

            modal.CompleteStep(stepId, finalBadge: LocalizationManager.Instance.GetString("settings.textures.optimized_badge", optimized));
            modal.Finish(success: true);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Applying texture optimisation settings failed", ex, "SettingsViewModel");
            modal.Finish(success: false);
            ShowStatus(LocalizationManager.Instance.GetString("settings.status.error", ex.Message), isError: true);
        }
    }

    private bool _suppressVaultToggleHandler;

    private void SetVaultEnabledWithoutPrompt(bool value)
    {
        _suppressVaultToggleHandler = true;
        try
        {
            EnableSingleSourceVault = value;
        }
        finally
        {
            _suppressVaultToggleHandler = false;
        }
        _options.EnableSingleSourceVault = value;
        var monitor = _serviceProvider.GetService<IOptionsMonitor<PlumbobForgeOptions>>();
        if (monitor?.CurrentValue != null) monitor.CurrentValue.EnableSingleSourceVault = value;
    }

    partial void OnEnableSingleSourceVaultChanged(bool value)
    {
        if (_isInitializing || _suppressVaultToggleHandler) return;
        if (!value && (_db.VaultShards.Any() || _db.MetaEntities.Any(m => m.IsVaultManaged)))
        {
            _enableSingleSourceVault = true;
            OnPropertyChanged(nameof(EnableSingleSourceVault));
            _ = RevertVaultAsync();
            return;
        }

        // Switching on with library packages still on disk must migrate them, otherwise the game would stop loading them.
        if (value && _db.MetaEntities.Any(m => !m.IsVaultManaged && (m.FileName.EndsWith(".package") || m.FileName.EndsWith(".sims3pack"))))
        {
            _enableSingleSourceVault = false;
            OnPropertyChanged(nameof(EnableSingleSourceVault));
            _ = MigrateToVaultAsync();
            return;
        }

        _options.EnableSingleSourceVault = value;
        var monitor = _serviceProvider.GetService<IOptionsMonitor<PlumbobForgeOptions>>();
        if (monitor?.CurrentValue != null) monitor.CurrentValue.EnableSingleSourceVault = value;
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

            if (_options.IsCustomTheme || string.Equals(_options.Theme, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                SelectedTheme = "Custom";
            }
            else
            {
                SelectedTheme = string.IsNullOrWhiteSpace(_options.Theme) ? ThemeService.CurrentTheme : _options.Theme;
            }
            SelectedAccent = string.IsNullOrWhiteSpace(_options.AccentColor) ? ThemeService.CurrentAccent : _options.AccentColor;

            AccentOptions.Clear();
            foreach (var accent in ThemeService.Accents)
            {
                AccentOptions.Add(new AccentColorItemViewModel(accent, string.Equals(accent.Name, SelectedAccent, StringComparison.OrdinalIgnoreCase)));
            }


            CustomBaseHex = string.IsNullOrWhiteSpace(_options.CustomThemeBaseColor) ? "#1e293b" : _options.CustomThemeBaseColor;
            CustomIsDark = _options.CustomThemeIsDark;
            UseCustomAccent = _options.UseCustomAccent;
            CustomAccentHex = string.IsNullOrWhiteSpace(_options.CustomThemeAccentColor) ? "#10b981" : _options.CustomThemeAccentColor;
            CustomThemeIntensity = _options.CustomThemeIntensity > 0 ? _options.CustomThemeIntensity : 45.0;
            PopulateBasePresets();
            OnPropertyChanged(nameof(CustomBaseColorValue));
        OnPropertyChanged(nameof(CustomAccentColorValue));
        RefreshCustomThemePalette();

            DocumentBaseDir = _options.DocumentBaseDir ?? string.Empty;
            GameFilesDir = _options.GameFilesDir ?? string.Empty;
            Sims3UserDir = _options.Sims3UserDir ?? string.Empty;
            CacheMethod = string.IsNullOrEmpty(_options.CacheMethod) ? "Dynamic" : _options.CacheMethod;
            AutoRebuildStaticCache = _options.AutoRebuildStaticCache;
            EnableSingleSourceVault = _options.EnableSingleSourceVault;
            if (!EnableSingleSourceVault && (_db.VaultShards.Any() || _db.MetaEntities.Any(m => m.IsVaultManaged)))
            {
                EnableSingleSourceVault = true;
                _options.EnableSingleSourceVault = true;
                _ = SaveSettingsInternalAsync(silent: true);
            }
            EnableAutoScan = _options.EnableAutoScan;
            AutoClearGameCaches = _options.AutoClearGameCaches;
            OptimizeTextures = _options.OptimizeTextures;
            TextureMaxDimension = _options.TextureMaxDimension;

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
            _options.Sims3UserDir = Sims3UserDir;
            _options.CacheMethod = CacheMethod;
            _options.AutoRebuildStaticCache = AutoRebuildStaticCache;
            _options.EnableSingleSourceVault = EnableSingleSourceVault;
            _options.EnableAutoScan = EnableAutoScan;
            _options.AutoClearGameCaches = AutoClearGameCaches;
            _options.OptimizeTextures = OptimizeTextures;
            _options.TextureMaxDimension = TextureMaxDimension;
            _options.ObservedFolders = ObservedFolders.ToList();

            _options.Theme = SelectedTheme;
            _options.AccentColor = SelectedAccent;
            _options.IsCustomTheme = IsCustomTheme;
            _options.CustomThemeBaseColor = CustomBaseHex;
            _options.CustomThemeIsDark = CustomIsDark;
            _options.UseCustomAccent = UseCustomAccent;
            _options.CustomThemeAccentColor = CustomAccentHex;
            _options.CustomThemeIntensity = CustomThemeIntensity;

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
