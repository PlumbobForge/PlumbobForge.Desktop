using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

// The title bar's optional Quick Actions button (off by default, switched on in Settings > Appearance):
// one dropdown with "Clear standard caches" and "Refresh application".
public partial class MainViewModel
{
    [ObservableProperty]
    private bool _showQuickActions = TitleBarPreferences.ShowQuickActions;

    private bool _quickActionsHooked;

    // Follows the Settings switch while the app runs.
    private void HookQuickActionsPreference()
    {
        if (_quickActionsHooked) return;
        _quickActionsHooked = true;
        TitleBarPreferences.Changed += () => ShowQuickActions = TitleBarPreferences.ShowQuickActions;
    }

    // Clears the Cache Cleaner's "Core Game Caches" group (the five standard cache files).
    [RelayCommand]
    public async Task ClearStandardCachesAsync()
    {
        if (IsGameRunning || Sims3GameProcess.IsGameRunning())
        {
            ActionToast.Show(Lp("tools.cache.game_running"), isError: true);
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var health = scope.ServiceProvider.GetRequiredService<Sims3HealthService>();
            var result = await health.ClearCachesAsync(new[] { "core_caches" });

            if (result.Errors.Count > 0)
                ActionToast.Show(Lp("tools.cache.standard_failed", result.Errors.Count, result.Errors[0]), isError: true);
            else if (result.FilesDeleted > 0)
                ActionToast.Show(Lp("tools.cache.standard_cleared", result.FilesDeleted, result.FormattedBytesReclaimed));
            else
                ActionToast.Show(Lp("tools.cache.standard_none"));
        }
        catch (Exception ex)
        {
            ActionToast.Show(Lp("tools.cache.standard_failed", 1, ex.Message), isError: true);
        }

        // Keep the Cache Cleaner's sizes in step with what was just deleted.
        await HealthVm.ScanCachesAsync();
    }

    // Reloads what the app shows from the database and disk. It rebuilds nothing.
    [RelayCommand]
    public async Task RefreshApplicationAsync()
    {
        if (ActiveProgressModal?.IsRunning == true) return;

        await ContentManagerVm.LoadDataAsync();
        await ConfigurationsVm.LoadDataAsync();
        if (IsToolsActive && HealthVm.IsCacheCleanerActive) await HealthVm.ScanCachesAsync();
        await RefreshDirtyStateAsync();
    }
}
