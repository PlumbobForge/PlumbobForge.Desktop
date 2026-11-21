using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigPickerText))]
    private string _activeConfigurationName = string.Empty;

    // Shown on the configuration picker beside Play.
    public string ConfigPickerText =>
        string.IsNullOrEmpty(ActiveConfigurationName)
            ? LocalizationManager.Instance.GetString("play.choose_configuration")
            : ActiveConfigurationName;

    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    // Picking from the list beside Play only activates the configuration; the game starts from Play.
    public async Task SelectConfigurationAsync(Window owner, long configurationId)
    {
        if (ActiveProgressModal?.IsRunning == true) return;

        await ConfigurationsVm.LoadDataAsync();
        var config = ConfigurationsVm.Configurations.FirstOrDefault(c => c.Id == configurationId);
        if (config == null)
        {
            await DialogHelper.ShowErrorAsync(L("play.title"), L("play.config_missing"), owner);
            return;
        }

        // Activation reports its own errors as a toast.
        await ConfigurationsVm.ActivateConfigurationAsync(config);
        await RefreshDirtyStateAsync();
    }

    // Starts The Sims 3 with the given configuration, offering a rebuild first when sets have unbuilt changes.
    public async Task PlayAsync(Window owner, long configurationId)
    {
        if (ActiveProgressModal?.IsRunning == true) return;

        if (Sims3GameProcess.IsGameRunning())
        {
            await DialogHelper.ShowErrorAsync(L("play.title"), L("play.game_running"), owner);
            return;
        }

        var options = _serviceProvider.GetRequiredService<IOptionsMonitor<PlumbobForgeOptions>>().CurrentValue;
        string executable = GameLaunchService.ResolveExecutable(options.Sims3Executable, options.GameFilesDir);
        string? problem = GameLaunchService.ValidateExecutable(executable);
        if (problem != null)
        {
            NavigateToSettingsSection("Storage");
            await DialogHelper.ShowErrorAsync(L("play.title"), problem, owner);
            return;
        }

        await ConfigurationsVm.LoadDataAsync();
        var config = ConfigurationsVm.Configurations.FirstOrDefault(c => c.Id == configurationId);
        if (config == null)
        {
            await DialogHelper.ShowErrorAsync(L("play.title"), L("play.config_missing"), owner);
            return;
        }

        // Activation reports its own errors as a toast and leaves the old configuration active.
        await ConfigurationsVm.ActivateConfigurationAsync(config);
        if (!config.Active)
        {
            await DialogHelper.ShowErrorAsync(L("play.title"), L("play.not_activated", config.Name), owner);
            return;
        }

        await RefreshDirtyStateAsync();
        if (HasDirtySets)
        {
            var choice = await new PlayOptionsWindow(config.Name).ShowDialog<PlayOptions?>(owner);
            if (choice == null) return;

            if (choice.Rebuild)
            {
                bool rebuilt = await ContentManagerVm.RebuildForPlayAsync();
                await RefreshDirtyStateAsync();
                if (!rebuilt)
                {
                    await DialogHelper.ShowErrorAsync(L("play.title"), L("play.rebuild_failed"), owner);
                    return;
                }
            }
        }

        try
        {
            GameLaunchService.Launch(executable);
        }
        catch (Exception ex)
        {
            await DialogHelper.ShowErrorAsync(L("play.title"), ex.Message, owner);
        }
    }
}
