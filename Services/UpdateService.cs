using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
    string? ErrorMessage = null
);

public class UpdateService
{
    private const string GitHubRepo = "PlumbobForge/PlumbobForgeApp";
    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PlumbobForge-Desktop", "2.0.0"));
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
                    return new UpdateCheckResult(false, currentVersionString, currentVersionString, null, null, null, null, null, null, "No published releases found yet.");
                }
                return new UpdateCheckResult(false, currentVersionString, currentVersionString, null, null, null, null, null, null, $"GitHub API returned {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                return new UpdateCheckResult(false, currentVersionString, currentVersionString, null, null, null, null, null, null, "Invalid response from GitHub.");
            }

            string tagName = node["tag_name"]?.GetValue<string>() ?? string.Empty;
            string releaseTitle = node["name"]?.GetValue<string>() ?? tagName;
            string releaseNotes = node["body"]?.GetValue<string>() ?? string.Empty;
            string releaseUrl = node["html_url"]?.GetValue<string>() ?? $"https://github.com/{GitHubRepo}/releases/latest";

            string cleanLatest = tagName.TrimStart('v', 'V').Trim();
            string cleanCurrent = currentVersionString.TrimStart('v', 'V').Trim();

            bool isNewer = CompareVersions(cleanLatest, cleanCurrent) > 0;

            // Find suitable Windows asset (.exe installer preferred, fallback to .zip)
            string? downloadUrl = null;
            string? assetName = null;
            long? fileSize = null;

            if (node["assets"] is JsonArray assets && assets.Count > 0)
            {
                JsonObject? chosenAsset = null;

                // Priority 1: .exe installer
                foreach (var a in assets)
                {
                    if (a is JsonObject obj)
                    {
                        var name = obj["name"]?.GetValue<string>() ?? string.Empty;
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".blockmap", StringComparison.OrdinalIgnoreCase))
                        {
                            chosenAsset = obj;
                            break;
                        }
                    }
                }

                // Priority 2: .zip archive
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
                FileSize: fileSize
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

    public void LaunchInstallerAndExit(string installerFilePath)
    {
        try
        {
            if (File.Exists(installerFilePath))
            {
                var currentAppExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                int currentPid = Environment.ProcessId;

                // Escaped paths for PowerShell
                string escapedInstaller = installerFilePath.Replace("'", "''");
                string escapedExe = (currentAppExe ?? string.Empty).Replace("'", "''");

                // Wrapper script:
                // 1. Waits for current app process to exit
                // 2. Runs the installer headless/silently with /S and /SILENT
                // 3. Deletes the installer file from %TEMP%
                // 4. Relaunches the updated application
                string psCommand = $@"
                    try {{ Wait-Process -Id {currentPid} -Timeout 15 -ErrorAction SilentlyContinue }} catch {{}};
                    Start-Sleep -Milliseconds 500;
                    $proc = Start-Process -FilePath '{escapedInstaller}' -ArgumentList '/S','/SILENT','/VERYSILENT','/NORESTART' -PassThru -Wait;
                    Start-Sleep -Seconds 1;
                    try {{ Remove-Item -Path '{escapedInstaller}' -Force -ErrorAction SilentlyContinue }} catch {{}};
                    if ('{escapedExe}' -ne '' -and (Test-Path '{escapedExe}')) {{
                        Start-Process -FilePath '{escapedExe}';
                    }}
                ";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{psCommand.Trim()}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(psi);

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                else
                {
                    Environment.Exit(0);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not launch installer: {ex.Message}", ex);
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
        // Remove any prerelease dash e.g. "2.0.1-beta" -> "2.0.1"
        int dashIdx = ver.IndexOf('-');
        if (dashIdx > 0) ver = ver.Substring(0, dashIdx);

        // Ensure at least 2 parts (e.g. "2" -> "2.0")
        var parts = ver.Split('.');
        if (parts.Length == 1) return $"{parts[0]}.0";
        return ver;
    }
}
