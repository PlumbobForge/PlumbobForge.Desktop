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
using PlumbobForge.Installer.Shared;

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
        var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
        var ver = asmVer != null ? (asmVer.Revision > 0 ? asmVer.ToString(4) : asmVer.ToString(3)) : InstallerConstants.DisplayVersion;
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
                                !name.Contains("updater", StringComparison.OrdinalIgnoreCase))
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
                    downloadUrl = chosenAsset["browser_download_url"]?.GetValue<string>();
                    assetName = chosenAsset["name"]?.GetValue<string>();
                    if (chosenAsset["size"] is JsonNode sizeNode)
                    {
                        fileSize = sizeNode.GetValue<long>();
                    }
                }
            }

            return new UpdateCheckResult(
                IsUpdateAvailable: isNewer,
                CurrentVersion: cleanCurrent,
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
            return new UpdateCheckResult(false, currentVersionString, currentVersionString, null, null, null, null, null, null, false, ex.Message);
        }
    }

    public async Task<string> DownloadUpdateAsync(string downloadUrl, string destinationFileName, IProgress<(long downloaded, long total, double percent, double speedMbPerSec)>? progress = null, CancellationToken ct = default)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "PlumbobForge", "Updates");
        Directory.CreateDirectory(tempFolder);
        var targetFile = Path.Combine(tempFolder, destinationFileName);

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
        using var destStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long totalRead = 0;
        var stopwatch = Stopwatch.StartNew();

        int bytesRead;
        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;

            if (progress != null)
            {
                double percent = totalBytes > 0 ? (double)totalRead / totalBytes : 0.0;
                double speedMbPerSec = (totalRead / (1024.0 * 1024.0)) / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                progress.Report((totalRead, totalBytes, percent, speedMbPerSec));
            }
        }

        return targetFile;
    }

    public void ApplyUpdateAndExit(string downloadedFilePath)
    {
        var currentPid = Process.GetCurrentProcess().Id;
        var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        var localUpdater = Path.Combine(baseDir, "PlumbobForge-Updater.exe");
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(baseDir, "PlumbobForge.Desktop.exe");

        if (downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(localUpdater))
        {
            // Fast differential in-place update using native updater
            // Copy updater to %TEMP% to prevent file in-use lock during binary replacement
            var tempUpdaterDir = Path.Combine(Path.GetTempPath(), "PlumbobForge", "Updater");
            Directory.CreateDirectory(tempUpdaterDir);
            var stagingUpdater = Path.Combine(tempUpdaterDir, "PlumbobForge-Updater.exe");
            File.Copy(localUpdater, stagingUpdater, overwrite: true);

            var psi = new ProcessStartInfo
            {
                FileName = stagingUpdater,
                Arguments = $"--wait-pid {currentPid} --patch \"{downloadedFilePath}\" --target \"{baseDir}\" --relaunch \"{currentExe}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        else
        {
            // Standalone Setup executable update
            var psi = new ProcessStartInfo
            {
                FileName = downloadedFilePath,
                Arguments = $"/SILENT --wait-pid={currentPid}",
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        // Gracefully shutdown this application
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(0);
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private static int CompareVersions(string vA, string vB)
    {
        if (Version.TryParse(vA, out var verA) && Version.TryParse(vB, out var verB))
        {
            return verA.CompareTo(verB);
        }

        // Fallback segment comparison
        var partsA = vA.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var partsB = vB.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int max = Math.Max(partsA.Length, partsB.Length);

        for (int i = 0; i < max; i++)
        {
            int numA = i < partsA.Length && int.TryParse(partsA[i], out var a) ? a : 0;
            int numB = i < partsB.Length && int.TryParse(partsB[i], out var b) ? b : 0;
            if (numA != numB) return numA.CompareTo(numB);
        }

        return 0;
    }
}
