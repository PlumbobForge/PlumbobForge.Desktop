using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

// The title bar's Play button: starts the game (rebuilding first when changes wait) and switches the active configuration.
public partial class MainViewModel
{
    private static string Lp(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    private DispatcherTimer? _gameWatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayText), nameof(PlayTip), nameof(CanPlay), nameof(ShowPlayIcon))]
    private bool _isGameRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayText), nameof(PlayTip), nameof(CanPlay), nameof(ShowPlayIcon))]
    private bool _isStartingGame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayText), nameof(PlayTip), nameof(CanPlay), nameof(ShowPlayIcon))]
    private bool _isVaultUpdating;

    [ObservableProperty]
    private string _activeConfigName = string.Empty;

    // Rebuild & Play only in Library mode: the vault never rebuilds.
    public bool NeedsRebuildToPlay => HasDirtySets && !IsVaultActive;
    public bool CanPlay => !IsGameRunning && !IsStartingGame && !IsVaultUpdating;
    public bool ShowPlayIcon => !IsStartingGame && !IsVaultUpdating;

    public string PlayText =>
        IsGameRunning ? Lp("nav.play_running")
        : IsStartingGame ? Lp("nav.play_starting")
        : IsVaultUpdating ? Lp("nav.play_vault_updating")
        : NeedsRebuildToPlay ? Lp("nav.play_rebuild_and_play")
        : Lp("nav.play_play");

    public string PlayTip =>
        IsGameRunning ? Lp("nav.play_running_tip")
        : IsVaultUpdating ? Lp("progress.vault_status_updating")
        : NeedsRebuildToPlay ? Lp("nav.play_rebuild_and_play_tip")
        : Lp("nav.play_play_tip");

    public string ConfigTip => Lp("nav.play_config_tip", ActiveConfigName);

    partial void OnHasDirtySetsChanged(bool value) => RaisePlayState();
    partial void OnIsVaultActiveChanged(bool value) => RaisePlayState();
    partial void OnActiveConfigNameChanged(string value) => OnPropertyChanged(nameof(ConfigTip));

    private void RaisePlayState()
    {
        OnPropertyChanged(nameof(NeedsRebuildToPlay));
        OnPropertyChanged(nameof(PlayText));
        OnPropertyChanged(nameof(PlayTip));
    }

    // Polled, since the game can be closed or started outside the app.
    private void StartGameWatch()
    {
        if (_gameWatch != null) return;
        _gameWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _gameWatch.Tick += async (_, _) => await RefreshGameRunningAsync();
        _gameWatch.Start();
        _ = RefreshGameRunningAsync();
    }

    public async Task RefreshGameRunningAsync()
    {
        bool running;
        try { running = await Task.Run(Sims3GameProcess.IsGameRunning); }
        catch (Exception) { return; }
        if (!IsStartingGame) IsGameRunning = running;
    }

    private async Task RefreshActiveConfigNameAsync(AppDbContext db)
    {
        var name = await db.ConfigEntities.AsNoTracking().Where(c => c.Active).Select(c => c.Name).FirstOrDefaultAsync();
        await Dispatcher.UIThread.InvokeAsync(() => ActiveConfigName = name ?? string.Empty);
    }

    [RelayCommand]
    public async Task PlayAsync()
    {
        if (!CanPlay) return;
        // A delete waiting behind Undo has to land before the game reads the Mods folder.
        await ActionToast.CommitPendingAsync();
        if (Sims3GameProcess.IsGameRunning())
        {
            IsGameRunning = true;
            ActionToast.Show(Lp("nav.play_already_running"));
            return;
        }

        // The same instance Settings edits, so a folder just picked there counts straight away.
        var options = _serviceProvider.GetService<IOptions<PlumbobForgeOptions>>()?.Value;
        string? exe = GameLauncher.FindGameExe(options?.GameFilesDir);
        if (exe == null)
        {
            ActionToast.Show(Lp("nav.play_not_found"), isError: true);
            return;
        }

        IsStartingGame = true;
        try
        {
            // Read again: the flag is only refreshed after some changes, and a stale "nothing to rebuild" would start
            // the game with outdated CC.
            await RefreshDirtyStateAsync();
            if (NeedsRebuildToPlay)
            {
                await RebuildCacheAsync();
                // Rebuild stopped or failed: the game would load outdated CC.
                if (NeedsRebuildToPlay) return;
            }

            if (!GameLauncher.Start(exe))
            {
                ActionToast.Show(Lp("nav.play_start_failed"), isError: true);
                return;
            }
            IsGameRunning = true;
        }
        finally
        {
            IsStartingGame = false;
        }
    }

    // Configurations for the Play menu, loaded the first time the menu opens.
    public async Task<IReadOnlyList<ConfigItemViewModel>> PlayMenuConfigsAsync()
    {
        if (ConfigurationsVm.Configurations.Count == 0) await ConfigurationsVm.LoadDataAsync();
        return ConfigurationsVm.Configurations.ToList();
    }

    public async Task ActivateFromPlayMenuAsync(ConfigItemViewModel config)
    {
        await ConfigurationsVm.ActivateConfigurationAsync(config);
        await RefreshDirtyStateAsync();
    }

    public void OpenConfigurationsFromPlayMenu() => NavigateToConfigurations();
}
