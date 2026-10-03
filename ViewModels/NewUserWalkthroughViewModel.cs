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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private string _selectedAccent = "Emerald";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDynamicCache))]
    [NotifyPropertyChangedFor(nameof(IsStaticCache))]
    [NotifyPropertyChangedFor(nameof(CacheMethodDescription))]
    private string _cacheMethod = "Dynamic";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressionLevelText))]
    [NotifyPropertyChangedFor(nameof(CompressionDescription))]
    private double _compressionSliderValue = 1.0;

    [ObservableProperty]
    private bool _hasRunMigration;

    [ObservableProperty]
    private string _migrationResultText = string.Empty;

    public bool CanGoBack => CurrentStep > 1;
    public bool CanGoNext => CurrentStep < 6;
    public bool IsLastStep => CurrentStep == 6;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;
    public bool IsStep5 => CurrentStep == 5;
    public bool IsStep6 => CurrentStep == 6;

    public bool IsDarkTheme => string.Equals(SelectedTheme, "Dark", StringComparison.OrdinalIgnoreCase);
    public bool IsLightTheme => string.Equals(SelectedTheme, "Light", StringComparison.OrdinalIgnoreCase);
    public bool IsSystemTheme => string.Equals(SelectedTheme, "System", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<AccentColorItemViewModel> AccentOptions { get; } = new();

    public bool IsDynamicCache => string.Equals(CacheMethod, "Dynamic", StringComparison.OrdinalIgnoreCase);
    public bool IsStaticCache => string.Equals(CacheMethod, "Static", StringComparison.OrdinalIgnoreCase);

    public string CacheMethodDescription => IsDynamicCache
        ? "Dynamic Cache: Builds separate caches per set. Faster rebuild times, hot-swappable configurations, and flexible set updates."
        : "Static Bundle: Merges all active sets into a single bundle to maximize in-game loading performance. Any change requires a full rebuild.";

    public int CompressionLevel
    {
        get => (int)Math.Round(CompressionSliderValue) switch
        {
            0 => 0,
            1 => 1,
            _ => 3
        };
        set => CompressionSliderValue = value switch
        {
            0 => 0.0,
            1 => 1.0,
            _ => 2.0
        };
    }

    public string CompressionLevelText => CompressionLevel switch
    {
        0 => "None (Fastest)",
        1 => "Balanced (Default)",
        _ => "Maximum (Slowest)"
    };

    public string CompressionDescription => CompressionLevel switch
    {
        0 => "No compression. Cache builds significantly faster and reduces in-game stutter, but files will take more disk space.",
        1 => "Standard compression. Good balance between build speed and disk space.",
        _ => "High compression. Saves disk space, but cache rebuilds take longer."
    };

    public event Action<bool>? RequestClose;

    public NewUserWalkthroughViewModel(IOptions<PlumbobForgeOptions> options, IServiceProvider serviceProvider, PKGManager pkgManager)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _pkgManager = pkgManager;

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
        foreach (var option in AccentOptions)
        {
            option.IsSelected = string.Equals(option.Name, accentName, StringComparison.OrdinalIgnoreCase);
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

    partial void OnCompressionSliderValueChanged(double value)
    {
        _options.Value.CompressionLevel = CompressionLevel;
        _ = AppSettingsService.SaveOptionsAsync(_options.Value);
    }

    [RelayCommand]
    public void NextStep()
    {
        if (CurrentStep < 6) CurrentStep++;
    }

    [RelayCommand]
    public void PreviousStep()
    {
        if (CurrentStep > 1) CurrentStep--;
    }

    [RelayCommand]
    public async Task OpenMigrationToolAsync()
    {
        var result = await DialogHelper.ShowMigrationAsync();
        if (result == null || !result.Confirmed) return;

        try
        {
            if (result.Source == MigrationSource.S3mo)
            {
                var s3moResult = await _pkgManager.MigrateS3moAsync(result.CustomPath ?? string.Empty, result.IsFullMigration);
                MigrationResultText = result.IsFullMigration
                    ? $"Successfully imported {s3moResult.PackagesMigrated} items, {s3moResult.SetsMigrated} sets, and {s3moResult.ConfigurationsMigrated} configurations from s3mo!"
                    : $"Successfully imported {s3moResult.PackagesMigrated} items from s3mo!";
            }
            else
            {
                var ccResult = await _pkgManager.MigrateCcMagicAsync(result.IsFullMigration, result.CustomPath);
                string colText = ccResult.CollectionsMigrated > 0 ? $", and {ccResult.CollectionsMigrated} collections" : string.Empty;
                MigrationResultText = result.IsFullMigration
                    ? $"Successfully imported {ccResult.PackagesMigrated} items, {ccResult.SetsMigrated} sets, {ccResult.ConfigurationsMigrated} configurations{colText} from CC Magic!"
                    : $"Successfully imported {ccResult.PackagesMigrated} items from CC Magic!";
            }
            HasRunMigration = true;

            var cm = _serviceProvider.GetService<ContentManagerViewModel>();
            if (cm != null) await cm.LoadDataAsync();
            var config = _serviceProvider.GetService<ConfigurationsViewModel>();
            if (config != null) await config.LoadDataAsync();
            var health = _serviceProvider.GetService<HealthViewModel>();
            if (health != null) await health.LoadCollectionsAsync();
            var main = _serviceProvider.GetService<MainViewModel>();
            if (main != null) await main.RefreshDirtyStateAsync();
        }
        catch (Exception ex)
        {
            HasRunMigration = true;
            string baseMsg = "Something failed, try restarting the app. If it continues to fail, please report it.";
            MigrationResultText = string.IsNullOrWhiteSpace(ex.Message) ? baseMsg : $"{baseMsg} ({ex.Message})";
        }
    }

    [RelayCommand]
    public async Task FinishAsync()
    {
        _options.Value.HasSeenWalkthrough = true;
        _options.Value.HasCompletedUpgradeWizard = true;
        _options.Value.LastOptimizedCacheMilestone = MainViewModel.CurrentCacheOptimizationMilestone;
        await AppSettingsService.SaveOptionsAsync(_options.Value);
        RequestClose?.Invoke(true);
    }
}
