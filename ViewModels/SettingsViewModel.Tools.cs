using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public partial class SettingsViewModel
{
    public async Task LoadStatsAsync()
    {
        try
        {
            TotalSetsCount = await _db.SetsEntities.CountAsync();
            TotalItemsCount = await _db.MetaEntities.CountAsync();
        }
        catch { }
    }

    [RelayCommand]
    public async Task ImportDownloadsAsync()
    {
        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel();
        modal.Start(LocalizationManager.Instance.GetString("progress.importing_downloads_title"));

        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            int count = 0;
            await Task.Run(async () =>
            {
                count = await _pkgManager.ImportFromDownloadsAsync(duplicateAction: "rename", progress: modal);
            });

            modal.Finish(success: true);
            ShowStatus(count > 0 ? $"Successfully imported {count} items from Downloads!" : "No new items found to import.", isError: false);
            await LoadStatsAsync();

            var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
            if (contentVm != null) await contentVm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Import from Downloads failed", ex, "SettingsViewModel");
            modal.Finish(success: false);
            ShowStatus($"Import failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task RecheckTypesAsync()
    {
        var result = await DialogHelper.ShowRecheckPackageTypesAsync();
        if (result == null || !result.Confirmed) return;

        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel { ShowCloseAppCheckbox = false };
        modal.Start(LocalizationManager.Instance.GetString("progress.rechecking_types_title"));

        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            int totalScanned = 0;
            int updatedCount = 0;

            await Task.Run(async () =>
            {
                var res = await _pkgManager.RecheckPackageTypesAsync(skipUserTagged: result.SkipUserTagged, progress: modal);
                totalScanned = res.TotalScanned;
                updatedCount = res.UpdatedCount;
            });

            modal.Finish(success: true);
            ShowStatus(updatedCount > 0
                ? $"Updated categories for {updatedCount} of {totalScanned} items!"
                : $"All {totalScanned} items are up to date (no changes needed).", isError: false);

            var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
            if (contentVm != null) await contentVm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Rechecking package types failed", ex, "SettingsViewModel");
            modal.Finish(success: false);
            ShowStatus($"Recheck failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task AutoFixAsync()
    {
        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel { ShowCloseAppCheckbox = false };
        modal.Start(LocalizationManager.Instance.GetString("progress.running_autofix_title"));

        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            await Task.Run(async () =>
            {
                await _pkgManager.AutoFixAsync(progress: modal);
            });

            modal.Finish(success: true);
            ShowStatus("Database auto-fix completed successfully!", isError: false);
            await LoadStatsAsync();

            var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
            if (contentVm != null) await contentVm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Database auto-fix failed", ex, "SettingsViewModel");
            modal.Finish(success: false);
            ShowStatus($"Auto-fix failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task MigrateCcMagicAsync()
    {
        var migrationChoice = await DialogHelper.ShowMigrationAsync();
        if (migrationChoice == null || !migrationChoice.Confirmed) return;

        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel { ShowCloseAppCheckbox = false };

        string title = migrationChoice.Source == MigrationSource.S3mo
            ? LocalizationManager.Instance.GetString("progress.migrating_s3mo_title")
            : LocalizationManager.Instance.GetString("progress.migrating_ccmagic_title");
        modal.Start(title);

        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            MigrationResult result;
            if (migrationChoice.Source == MigrationSource.S3mo)
            {
                result = await Task.Run(async () =>
                {
                    return await _pkgManager.MigrateS3moAsync(migrationChoice.CustomPath!, migrationChoice.IsFullMigration, progress: modal);
                });

                modal.Finish(success: true);
                if (migrationChoice.IsFullMigration)
                {
                    ShowStatus($"Successfully migrated {result.PackagesMigrated} items, {result.SetsMigrated} sets, and {result.ConfigurationsMigrated} configurations from s3mo!", isError: false);
                }
                else
                {
                    ShowStatus(result.PackagesMigrated > 0
                        ? $"Successfully migrated {result.PackagesMigrated} files from s3mo!"
                        : "No s3mo files found to migrate.", isError: false);
                }
            }
            else
            {
                result = await Task.Run(async () =>
                {
                    return await _pkgManager.MigrateCcMagicAsync(migrationChoice.IsFullMigration, migrationChoice.CustomPath, progress: modal);
                });

                modal.Finish(success: true);
                if (migrationChoice.IsFullMigration)
                {
                    string collMsg = result.CollectionsMigrated > 0 ? $", and {result.CollectionsMigrated} collections" : string.Empty;
                    string msg = $"Successfully migrated {result.PackagesMigrated} items, {result.SetsMigrated} sets, {result.ConfigurationsMigrated} configurations{collMsg} from CC Magic!";
                    if (result.SkippedMissingFiles > 0)
                    {
                        msg += $" ({result.SkippedMissingFiles} database items were skipped as files were not found on disk)";
                    }
                    ShowStatus(msg, isError: false);
                }
                else
                {
                    ShowStatus(result.PackagesMigrated > 0
                        ? $"Successfully migrated {result.PackagesMigrated} files from CC Magic!"
                        : "No CC Magic files found to migrate.", isError: false);
                }
            }

            await LoadStatsAsync();

            var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
            if (contentVm != null) await contentVm.LoadDataAsync();

            var configVm = _serviceProvider.GetService<ConfigurationsViewModel>();
            if (configVm != null) await configVm.LoadDataAsync();

            var healthVm = _serviceProvider.GetService<HealthViewModel>();
            if (healthVm != null) await healthVm.LoadCollectionsAsync();

            if (mainVm != null) await mainVm.RefreshDirtyStateAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Migration failed", ex, "SettingsViewModel");
            string baseMsg = "Something failed, try restarting the app. If it continues to fail, please report it.";
            string errorDetails = string.IsNullOrWhiteSpace(ex.Message)
                ? baseMsg
                : $"{baseMsg} ({ex.Message})";
            modal.ErrorStep("migration_error", "Migration Failed", errorDetails);
            modal.Finish(success: false);
            ShowStatus(baseMsg, isError: true);
        }
    }

    [RelayCommand]
    public async Task MigrateToVaultAsync()
    {
        var choice = await DialogHelper.ShowVaultMigrationChoiceDialogAsync();
        if (choice == null) return;

        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel { ShowCloseAppCheckbox = false };
        modal.Start(LocalizationManager.Instance.GetString("progress.vault_migrating_title"));

        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            // Own scope (own DbContext): the shared UI context must not be used from a background thread.
            var (migrated, reclaimed) = await Task.Run(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var migrationService = scope.ServiceProvider.GetRequiredService<MigrationService>();
                return await migrationService.MigrateToVaultAsync(backupMode: choice.Value, progress: modal);
            });

            // Entities cached by the UI context still look like library items.
            _db.ChangeTracker.Clear();

            modal.Finish(success: true);
            double mb = reclaimed / (1024.0 * 1024.0);
            ShowStatus($"Vault migration complete: {migrated} items migrated ({mb:F1} MB staging space reclaimed).", isError: false);

            SetVaultEnabledWithoutPrompt(true);
            await SaveSettingsInternalAsync(silent: true);

            if (mainVm != null) await mainVm.RefreshDirtyStateAsync();

            var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
            if (contentVm != null) await contentVm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            AppLogger.LogError("Vault migration failed", ex, "SettingsViewModel");
            modal.Finish(success: false);
            ShowStatus($"Vault migration failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task CompactVaultAsync()
    {
        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel { ShowCloseAppCheckbox = false };
        modal.Start("Compacting Vault Shards...");

        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            const string stepId = "compact_vault";
            modal.StartStep(stepId, "Compacting shards with unused space...", progress: 0.0);

            var (count, reclaimed) = await Task.Run(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var vaultService = scope.ServiceProvider.GetRequiredService<VaultService>();
                return await vaultService.CompactVaultAsync((shardName, done, total) =>
                {
                    double pct = total > 0 ? (double)done / total : 1.0;
                    modal.UpdateStep(stepId, title: string.IsNullOrEmpty(shardName) ? null : $"Compacting {shardName}...", badge: $"{done}/{total}", progress: pct);
                });
            });

            _db.ChangeTracker.Clear();
            modal.CompleteStep(stepId, finalBadge: count > 0 ? $"{count} shards" : "Nothing to compact");
            modal.Finish(success: true);
            double mb = reclaimed / (1024.0 * 1024.0);
            ShowStatus(count > 0
                ? $"Vault compaction complete: {count} shards compacted, {mb:F1} MB reclaimed."
                : "Vault compaction: no shard had enough unused space to be worth compacting.", isError: false);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Vault compaction failed", ex, "SettingsViewModel");
            modal.Finish(success: false);
            ShowStatus($"Vault compaction failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task RevertVaultAsync()
    {
        var choice = await DialogHelper.ShowVaultRevertChoiceDialogAsync(_options.ManagedPackageFolderPath);
        if (choice == null) return;

        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel { ShowCloseAppCheckbox = false };
        modal.Start("Reverting to Default Storage Mode...");

        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        try
        {
            int restoredCount = await Task.Run(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var migrationService = scope.ServiceProvider.GetRequiredService<MigrationService>();
                return await migrationService.RevertFromVaultAsync(choice, progress: modal);
            });

            // The revert cleared vault flags with raw SQL; drop every entity the UI context still caches.
            _db.ChangeTracker.Clear();

            modal.Finish(success: true);
            ShowStatus($"Reversion complete: {restoredCount} items restored to Library.", isError: false);

            SetVaultEnabledWithoutPrompt(false);
            await SaveSettingsInternalAsync(silent: true);

            if (mainVm != null) await mainVm.RefreshDirtyStateAsync();

            var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
            if (contentVm != null) await contentVm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Vault reversion failed", ex, "SettingsViewModel");
            modal.Finish(success: false);
            ShowStatus($"Vault reversion failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public void OpenGithub()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/PlumbobForge") { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public void OpenSims3Folder()
    {
        try
        {
            var path = Sims3PathResolver.ResolveSims3UserFolder(_options.Sims3UserDir);
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            }
        }
        catch { }
    }
}
