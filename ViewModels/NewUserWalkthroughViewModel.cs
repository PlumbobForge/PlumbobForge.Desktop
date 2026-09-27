using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public partial class NewUserWalkthroughViewModel : ObservableObject
{
    private readonly IOptions<PlumbobForgeOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly PKGManager _pkgManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(IsStep4))]
    [NotifyPropertyChangedFor(nameof(IsStep5))]
    [NotifyPropertyChangedFor(nameof(IsStep6))]
    private int _currentStep = 1;

    public bool CanGoBack => CurrentStep > 1;
    public bool CanGoNext => CurrentStep < 6;
    public bool IsLastStep => CurrentStep == 6;

    public bool IsStep1 => CurrentStep == 1; // Welcome
    public bool IsStep2 => CurrentStep == 2; // Library
    public bool IsStep3 => CurrentStep == 3; // Configurations
    public bool IsStep4 => CurrentStep == 4; // Tools
    public bool IsStep5 => CurrentStep == 5; // Quick Settings
    public bool IsStep6 => CurrentStep == 6; // Migration & Ready

    #region Step 5: Quick Settings Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    private string _selectedTheme = "Dark";

    public bool IsDarkTheme => string.Equals(SelectedTheme, "Dark", StringComparison.OrdinalIgnoreCase);
    public bool IsLightTheme => string.Equals(SelectedTheme, "Light", StringComparison.OrdinalIgnoreCase);
    public bool IsSystemTheme => string.Equals(SelectedTheme, "System", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _selectedAccent = "Emerald";

    public ObservableCollection<AccentColorItemViewModel> AccentOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDynamicCache))]
    [NotifyPropertyChangedFor(nameof(IsStaticCache))]
    [NotifyPropertyChangedFor(nameof(CacheMethodDescription))]
    private string _cacheMethod = "Dynamic";

    public bool IsDynamicCache => string.Equals(CacheMethod, "Dynamic", StringComparison.OrdinalIgnoreCase);
    public bool IsStaticCache => string.Equals(CacheMethod, "Static", StringComparison.OrdinalIgnoreCase);

    public string CacheMethodDescription => IsDynamicCache
        ? "Dynamic Cache: Builds separate caches per set. Faster rebuild times, hot-swappable configurations, and flexible set updates."
        : "Static Bundle: Merges all active sets into a single bundle to maximize in-game loading performance. Any change requires a full rebuild.";

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
        0 => "None",
        1 => "Balanced - Recommended",
        _ => "High"
    };

    public string CompressionDescription => CompressionLevel switch
    {
        0 => "No DBPF compression applied. Cache rebuilds are instant, but package file size is larger.",
        1 => "Optimal balance between compression ratio and package build speed. Recommended for most setups.",
        _ => "Maximum compression ratio to reduce disk footprint. Slower cache rebuild times."
    };

    partial void OnCompressionSliderValueChanged(double value)
    {
        int level = CompressionLevel;
        _options.Value.CompressionLevel = level;
        _ = AppSettingsService.SaveOptionsAsync(_options.Value);
    }

    [RelayCommand]
    public void SetTheme(string theme)
    {
        SelectedTheme = theme;
        _options.Value.Theme = theme;
        ThemeService.ApplyTheme(theme);
        _ = AppSettingsService.SaveOptionsAsync(_options.Value);
    }

    [RelayCommand]
    public void SetAccent(string accentName)
    {
        SelectedAccent = accentName;
        _options.Value.AccentColor = accentName;
        ThemeService.ApplyAccent(accentName);

        foreach (var item in AccentOptions)
        {
            item.IsSelected = string.Equals(item.Name, accentName, StringComparison.OrdinalIgnoreCase);
        }

        _ = AppSettingsService.SaveOptionsAsync(_options.Value);
    }

    [RelayCommand]
    public void SetCacheMethod(string method)
    {
        CacheMethod = method;
        _options.Value.CacheMethod = method;
        _ = AppSettingsService.SaveOptionsAsync(_options.Value);
    }

    [RelayCommand]
    public void SetDynamicCache() => SetCacheMethod("Dynamic");

    [RelayCommand]
    public void SetStaticCache() => SetCacheMethod("Static");

    #endregion

    #region Step 6: Migration State

    [ObservableProperty]
    private bool _hasRunMigration = false;

    [ObservableProperty]
    private string _migrationResultText = string.Empty;

    #endregion

    public event Action<bool>? RequestClose;

    public NewUserWalkthroughViewModel(IOptions<PlumbobForgeOptions> options, IServiceProvider serviceProvider, PKGManager pkgManager)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _pkgManager = pkgManager;

        // Initialize Settings from existing options
        SelectedTheme = string.IsNullOrWhiteSpace(_options.Value.Theme) ? "Dark" : _options.Value.Theme;
        SelectedAccent = string.IsNullOrWhiteSpace(_options.Value.AccentColor) ? "Emerald" : _options.Value.AccentColor;
        CacheMethod = string.IsNullOrWhiteSpace(_options.Value.CacheMethod) ? "Dynamic" : _options.Value.CacheMethod;
        CompressionLevel = _options.Value.CompressionLevel;

        AccentOptions.Clear();
        foreach (var accent in ThemeService.Accents)
        {
            AccentOptions.Add(new AccentColorItemViewModel(accent, string.Equals(accent.Name, SelectedAccent, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [RelayCommand]
    public void NextStep()
    {
        if (CurrentStep < 6)
        {
            CurrentStep++;
        }
    }

    [RelayCommand]
    public void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    public async Task OpenMigrationToolAsync()
    {
        var result = await DialogHelper.ShowMigrationAsync();
        if (result != null && result.Confirmed)
        {
            try
            {
                MigrationResult migrationRes;
                if (result.Source == MigrationSource.S3mo)
                {
                    migrationRes = await _pkgManager.MigrateS3moAsync(result.CustomPath ?? string.Empty, result.IsFullMigration);
                    if (result.IsFullMigration)
                    {
                        MigrationResultText = $"Successfully imported {migrationRes.PackagesMigrated} items, {migrationRes.SetsMigrated} sets, and {migrationRes.ConfigurationsMigrated} configurations from s3mo!";
                    }
                    else
                    {
                        MigrationResultText = $"Successfully imported {migrationRes.PackagesMigrated} items from s3mo!";
                    }
                }
                else
                {
                    migrationRes = await _pkgManager.MigrateCcMagicAsync(result.IsFullMigration, result.CustomPath);
                    if (result.IsFullMigration)
                    {
                        string collMsg = migrationRes.CollectionsMigrated > 0 ? $", and {migrationRes.CollectionsMigrated} collections" : string.Empty;
                        MigrationResultText = $"Successfully imported {migrationRes.PackagesMigrated} items, {migrationRes.SetsMigrated} sets, {migrationRes.ConfigurationsMigrated} configurations{collMsg} from CC Magic!";
                    }
                    else
                    {
                        MigrationResultText = $"Successfully imported {migrationRes.PackagesMigrated} items from CC Magic!";
                    }
                }

                HasRunMigration = true;

                // Trigger data refresh if ContentManagerViewModel / SettingsViewModel is loaded
                var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
                if (contentVm != null) await contentVm.LoadDataAsync();

                var configVm = _serviceProvider.GetService<ConfigurationsViewModel>();
                if (configVm != null) await configVm.LoadDataAsync();

                var healthVm = _serviceProvider.GetService<HealthViewModel>();
                if (healthVm != null) await healthVm.LoadCollectionsAsync();

                var mainVm = _serviceProvider.GetService<MainViewModel>();
                if (mainVm != null) await mainVm.RefreshDirtyStateAsync();
            }
            catch (Exception ex)
            {
                HasRunMigration = true;
                string baseMsg = "Something failed, try restarting the app. If it continues to fail, please report it.";
                MigrationResultText = string.IsNullOrWhiteSpace(ex.Message)
                    ? baseMsg
                    : $"{baseMsg} ({ex.Message})";
            }
        }
    }

    [RelayCommand]
    public async Task FinishAsync()
    {
        _options.Value.HasSeenWalkthrough = true;
        _options.Value.HasCompletedUpgradeWizard = true;
        await AppSettingsService.SaveOptionsAsync(_options.Value);
        RequestClose?.Invoke(true);
    }
}
