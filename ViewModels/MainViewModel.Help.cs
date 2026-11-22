using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

// The title bar's ? button: a Help menu, or "close the Guide" while it is open.
// Users who had the old bug button get a one-time callout saying feedback moved here.
public partial class MainViewModel
{
    [ObservableProperty]
    private bool _isHelpMoveTipVisible;

    public string HelpTip => Lp(IsGuideOpen ? "nav.guide_close" : "nav.help");

    partial void OnIsGuideOpenChanged(bool value) => OnPropertyChanged(nameof(HelpTip));

    private PlumbobForgeOptions? HelpOptions => _serviceProvider.GetService<IOptions<PlumbobForgeOptions>>()?.Value;

    // Called on the first launch after an update. Fresh installs never had the bug button.
    public void ShowHelpMoveTipOnce(bool isUpdate)
    {
        var options = HelpOptions;
        if (options == null || options.HasSeenHelpMoveTip) return;
        if (isUpdate)
        {
            IsHelpMoveTipVisible = true;
            return;
        }
        options.HasSeenHelpMoveTip = true;
        _ = AppSettingsService.SaveOptionsAsync(options);
    }

    [RelayCommand]
    public void DismissHelpMoveTip()
    {
        if (!IsHelpMoveTipVisible) return;
        IsHelpMoveTipVisible = false;
        if (HelpOptions is { } options)
        {
            options.HasSeenHelpMoveTip = true;
            _ = AppSettingsService.SaveOptionsAsync(options);
        }
    }
}
