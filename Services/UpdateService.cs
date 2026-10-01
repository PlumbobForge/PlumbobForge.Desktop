using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace PlumbobForge.Desktop.Services;

public record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseTitle,
    string? ReleaseNotes,
    string? ReleaseUrl,
    string? DownloadUrl,
    string? AssetName,
    long? FileSize,
    bool IsDeltaUpdate = false,
    string? ErrorMessage = null
);

public class UpdateService
{
    private const string GitHubRepo = "PlumbobForge/PlumbobForgeApp";
    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.4";
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PlumbobForge-Desktop", ver));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersionString, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            using var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new UpdateCheckResult(false, currentVersionString, currentVersionString, null, null, null, null, null, null, false, "No published releases found yet.");
                }
                return new UpdateCheckResult(false, currentVersionString, currentVersionString, null, null, null, null, null, null, false, $"GitHub API returned {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                return new UpdateCheckResult(false, currentVersionString, currentVersionString, null, null, null, null, null, null, false, "Invalid response from GitHub.");
            }

            string tagName = node["tag_name"]?.GetValue<string>() ?? string.Empty;
            string releaseTitle = node["name"]?.GetValue<string>() ?? tagName;
            string releaseNotes = node["body"]?.GetValue<string>() ?? string.Empty;
            string releaseUrl = node["html_url"]?.GetValue<string>() ?? $"https://github.com/{GitHubRepo}/releases/latest";

            string cleanLatest = tagName.TrimStart('v', 'V').Trim();
            string cleanCurrent = currentVersionString.TrimStart('v', 'V').Trim();

            bool isNewer = CompareVersions(cleanLatest, cleanCurrent) > 0;

            // Check if delta patch updater is present on local installation
            var localAppDir = AppDomain.CurrentDomain.BaseDirectory;
            bool canApplyDelta = File.Exists(Path.Combine(localAppDir, "PlumbobForge-Updater.exe"));

            string? downloadUrl = null;
            string? assetName = null;
            long? fileSize = null;
            bool isDelta = false;

            if (node["assets"] is JsonArray assets && assets.Count > 0)
            {
                JsonObject? chosenAsset = null;

                // Priority 1: High-speed Differential / Delta Patch zip (if local updater is available)
                if (canApplyDelta)
                {
                    foreach (var a in assets)
                    {
                        if (a is JsonObject obj)
                        {
                            var name = obj["name"]?.GetValue<string>() ?? string.Empty;
                            if ((name.Contains("delta", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("patch", StringComparison.OrdinalIgnoreCase)) &&
                                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                chosenAsset = obj;
                                isDelta = true;
                                break;
                            }
                        }
                    }
                }

                // Priority 2: Standalone .exe Installer
                if (chosenAsset == null)
                {
                    foreach (var a in assets)
                    {
                        if (a is JsonObject obj)
                        {
                            var name = obj["name"]?.GetValue<string>() ?? string.Empty;
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                !name.EndsWith(".blockmap", StringComparison.OrdinalIgnoreCase))
                            {
                                chosenAsset = obj;
                                isDelta = false;
                                break;
                            }
                        }
                    }
                }

                // Priority 3: Fallback .zip archive
                if (chosenAsset == null)
                {
                    foreach (var a in assets)
                    {
                        if (a is JsonObject obj)
                        {
                            var name = obj["name"]?.GetValue<string>() ?? string.Empty;
                            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                chosenAsset = obj;
                                isDelta = false;
                                break;
                            }
                        }
                    }
                }

                if (chosenAsset != null)
                {
                    assetName = chosenAsset["name"]?.GetValue<string>();
                    downloadUrl = chosenAsset["browser_download_url"]?.GetValue<string>();
                    fileSize = chosenAsset["size"]?.GetValue<long>();
                }
            }

            return new UpdateCheckResult(
                IsUpdateAvailable: isNewer,
                CurrentVersion: currentVersionString,
                LatestVersion: cleanLatest,
                ReleaseTitle: releaseTitle,
                ReleaseNotes: releaseNotes,
                ReleaseUrl: releaseUrl,
                DownloadUrl: downloadUrl,
                AssetName: assetName,
                FileSize: fileSize,
                IsDeltaUpdate: isDelta
            );
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                IsUpdateAvailable: false,
                CurrentVersion: currentVersionString,
                LatestVersion: currentVersionString,
                ReleaseTitle: null,
                ReleaseNotes: null,
                ReleaseUrl: null,
                DownloadUrl: null,
                AssetName: null,
                FileSize: null,
                IsDeltaUpdate: false,
                ErrorMessage: ex.Message
            );
        }
    }

    public async Task<string> DownloadUpdateAsync(string downloadUrl, string assetName, IProgress<(long downloaded, long total, double percent, double speedMbPerSec)>? progress = null, CancellationToken ct = default)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "PlumbobForge", "Updates");
        Directory.CreateDirectory(tempFolder);

        var destinationPath = Path.Combine(tempFolder, assetName);

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long totalDownloaded = 0;
        var stopwatch = Stopwatch.StartNew();
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            totalDownloaded += bytesRead;

            if (progress != null && totalBytes > 0)
            {
                double percent = (double)totalDownloaded / totalBytes;
                double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
                double speedMbPerSec = (totalDownloaded / (1024.0 * 1024.0)) / seconds;
                progress.Report((totalDownloaded, totalBytes, percent, speedMbPerSec));
            }
        }

        return destinationPath;
    }

    /// <summary>
    /// Applies update directly without PowerShell or console flashes, using native watchdog process hand-off.
    /// </summary>
    public void ApplyUpdateAndExit(string updateFilePath)
    {
        try
        {
            if (!File.Exists(updateFilePath))
            {
                throw new FileNotFoundException("Update file not found", updateFilePath);
            }

            var currentAppExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            int currentPid = Environment.ProcessId;
            var appDir = AppDomain.CurrentDomain.BaseDirectory;

            // Scenario 1: Differential / Delta ZIP patch
            if (updateFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var localUpdater = Path.Combine(appDir, "PlumbobForge-Updater.exe");
                if (File.Exists(localUpdater))
                {
                    // Copy updater to %TEMP% so no assembly in appDir is locked
                    var tempUpdaterDir = Path.Combine(Path.GetTempPath(), "PlumbobForge", "Updater");
                    Directory.CreateDirectory(tempUpdaterDir);
                    var tempUpdaterPath = Path.Combine(tempUpdaterDir, "PlumbobForge-Updater.exe");
                    File.Copy(localUpdater, tempUpdaterPath, overwrite: true);

                    var psi = new ProcessStartInfo
                    {
                        FileName = tempUpdaterPath,
                        Arguments = $"--wait-pid {currentPid} --patch \"{updateFilePath}\" --target \"{appDir}\" --relaunch \"{currentAppExe}\"",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    Process.Start(psi);
                    ShutdownApplication();
                    return;
                }
            }

            // Scenario 2: Standalone Setup Installer (Bootstrapper)
            var setupPsi = new ProcessStartInfo
            {
                FileName = updateFilePath,
                Arguments = $"/S /RUN /WAITPID={currentPid}",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(setupPsi);
            ShutdownApplication();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not apply update: {ex.Message}", ex);
        }
    }

    [Obsolete("Use ApplyUpdateAndExit instead.")]
    public void LaunchInstallerAndExit(string installerFilePath) => ApplyUpdateAndExit(installerFilePath);

    private static void ShutdownApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private static int CompareVersions(string v1, string v2)
    {
        if (Version.TryParse(NormalizeVersionString(v1), out var ver1) &&
            Version.TryParse(NormalizeVersionString(v2), out var ver2))
        {
            return ver1.CompareTo(ver2);
        }

        return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersionString(string ver)
    {
        int dashIdx = ver.IndexOf('-');
        if (dashIdx > 0) ver = ver.Substring(0, dashIdx);
        return ver.Trim();
    }
}
