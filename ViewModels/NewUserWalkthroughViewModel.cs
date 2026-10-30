using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconPacks.Avalonia.BoxIcons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public partial class NewUserWalkthroughViewModel : ObservableObject
{
    public const int WelcomeStep = 0;
    public const int LibraryStep = 1;
    public const int ConfigurationsStep = 2;
    public const int ToolsStep = 3;
    public const int ModeStep = 4;
    public const int ImportStep = 5;
    public const int PersonalizeStep = 6;
    public const int ReadyStep = 7;

    private readonly IOptions<PlumbobForgeOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly PKGManager _pkgManager;


    public SettingsViewModel Settings { get; }

    public ObservableCollection<WalkthroughStepItemViewModel> Steps { get; } = new();
    public ObservableCollection<WalkthroughLanguageOptionViewModel> LanguageChoices { get; } = new();

    public Window? DialogOwner { get; set; }

    public event Action<bool>? RequestClose;

    [ObservableProperty]
    private int _stepIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDynamicMode))]
    [NotifyPropertyChangedFor(nameof(IsStaticMode))]
    [NotifyPropertyChangedFor(nameof(IsVaultMode))]
    [NotifyPropertyChangedFor(nameof(ModeSummary))]
    private string _selectedMode = "Dynamic";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigate))]
    [NotifyPropertyChangedFor(nameof(ShowImportChoices))]
    private bool _isImporting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowImportChoices))]
    [NotifyPropertyChangedFor(nameof(ImportSummary))]
    private bool _hasImported;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportSummary))]
    private bool _importFailed;

    [ObservableProperty]
    private string _importProgressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportSummary))]
    private string _migrationResultText = string.Empty;

    public bool IsFirstStep => StepIndex == WelcomeStep;
    public bool IsLastStep => StepIndex == ReadyStep;
    public bool CanGoBack => StepIndex > WelcomeStep;
    public bool CanSkipTour => StepIndex < ModeStep;
    public bool CanNavigate => !IsImporting;
    public string NextButtonText => LocalizationManager.Instance.GetString(IsFirstStep ? "dialogs.walkthrough.lets_go_btn" : "dialogs.walkthrough.next_btn");

    public bool IsDynamicMode => SelectedMode == "Dynamic";
    public bool IsStaticMode => SelectedMode == "Static";
    public bool IsVaultMode => SelectedMode == "Vault";

    public bool ShowImportChoices => !IsImporting && !HasImported;

    public string ModeSummary => LocalizationManager.Instance.GetString(SelectedMode switch
    {
        "Static" => "dialogs.walkthrough.mode_static_title",
        "Vault" => "dialogs.walkthrough.mode_vault_title",
        _ => "dialogs.walkthrough.mode_dynamic_title"
    });

    public string ImportSummary
    {
        get
        {
            if (HasImported && !ImportFailed) return MigrationResultText;
            if (ImportFailed) return LocalizationManager.Instance.GetString("dialogs.walkthrough.summary_import_failed");
            return LocalizationManager.Instance.GetString("dialogs.walkthrough.summary_fresh");
        }
    }

    public string LookSummary
    {
        get
        {
            string theme = LocalizationManager.Instance.GetString(Settings.SelectedTheme switch
            {
                "Light" => "dialogs.walkthrough.theme_light",
                "System" => "dialogs.walkthrough.theme_system",
                _ => "dialogs.walkthrough.theme_dark"
            });
            string accent = Settings.AccentOptions
                .FirstOrDefault(a => string.Equals(a.Name, Settings.SelectedAccent, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? Settings.SelectedAccent;
            return $"{theme} · {accent}";
        }
    }

    public NewUserWalkthroughViewModel(IOptions<PlumbobForgeOptions> options, IServiceProvider serviceProvider, PKGManager pkgManager, SettingsViewModel settings)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _pkgManager = pkgManager;
        Settings = settings;

        SelectedMode = _options.Value.EnableSingleSourceVault
            ? "Vault"
            : string.Equals(_options.Value.CacheMethod, "Static", StringComparison.OrdinalIgnoreCase) ? "Static" : "Dynamic";

        AddStep("dialogs.walkthrough.tab_welcome", PackIconBoxIconsKind.RegularHome);
        AddStep("dialogs.walkthrough.tab_library", PackIconBoxIconsKind.RegularFolder);
        AddStep("dialogs.walkthrough.tab_configs", PackIconBoxIconsKind.RegularSliderAlt);
        AddStep("dialogs.walkthrough.tab_tools", PackIconBoxIconsKind.RegularSpanner);
        AddStep("dialogs.walkthrough.tab_mode", PackIconBoxIconsKind.RegularDatabase);
        AddStep("dialogs.walkthrough.tab_import", PackIconBoxIconsKind.RegularArchive);
        AddStep("dialogs.walkthrough.tab_personalize", PackIconBoxIconsKind.RegularPalette);
        AddStep("dialogs.walkthrough.tab_ready", PackIconBoxIconsKind.RegularRocket);

        string currentLanguage = LocalizationManager.Instance.CurrentLanguage;
        foreach (var language in Settings.LanguageOptions)
        {
            LanguageChoices.Add(new WalkthroughLanguageOptionViewModel(language, string.Equals(language.Code, currentLanguage, StringComparison.OrdinalIgnoreCase)));
        }

        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        UpdateSteps();
    }

    private void AddStep(string titleKey, PackIconBoxIconsKind icon)
    {
        Steps.Add(new WalkthroughStepItemViewModel(Steps.Count, titleKey, icon));
    }

    public void Detach()
    {
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void OnLanguageChanged(string _)
    {
        foreach (var step in Steps) step.RefreshTitle();
        foreach (var choice in LanguageChoices) choice.RefreshLabel();
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(ModeSummary));
        OnPropertyChanged(nameof(ImportSummary));
        OnPropertyChanged(nameof(LookSummary));
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.SelectedTheme) or nameof(SettingsViewModel.SelectedAccent))
        {
            OnPropertyChanged(nameof(LookSummary));
        }
    }

    partial void OnStepIndexChanged(int value)
    {
        UpdateSteps();
        foreach (var name in new[]
        {
            nameof(IsFirstStep), nameof(IsLastStep), nameof(CanGoBack), nameof(CanSkipTour), nameof(NextButtonText)
        })
        {
            OnPropertyChanged(name);
        }
    }

    private int _furthestStep;

    private void UpdateSteps()
    {
        _furthestStep = Math.Max(_furthestStep, StepIndex);
        foreach (var step in Steps)
        {
            step.IsActive = step.Index == StepIndex;
            step.IsDone = step.Index <= _furthestStep && step.Index != StepIndex;
            step.IsReachable = step.Index <= _furthestStep;
        }
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CanNavigate && StepIndex < ReadyStep) StepIndex++;
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CanNavigate && StepIndex > WelcomeStep) StepIndex--;
    }

    [RelayCommand]
    private void GoToStep(WalkthroughStepItemViewModel? step)
    {
        if (step == null || !CanNavigate || !step.IsReachable) return;
        StepIndex = step.Index;
    }

    [RelayCommand]
    private void SkipTour()
    {
        if (CanNavigate) StepIndex = ModeStep;
    }

    [RelayCommand]
    private void SelectLanguage(WalkthroughLanguageOptionViewModel? choice)
    {
        if (choice == null) return;
        foreach (var option in LanguageChoices)
        {
            option.IsSelected = ReferenceEquals(option, choice);
        }
        Settings.SelectLanguage(choice.Language);
    }

    [RelayCommand]
    private void SelectMode(string mode) => SelectedMode = mode;

    [RelayCommand]
    private Task ImportFromCcMagicAsync() => ImportAsync(MigrationSource.CcMagic);

    [RelayCommand]
    private Task ImportFromS3moAsync() => ImportAsync(MigrationSource.S3mo);

    private async Task ImportAsync(MigrationSource source)
    {
        var result = await DialogHelper.ShowMigrationAsync(owner: DialogOwner, preselect: source);
        if (result == null || !result.Confirmed) return;

        ImportFailed = false;
        ImportProgressText = LocalizationManager.Instance.GetString("dialogs.walkthrough.import_starting");
        IsImporting = true;
        void OnProgress(string message) => Dispatcher.UIThread.Post(() => ImportProgressText = message);

        try
        {
            if (result.Source == MigrationSource.S3mo)
            {
                var s3moResult = await _pkgManager.MigrateS3moAsync(result.CustomPath ?? string.Empty, result.IsFullMigration, OnProgress);
                MigrationResultText = result.IsFullMigration
                    ? LocalizationManager.Instance.GetString("dialogs.walkthrough.import_result_full_s3mo", s3moResult.PackagesMigrated, s3moResult.SetsMigrated, s3moResult.ConfigurationsMigrated)
                    : LocalizationManager.Instance.GetString("dialogs.walkthrough.import_result_simple_s3mo", s3moResult.PackagesMigrated);
            }
            else
            {
                var ccResult = await _pkgManager.MigrateCcMagicAsync(result.IsFullMigration, result.CustomPath, OnProgress);
                MigrationResultText = result.IsFullMigration
                    ? LocalizationManager.Instance.GetString("dialogs.walkthrough.import_result_full_ccmagic", ccResult.PackagesMigrated, ccResult.SetsMigrated, ccResult.ConfigurationsMigrated, ccResult.CollectionsMigrated)
                    : LocalizationManager.Instance.GetString("dialogs.walkthrough.import_result_simple_ccmagic", ccResult.PackagesMigrated);
            }
            HasImported = true;

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
            AppLogger.LogError("Migration from the walkthrough failed", ex, "NewUserWalkthrough");
            ImportFailed = true;
            string baseMsg = LocalizationManager.Instance.GetString("dialogs.walkthrough.import_failed");
            MigrationResultText = string.IsNullOrWhiteSpace(ex.Message) ? baseMsg : $"{baseMsg} ({ex.Message})";
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private void RetryImport()
    {
        HasImported = false;
        ImportFailed = false;
        MigrationResultText = string.Empty;
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        _options.Value.HasSeenWalkthrough = true;
        _options.Value.HasCompletedUpgradeWizard = true;
        _options.Value.LastOptimizedCacheMilestone = MainViewModel.CurrentCacheOptimizationMilestone;

        if (IsStaticMode) await Settings.SetStaticCacheAsync();
        else if (IsDynamicMode) await Settings.SetDynamicCacheAsync();

        await AppSettingsService.SaveOptionsAsync(_options.Value);
        Detach();
        RequestClose?.Invoke(true);

        // After the walkthrough has closed: with packages already in the library (an import just now) this asks
        // how to back them up and migrates them, otherwise it only switches the mode on.
        if (IsVaultMode && !Settings.EnableSingleSourceVault)
        {
            Settings.EnableSingleSourceVault = true;
        }
    }
}

public partial class WalkthroughStepItemViewModel : ObservableObject
{
    public int Index { get; }
    public string Number => (Index + 1).ToString();
    public string TitleKey { get; }
    public PackIconBoxIconsKind Icon { get; }
    public string Title => LocalizationManager.Instance.GetString(TitleKey);

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    private bool _isDone;

    [ObservableProperty]
    private bool _isReachable;

    public bool IsPending => !IsDone;

    public WalkthroughStepItemViewModel(int index, string titleKey, PackIconBoxIconsKind icon)
    {
        Index = index;
        TitleKey = titleKey;
        Icon = icon;
    }

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

public partial class WalkthroughLanguageOptionViewModel : ObservableObject
{
    public LanguageOptionItemViewModel Language { get; }
    public string Label => Language.Code == "auto"
        ? LocalizationManager.Instance.GetString("dialogs.walkthrough.language_auto")
        : Language.NativeName;

    [ObservableProperty]
    private bool _isSelected;

    public WalkthroughLanguageOptionViewModel(LanguageOptionItemViewModel language, bool isSelected)
    {
        Language = language;
        IsSelected = isSelected;
    }

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}
