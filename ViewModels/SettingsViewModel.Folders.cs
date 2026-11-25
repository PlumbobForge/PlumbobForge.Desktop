using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;
using S3ForgeTools.GameFiles;

namespace PlumbobForge.Desktop.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty]
    private string _documentBaseDir = string.Empty;

    [ObservableProperty]
    private string _gameFilesDir = string.Empty;

    partial void OnGameFilesDirChanged(string value)
    {
        if (_isInitializing) return;
        _options.GameFilesDir = value;
        _ = SaveSettingsInternalAsync(silent: true);
    }

    [ObservableProperty]
    private string _sims3UserDir = string.Empty;

    partial void OnSims3UserDirChanged(string value)
    {
        if (_isInitializing) return;
        _options.Sims3UserDir = value;
        _ = SaveSettingsInternalAsync(silent: true);
    }

    [ObservableProperty]
    private string _pendingDocumentBaseDir = string.Empty;

    [ObservableProperty]
    private bool _isMoveFilesModalVisible = false;

    public string CurrentLibraryPathDisplay => Path.Combine(_options.DocumentBaseDir ?? string.Empty, "Library");
    public string PendingLibraryPathDisplay => Path.Combine(PendingDocumentBaseDir ?? string.Empty, "Library");

    [RelayCommand]
    public void AutoDetectBaseDir()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var defaultPath = Path.Combine(docs, "PlumbobForge");
        PromptOrApplyDocumentBaseDir(defaultPath);
    }

    [RelayCommand]
    public void AutoDetectGameFiles()
    {
        string detected = GamePathValidator.AutodetectGameFilesPath();
        if (!string.IsNullOrEmpty(detected))
        {
            GameFilesDir = detected;
            ShowStatus("Auto-detected The Sims 3 installation path!", isError: false);
            _ = SaveSettingsInternalAsync(silent: true);
        }
        else
        {
            ShowStatus("Could not automatically locate The Sims 3 installation. Please use Browse.", isError: true);
        }
    }

    [RelayCommand]
    public void AutoDetectSims3UserDir()
    {
        string detected = Sims3PathResolver.AutodetectSims3UserFolder();
        if (!string.IsNullOrEmpty(detected) && Directory.Exists(detected))
        {
            Sims3UserDir = detected;
            ShowStatus($"Auto-detected The Sims 3 folder: {Path.GetFileName(detected)}", isError: false);
            _ = SaveSettingsInternalAsync(silent: true);
        }
        else
        {
            ShowStatus("Could not automatically locate The Sims 3 user folder. Please use Browse.", isError: true);
        }
    }

    public void PromptOrApplyDocumentBaseDir(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return;

        // If the user picked the "Library" folder itself (e.g. C:\...\PlumbobForge\Library),
        // adjust to the parent folder because PlumbobForge always appends "Library" to DocumentBaseDir.
        try
        {
            var dirInfo = new DirectoryInfo(newPath.TrimEnd('\\', '/'));
            if (dirInfo.Exists && dirInfo.Name.Equals("Library", StringComparison.OrdinalIgnoreCase) && dirInfo.Parent != null)
            {
                newPath = dirInfo.Parent.FullName;
            }
        }
        catch { }

        string oldPath = _options.DocumentBaseDir ?? string.Empty;
        if (string.Equals(oldPath.TrimEnd('\\', '/'), newPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            DocumentBaseDir = newPath;
            return;
        }

        PendingDocumentBaseDir = newPath;
        OnPropertyChanged(nameof(CurrentLibraryPathDisplay));
        OnPropertyChanged(nameof(PendingLibraryPathDisplay));

        string oldLib = Path.Combine(oldPath, "Library");
        // Files in sub-folders (set folders, restored files) count too.
        if (Directory.Exists(oldLib) && Directory.EnumerateFiles(oldLib, "*", SearchOption.AllDirectories).Any())
        {
            IsMoveFilesModalVisible = true;
        }
        else
        {
            _ = UpdatePathOnlyAsync();
        }
    }

    [RelayCommand]
    public async Task MoveFilesAndSaveAsync()
    {
        IsMoveFilesModalVisible = false;
        string oldBase = _options.DocumentBaseDir ?? string.Empty;
        string newBase = PendingDocumentBaseDir;

        var mainVm = _serviceProvider.GetService<MainViewModel>();
        var modal = new TaskProgressModalViewModel();
        modal.Start(LocalizationManager.Instance.GetString("progress.moving_library_title"));
        if (mainVm != null) mainVm.ActiveProgressModal = modal;
        modal.Closed += () => { if (mainVm != null) mainVm.ActiveProgressModal = null; };

        var watcher = _serviceProvider.GetService<DownloadsWatcherService>();
        try
        {
            await Task.Run(async () =>
            {
                modal.StartStep("move_files", LocalizationManager.Instance.GetString("progress.moving_packages_thumbnails"), progress: 0.0);
                string oldLib = Path.Combine(oldBase, "Library");
                string newLib = Path.Combine(newBase, "Library");

                if (IsSameOrInside(newBase, oldBase) || IsSameOrInside(oldBase, newBase))
                {
                    throw new IOException("The new PlumbobForge folder cannot be inside the current one, or the other way around.");
                }

                // The library watcher would rescan the old folder halfway through the move, see the moved files as
                // deleted and drop their records (and with them their sets); the scan after the move would then add
                // them back to the Default set. Nothing watches the library until the move is done.
                if (watcher != null) await watcher.PauseWatchersAsync();

                Directory.CreateDirectory(newLib);
                MovePlumbobForgeFolder(oldBase, newBase, (done, total) =>
                    modal.UpdateStep("move_files", progress: (double)done / Math.Max(1, total), badge: $"{done}/{total}"),
                    () => modal.UpdateStep("move_files", progress: 0.0, badge: "Undoing"));

                modal.CompleteStep("move_files", finalBadge: "Files moved");

                // Vault items point at their shard under Mods/PF_Vault, which does not move with the library.
                var allItems = await _db.MetaEntities.Where(m => !m.IsVaultManaged).ToListAsync();
                foreach (var item in allItems)
                {
                    item.CompleteFileName = MapIntoLibrary(item.CompleteFileName, item.FileName, oldLib, newLib);
                }
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();

                SetDocumentBaseDirOption(newBase);
                DocumentBaseDir = newBase;
                await SaveSettingsInternalAsync(silent: true);
                ReloadSettingsNow();

                // Rebuild cache
                await FreshScope.RunAsync<PKGManager>(pkg => pkg.RunAsync(isRefresh: true, onProgress: null, progress: modal, forceRebuild: true));
            });

            modal.Finish(success: true);
            ShowStatus("Library successfully moved and database updated!", isError: false);
            var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
            if (contentVm != null) await contentVm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            modal.Finish(success: false);
            ShowStatus($"Failed to move files: {ex.Message}", isError: true);
        }
        finally
        {
            // Watches the new folder after a move, the old one after a failed move.
            watcher?.ReloadWatchers(_options);
        }
    }

    // What a move takes along: PlumbobForge's own folders and files only, never the rest of the folder (it may be a
    // folder like Documents). Logs stay: the open log file is still being written to.
    private string[] OwnFolders(bool required) => required
        ? new[]
        {
            "Library", "Library_Backup", "CustomIcons",
            _options.DownloadFolderName, _options.ArchiveFolderName, _options.TS3PackFolderName,
            _options.TS3PackStoreFolderName, _options.LegacyPackageFolderName,
        }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        // Rebuilt or re-exported when missing: one that cannot be moved is left behind, not a reason to stop.
        : new[] { "Thumbnails", "TextureCache", string.IsNullOrWhiteSpace(_options.SetCacheFolderName) ? "Builds" : _options.SetCacheFolderName, "Worlds", "Lots", "Sims" };

    private static readonly string[] OwnFiles = { "ManagedNonPackageItems.txt", "ManagedWorlds.txt" };

    // Moves PlumbobForge's folders and files from oldBase to newBase, each file keeping its place. If a file that
    // matters cannot be moved, the ones already moved go back and the move fails, so nothing is ever left split
    // between two places. A file already at the destination (copied there by hand) is used as it is; the old copy
    // stays where it is.
    private void MovePlumbobForgeFolder(string oldBase, string newBase, Action<int, int> onProgress, Action onUndo)
    {
        var required = new List<(string From, string To)>();
        var optional = new List<(string From, string To)>();
        void Collect(string name, List<(string From, string To)> into)
        {
            string from = Path.Combine(oldBase, name);
            if (!Directory.Exists(from)) return;
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                into.Add((file, Path.Combine(newBase, name, Path.GetRelativePath(from, file))));
            }
        }
        foreach (var name in OwnFolders(required: true)) Collect(name, required);
        foreach (var name in OwnFolders(required: false)) Collect(name, optional);
        foreach (var name in OwnFiles)
        {
            string from = Path.Combine(oldBase, name);
            if (File.Exists(from)) required.Add((from, Path.Combine(newBase, name)));
        }

        int total = required.Count + optional.Count, done = 0;
        var moved = new List<(string From, string To)>();
        foreach (var (from, to) in required)
        {
            try
            {
                if (!File.Exists(to))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Move(from, to);
                    moved.Add((from, to));
                }
            }
            catch (Exception ex)
            {
                onUndo();
                int notRestored = 0;
                for (int j = moved.Count - 1; j >= 0; j--)
                {
                    try { File.Move(moved[j].To, moved[j].From); }
                    catch { notRestored++; }
                }
                AppLogger.LogWarning($"[MoveLibrary] Could not move '{from}': {ex.Message}. Moved {moved.Count - notRestored} file(s) back, {notRestored} could not be moved back.");
                throw new IOException(notRestored == 0
                    ? $"Could not move {Path.GetFileName(from)} ({ex.Message}). Nothing was changed: the library is still in {oldBase}. If the file is in use, close the game or the item open in the CC Optimizer or mesh viewer, then try again."
                    : $"Could not move {Path.GetFileName(from)} ({ex.Message}). {notRestored} file(s) could not be moved back and are still in {newBase}; the library setting was not changed.");
            }
            onProgress(++done, total);
        }

        int leftBehind = 0;
        foreach (var (from, to) in optional)
        {
            try
            {
                if (!File.Exists(to))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Move(from, to);
                }
            }
            catch { leftBehind++; }
            onProgress(++done, total);
        }
        if (leftBehind > 0) AppLogger.LogWarning($"[MoveLibrary] {leftBehind} cache file(s) could not be moved and were left in {oldBase}; they are rebuilt when needed.");

        // The emptied folders go; anything still in them (a file that was already at the destination) keeps them.
        foreach (var name in OwnFolders(required: true).Concat(OwnFolders(required: false)))
        {
            RemoveEmptyFolders(Path.Combine(oldBase, name));
        }
    }

    private static void RemoveEmptyFolders(string dir)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            foreach (var sub in Directory.GetDirectories(dir)) RemoveEmptyFolders(sub);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch { }
    }

    // The services read the options monitor, a different object from this view model's options until the saved
    // settings file is read back; without this the rebuild right after a move would still use the old folder.
    private void SetDocumentBaseDirOption(string newBase)
    {
        _options.DocumentBaseDir = newBase;
        var monitor = _serviceProvider.GetService<IOptionsMonitor<PlumbobForgeOptions>>();
        if (monitor?.CurrentValue != null) monitor.CurrentValue.DocumentBaseDir = newBase;
    }

    // Services that take a fresh copy of the settings per use (thumbnails, archives) read the saved file, which is
    // otherwise only read back a moment later; until then they would still write to the old folder.
    private void ReloadSettingsNow()
    {
        try { _serviceProvider.GetService<IConfigurationRoot>()?.Reload(); }
        catch (Exception ex) { AppLogger.LogWarning($"[MoveLibrary] Could not reload the settings: {ex.Message}"); }
    }

    private static bool IsSameOrInside(string path, string folder)
    {
        string p = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    // An item's path in the new library: the same place below Library (a set folder, for example) when it was in the
    // old one, else straight in Library, where the library scan looks for it by name.
    private static string MapIntoLibrary(string? completeFileName, string fileName, string oldLib, string newLib)
    {
        if (!string.IsNullOrEmpty(completeFileName) && !string.IsNullOrEmpty(oldLib))
        {
            try
            {
                string rel = Path.GetRelativePath(oldLib, completeFileName);
                if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel))
                {
                    return Path.Combine(newLib, rel);
                }
            }
            catch { }
        }
        return Path.Combine(newLib, fileName);
    }

    [RelayCommand]
    public async Task UpdatePathOnlyAsync()
    {
        IsMoveFilesModalVisible = false;
        string oldLib = Path.Combine(_options.DocumentBaseDir ?? string.Empty, "Library");
        string newBase = PendingDocumentBaseDir;
        string newLib = Path.Combine(newBase, "Library");
        Directory.CreateDirectory(newLib);

        DocumentBaseDir = newBase;
        SetDocumentBaseDirOption(newBase);

        // Vault items point at their shard under Mods/PF_Vault, which has nothing to do with the library folder.
        var allItems = await _db.MetaEntities.Where(m => !m.IsVaultManaged).ToListAsync();
        foreach (var item in allItems)
        {
            item.CompleteFileName = MapIntoLibrary(item.CompleteFileName, item.FileName, oldLib, newLib);
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await SaveSettingsInternalAsync(silent: true);
        ReloadSettingsNow();

        // Reload watcher on new path
        var watcher = _serviceProvider.GetService<DownloadsWatcherService>();
        watcher?.ReloadWatchers(_options);

        // Rescan and refresh library view
        await _pkgManager.ScanLibraryDiskAsync();
        var contentVm = _serviceProvider.GetService<ContentManagerViewModel>();
        if (contentVm != null) await contentVm.LoadDataAsync();

        ShowStatus("Library path updated!", isError: false);
    }

    [RelayCommand]
    public void CancelPathChange()
    {
        IsMoveFilesModalVisible = false;
        DocumentBaseDir = _options.DocumentBaseDir ?? string.Empty;
    }

    public async Task BrowseDocumentBaseDirAsync(IStorageProvider storageProvider)
    {
        try
        {
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select PlumbobForge Library Root Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                string path = folders[0].Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    PromptOrApplyDocumentBaseDir(path);
                }
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Error selecting directory: {ex.Message}", isError: true);
        }
    }

    public async Task BrowseGameFilesDirAsync(IStorageProvider storageProvider)
    {
        try
        {
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select The Sims 3 Game Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                string path = folders[0].Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    GameFilesDir = path;
                    _options.GameFilesDir = path;
                    await SaveSettingsInternalAsync(silent: true);
                    ShowStatus("Game directory updated!", isError: false);
                }
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Error selecting directory: {ex.Message}", isError: true);
        }
    }

    public async Task BrowseSims3UserDirAsync(IStorageProvider storageProvider)
    {
        try
        {
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select The Sims 3 User Documents Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                string path = folders[0].Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    Sims3UserDir = path;
                    _options.Sims3UserDir = path;
                    await SaveSettingsInternalAsync(silent: true);
                    ShowStatus("The Sims 3 user folder updated!", isError: false);
                }
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Error selecting directory: {ex.Message}", isError: true);
        }
    }

    public async Task AddObservedFolderAsync(IStorageProvider storageProvider)
    {
        try
        {
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Folder to Monitor for Downloads",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                string path = folders[0].Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(path) && !ObservedFolders.Contains(path))
                {
                    ObservedFolders.Add(path);
                    _options.ObservedFolders = ObservedFolders.ToList();
                    await SaveSettingsInternalAsync(silent: true);
                }
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Error selecting folder: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public void RemoveObservedFolder(string folder)
    {
        if (ObservedFolders.Contains(folder))
        {
            ObservedFolders.Remove(folder);
            _options.ObservedFolders = ObservedFolders.ToList();
            _ = SaveSettingsInternalAsync(silent: true);
        }
    }
}
