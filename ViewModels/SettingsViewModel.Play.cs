using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class SettingsViewModel
{
    // The program the Play button starts; empty means Game\Bin\TS3W.exe under the game folder.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Sims3ExecutableHint))]
    private string _sims3Executable = string.Empty;

    partial void OnSims3ExecutableChanged(string value)
    {
        if (_isInitializing) return;
        _options.Sims3Executable = value;
        _ = SaveSettingsInternalAsync(silent: true);
    }

    public string Sims3ExecutableHint
    {
        get
        {
            var loc = LocalizationManager.Instance;
            string resolved = GameLaunchService.ResolveExecutable(Sims3Executable, GameFilesDir);
            if (!string.IsNullOrWhiteSpace(Sims3Executable))
            {
                return GameLaunchService.ValidateExecutable(resolved) ?? loc.GetString("settings.storage.sims3_exe_custom");
            }
            return string.IsNullOrEmpty(resolved)
                ? loc.GetString("settings.storage.sims3_exe_not_found")
                : loc.GetString("settings.storage.sims3_exe_default", resolved);
        }
    }

    [RelayCommand]
    public void ClearSims3Executable() => Sims3Executable = string.Empty;

    public async Task BrowseSims3ExecutableAsync(IStorageProvider storageProvider)
    {
        try
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationManager.Instance.GetString("settings.storage.sims3_exe_pick_title"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(LocalizationManager.Instance.GetString("settings.storage.sims3_exe_filter")) { Patterns = new[] { "*.exe" } } }
            });

            string? path = files.FirstOrDefault()?.Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(path)) Sims3Executable = path;
        }
        catch (Exception ex)
        {
            ShowStatus(LocalizationManager.Instance.GetString("settings.storage.sims3_exe_pick_error", ex.Message), isError: true);
        }
    }
}
