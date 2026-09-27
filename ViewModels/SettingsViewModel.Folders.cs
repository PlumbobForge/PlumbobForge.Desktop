using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    public void PromptOrApplyDocumentBaseDir(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return;
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
        if (Directory.Exists(oldLib) && Directory.GetFiles(oldLib).Length > 0)
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

        try
        {
            await Task.Run(async () =>
            {
                modal.StartStep("move_files", LocalizationManager.Instance.GetString("progress.moving_packages_thumbnails"), progress: 0.0);
                string oldLib = Path.Combine(oldBase, "Library");
                string newLib = Path.Combine(newBase, "Library");
                string oldThumb = Path.Combine(oldBase, "Thumbnails");
                string newThumb = Path.Combine(newBase, "Thumbnails");
                string oldBuilds = Path.Combine(oldBase, "Builds");
                string newBuilds = Path.Combine(newBase, "Builds");

                Directory.CreateDirectory(newLib);
                Directory.CreateDirectory(newThumb);
                Directory.CreateDirectory(newBuilds);

                if (Directory.Exists(oldLib))
                {
                    var files = Directory.GetFiles(oldLib, "*.*", SearchOption.TopDirectoryOnly);
                    int i = 0;
                    foreach (var file in files)
                    {
                        i++;
                        string dest = Path.Combine(newLib, Path.GetFileName(file));
                        if (!File.Exists(dest))
                        {
                            File.Move(file, dest);
                        }
                        double pct = (double)i / Math.Max(1, files.Length);
                        modal.UpdateStep("move_files", progress: pct, badge: $"{i}/{files.Length}");
                    }
                }

                if (Directory.Exists(oldThumb))
                {
                    var thumbs = Directory.GetFiles(oldThumb, "*.*", SearchOption.TopDirectoryOnly);
                    foreach (var thumb in thumbs)
                    {
                        string dest = Path.Combine(newThumb, Path.GetFileName(thumb));
                        if (!File.Exists(dest)) File.Move(thumb, dest);
                    }
                }

                modal.CompleteStep("move_files", finalBadge: "Files moved");

                // Update DB records
                var allItems = await _db.MetaEntities.ToListAsync();
                foreach (var item in allItems)
                {
                    item.CompleteFileName = Path.Combine(newLib, item.FileName);
                }
                await _db.SaveChangesAsync();

                _options.DocumentBaseDir = newBase;
                DocumentBaseDir = newBase;
                await SaveSettingsInternalAsync(silent: true);

                // Rebuild cache
                await _pkgManager.RunAsync(isRefresh: true, onProgress: null, progress: modal, forceRebuild: true);
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
    }

    [RelayCommand]
    public async Task UpdatePathOnlyAsync()
    {
        IsMoveFilesModalVisible = false;
        string newBase = PendingDocumentBaseDir;
        string newLib = Path.Combine(newBase, "Library");
        Directory.CreateDirectory(newLib);

        DocumentBaseDir = newBase;
        _options.DocumentBaseDir = newBase;

        var allItems = await _db.MetaEntities.ToListAsync();
        foreach (var item in allItems)
        {
            item.CompleteFileName = Path.Combine(newLib, item.FileName);
        }
        await _db.SaveChangesAsync();
        await SaveSettingsInternalAsync(silent: true);
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
