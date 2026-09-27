using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty]
    private string _appVersion = "2.0.0";

    [ObservableProperty]
    private bool _isCheckingForUpdates = false;

    [ObservableProperty]
    private string _updateStatusText = "Click Check for Updates to check for a new version";

    [ObservableProperty]
    private bool _isUpdateAvailable = false;

    [ObservableProperty]
    private string _latestVersionTag = string.Empty;

    [ObservableProperty]
    private string _releaseTitle = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private string _releaseUrl = "https://github.com/PlumbobForge/PlumbobForgeApp/releases";

    [ObservableProperty]
    private string? _downloadUrl = null;

    [ObservableProperty]
    private string? _assetName = null;

    [ObservableProperty]
    private long? _fileSize = null;

    [ObservableProperty]
    private bool _isDownloadingUpdate = false;

    [ObservableProperty]
    private double _updateDownloadProgress = 0.0;

    [ObservableProperty]
    private string _updateDownloadProgressText = string.Empty;

    [ObservableProperty]
    private bool _isUpdateDownloaded = false;

    [ObservableProperty]
    private string? _downloadedInstallerPath = null;

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates || IsDownloadingUpdate) return;
        IsCheckingForUpdates = true;
        UpdateStatusText = "Connecting to GitHub Releases...";

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(AppVersion);

            if (result.ErrorMessage != null)
            {
                UpdateStatusText = $"Update check failed: {result.ErrorMessage}";
                ShowStatus($"Update check failed: {result.ErrorMessage}", isError: true);
                return;
            }

            IsUpdateAvailable = result.IsUpdateAvailable;
            LatestVersionTag = result.LatestVersion;
            ReleaseTitle = result.ReleaseTitle ?? result.LatestVersion;
            ReleaseNotes = result.ReleaseNotes ?? string.Empty;
            ReleaseUrl = result.ReleaseUrl ?? "https://github.com/PlumbobForge/PlumbobForgeApp/releases";
            DownloadUrl = result.DownloadUrl;
            AssetName = result.AssetName;
            FileSize = result.FileSize;

            if (result.IsUpdateAvailable)
            {
                UpdateStatusText = $"A new version (v{result.LatestVersion}) is available!";
                ShowStatus($"New version v{result.LatestVersion} is available to download!", isError: false);
            }
            else
            {
                UpdateStatusText = $"You're running the latest version (v{AppVersion})";
                ShowStatus($"PlumbobForge is up to date (v{AppVersion})", isError: false);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = "Failed to check for updates";
            ShowStatus($"Update check failed: {ex.Message}", isError: true);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    public async Task DownloadAndInstallUpdateAsync()
    {
        if (IsDownloadingUpdate) return;

        if (string.IsNullOrEmpty(DownloadUrl) || string.IsNullOrEmpty(AssetName))
        {
            OpenChangelog();
            return;
        }

        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0.0;
        UpdateDownloadProgressText = "Starting download...";

        var progress = new Progress<(long downloaded, long total, double percent, double speedMbPerSec)>(p =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                UpdateDownloadProgress = p.percent;
                double dlMb = p.downloaded / (1024.0 * 1024.0);
                double totalMb = p.total / (1024.0 * 1024.0);
                UpdateDownloadProgressText = $"{dlMb:F1} MB / {totalMb:F1} MB ({p.percent:P0}) — {p.speedMbPerSec:F1} MB/s";
            });
        });

        try
        {
            var downloadedPath = await _updateService.DownloadUpdateAsync(DownloadUrl, AssetName, progress);
            DownloadedInstallerPath = downloadedPath;
            IsUpdateDownloaded = true;
            UpdateStatusText = $"Downloaded {AssetName}! Launching installer...";
            ShowStatus($"Update downloaded successfully! Launching installer...", isError: false);

            await Task.Delay(1000);
            _updateService.LaunchInstallerAndExit(downloadedPath);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Download failed: {ex.Message}";
            ShowStatus($"Download failed: {ex.Message}", isError: true);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    public void OpenChangelog()
    {
        try
        {
            var url = !string.IsNullOrEmpty(ReleaseUrl) ? ReleaseUrl : "https://github.com/PlumbobForge/PlumbobForgeApp/releases/";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }
}
