using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class UpgradeWizardViewModel : ObservableObject
{
    private readonly PKGManager _pkgManager;
    private readonly IOptions<PlumbobForgeOptions> _options;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    private int _currentStep = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private bool _isBusy = false;

    public bool CanGoBack => CurrentStep > 1 && !IsBusy;
    public bool CanGoNext => CurrentStep < 3 && !IsBusy;
    public bool IsLastStep => CurrentStep == 3;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;

    public event Action<bool>? RequestClose;

    public UpgradeWizardViewModel(PKGManager pkgManager, IOptions<PlumbobForgeOptions> options)
    {
        _pkgManager = pkgManager;
        _options = options;
    }

    private async Task AutoCleanThumbnailsAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                string baseDir = _options.Value.DocumentBaseDir;
                if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
                {
                    string thumbDir = Path.Combine(baseDir, "Thumbnails");
                    if (Directory.Exists(thumbDir))
                    {
                        var files = Directory.GetFiles(thumbDir);
                        foreach (var f in files)
                        {
                            try
                            {
                                File.Delete(f);
                            }
                            catch { }
                        }
                    }
                }
            });
        }
        catch { }
    }

    [RelayCommand]
    public void NextStep()
    {
        if (CurrentStep < 3)
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
    public async Task FinishAsync()
    {
        _options.Value.HasCompletedUpgradeWizard = true;
        await AutoCleanThumbnailsAsync();
        await AppSettingsService.SaveOptionsAsync(_options.Value);
        RequestClose?.Invoke(true);
    }
}
