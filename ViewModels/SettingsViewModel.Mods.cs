using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

// Mods folder support and the Mods-only storage mode (Storage mode card, walkthrough, post-update prompt).
public partial class SettingsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryStorage))]
    [NotifyPropertyChangedFor(nameof(IsVaultStorage))]
    private bool _isModsOnly;

    [ObservableProperty]
    private bool _enableModsFolder;

    [ObservableProperty]
    private bool _isChangingModsSupport;

    private bool _suppressModsToggleHandler;

    public bool IsLibraryStorage => !IsModsOnly && !EnableSingleSourceVault;
    public bool IsVaultStorage => !IsModsOnly && EnableSingleSourceVault;

    partial void OnEnableSingleSourceVaultChanged(bool oldValue, bool newValue)
    {
        OnPropertyChanged(nameof(IsLibraryStorage));
        OnPropertyChanged(nameof(IsVaultStorage));
    }

    private void LoadModsOptions()
    {
        _suppressModsToggleHandler = true;
        try
        {
            IsModsOnly = _options.ModsOnly;
            EnableModsFolder = _options.EnableModsFolder || _options.ModsOnly;
        }
        finally
        {
            _suppressModsToggleHandler = false;
        }
    }

    partial void OnEnableModsFolderChanged(bool value)
    {
        if (_isInitializing || _suppressModsToggleHandler) return;
        _ = SetModsSupportAsync(value, IsModsOnly);
    }

    // Saves the choice, flips the process-wide switch and brings the Mods folder in line: turning support on scans it
    // and applies the active configuration; turning it off puts back on the files a configuration held off.
    public async Task SetModsSupportAsync(bool enabled, bool modsOnly)
    {
        foreach (var o in new[] { _options, _serviceProvider.GetService<IOptionsMonitor<PlumbobForgeOptions>>()?.CurrentValue })
        {
            if (o == null) continue;
            o.EnableModsFolder = enabled || modsOnly;
            o.ModsOnly = modsOnly;
            o.HasAnsweredModsSupport = true;
        }
        ModsFolderSupport.Apply(_options);
        _suppressModsToggleHandler = true;
        try
        {
            IsModsOnly = modsOnly;
            EnableModsFolder = enabled || modsOnly;
        }
        finally
        {
            _suppressModsToggleHandler = false;
        }
        await AppSettingsService.SaveOptionsAsync(_options);

        IsChangingModsSupport = true;
        try
        {
            var released = await Task.Run(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var mods = scope.ServiceProvider.GetRequiredService<ModsFolderService>();
                if (!ModsFolderSupport.IsEnabled) return await mods.ReleaseConfigurationHoldsAsync();
                // Mods-only users may not have a Mods/Resource.cfg yet; the Mods-only sync writes one.
                if (modsOnly) await scope.ServiceProvider.GetRequiredService<CacheBuilderService>().SyncToSims3Async();
                await mods.ScanAsync();
                return null;
            });
            if (released is { Failed.Count: > 0 })
            {
                ShowStatus(LocalizationManager.Instance.GetString("settings.mods.release_failed", released.Failed.Count), isError: true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Changing Mods folder support failed", ex, "SettingsViewModel");
            ShowStatus(LocalizationManager.Instance.GetString("settings.mods.error", ex.Message), isError: true);
        }
        finally
        {
            IsChangingModsSupport = false;
        }
        await ReloadLibraryAsync();
    }

    // Mods-only to library mode: the Mods folder stays managed, and PlumbobForge's cache line goes into Resource.cfg.
    [RelayCommand]
    public Task TurnOnLibraryModeAsync() => LeaveModsOnlyAsync(vault: false);

    [RelayCommand]
    public Task TurnOnVaultModeAsync() => LeaveModsOnlyAsync(vault: true);

    private async Task LeaveModsOnlyAsync(bool vault)
    {
        if (!IsModsOnly) return;
        foreach (var o in new[] { _options, _serviceProvider.GetService<IOptionsMonitor<PlumbobForgeOptions>>()?.CurrentValue })
        {
            if (o == null) continue;
            o.ModsOnly = false;
            o.EnableModsFolder = true;
            o.HasAnsweredModsSupport = true;
            o.EnableSingleSourceVault = vault;
        }
        ModsFolderSupport.Apply(_options);
        _suppressModsToggleHandler = true;
        try
        {
            IsModsOnly = false;
            EnableModsFolder = true;
        }
        finally
        {
            _suppressModsToggleHandler = false;
        }
        SetVaultEnabledWithoutPrompt(vault);
        await AppSettingsService.SaveOptionsAsync(_options);

        IsChangingModsSupport = true;
        try
        {
            await Task.Run(async () =>
            {
                if (!string.IsNullOrWhiteSpace(_options.ManagedPackageFolderPath)) Directory.CreateDirectory(_options.ManagedPackageFolderPath);
                using var scope = _serviceProvider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CacheBuilderService>().SyncToSims3Async();
            });
            ShowStatus(LocalizationManager.Instance.GetString(vault ? "settings.mods.vault_on" : "settings.mods.library_on"), isError: false);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Leaving Mods-only mode failed", ex, "SettingsViewModel");
            ShowStatus(LocalizationManager.Instance.GetString("settings.mods.error", ex.Message), isError: true);
        }
        finally
        {
            IsChangingModsSupport = false;
        }
        await ReloadLibraryAsync();
    }

    private async Task ReloadLibraryAsync()
    {
        var library = _serviceProvider.GetService<ContentManagerViewModel>();
        if (library == null) return;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(library.LoadDataAsync);
    }
}
