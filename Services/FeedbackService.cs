using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PlumbobForge.Backend.Services;
using PlumbobForge.Installer.Shared;

namespace PlumbobForge.Desktop.Services;

public enum FeedbackCategory
{
    Feedback,
    BugReport,
    FeatureRequest,
    Other
}

public record FeedbackReport(
    FeedbackCategory Category,
    string Message,
    string? Contact,
    bool IncludeAppLogs,
    IReadOnlyList<string> GameLogFiles);

public static class FeedbackService
{
    // Set this to your deployed Worker URL (see PlumbobForge/feedback-worker/README.md).
    public const string Endpoint = "https://plumbobforge-feedback.blobcat.workers.dev/";

    // Not a secret (it ships in the exe); it only filters out random bots hitting the Worker URL.
    private const string AppKey = "pf-feedback-v1";

    public const int MaxMessageLength = 4000;
    private const int MaxGameLogs = 10;
    private static readonly TimeSpan GameLogMaxAge = TimeSpan.FromDays(7);
    private const int MaxLogTailBytes = 2 * 1024 * 1024;
    private const int MaxGameLogBytes = 512 * 1024;

    public static bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out _) && !Endpoint.Contains("YOUR-SUBDOMAIN", StringComparison.Ordinal);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static string AppVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            return v != null ? (v.Revision > 0 ? v.ToString(4) : v.ToString(3)) : InstallerConstants.DisplayVersion;
        }
    }

    public static bool IsResourceCfg(string path) =>
        string.Equals(Path.GetFileName(path), "Resource.cfg", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> FindRecentGameLogs(string? sims3Folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sims3Folder) || !Directory.Exists(sims3Folder))
                return Array.Empty<string>();

            var cutoff = DateTime.Now - GameLogMaxAge;
            var files = Directory.EnumerateFiles(sims3Folder)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return name.StartsWith("xcpt", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("ScriptError_", StringComparison.OrdinalIgnoreCase);
                })
                .Select(f => new FileInfo(f))
                .Where(fi => fi.LastWriteTime >= cutoff)
                .OrderByDescending(fi => fi.LastWriteTime)
                .Take(MaxGameLogs)
                .Select(fi => fi.FullName)
                .ToList();

            foreach (var cfg in new[]
                     {
                         Path.Combine(sims3Folder, "Mods", "Resource.cfg"),
                         Path.Combine(sims3Folder, "Mods", "Cache", "Config", "Resource.cfg")
                     })
            {
                if (File.Exists(cfg)) files.Add(cfg);
            }
            return files;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Writes the files that would be attached into <paramref name="folder"/> (already redacted).</summary>
    public static int WriteAttachments(FeedbackReport report, string folder)
    {
        Directory.CreateDirectory(folder);
        int count = 0;
        foreach (var (name, content) in CollectAttachments(report))
        {
            File.WriteAllText(Path.Combine(folder, name), content, Encoding.UTF8);
            count++;
        }
        return count;
    }

    public static async Task SendAsync(FeedbackReport report, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("The feedback service isn't set up in this build.");

        var boundary = "----PlumbobForge" + Guid.NewGuid().ToString("N");
        using var form = new MultipartFormDataContent(boundary);
        form.Headers.Remove("Content-Type");
        form.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");

        AddField(form, "category", report.Category.ToString());
        AddField(form, "message", Truncate(report.Message.Trim(), MaxMessageLength));
        AddField(form, "contact", Truncate(report.Contact?.Trim() ?? string.Empty, 100));
        AddField(form, "appVersion", AppVersion);
        AddField(form, "os", RuntimeInformation.OSDescription);

        var attachments = CollectAttachments(report);
        if (attachments.Count > 0)
        {
            var zipContent = new ByteArrayContent(BuildZip(attachments));
            zipContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
            zipContent.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data")
            {
                Name = "\"logs\"",
                FileName = $"\"PlumbobForge-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip\""
            };
            form.Add(zipContent);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        request.Headers.Add("X-PF-Key", AppKey);
        request.Headers.UserAgent.ParseAdd($"PlumbobForge-Desktop/{AppVersion}");

        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            AppLogger.LogWarning($"Feedback upload failed: {(int)response.StatusCode} {body}", null, "Feedback");
            throw new HttpRequestException((int)response.StatusCode == 429
                ? "Too many reports sent recently. Please try again in a while."
                : $"The feedback server returned {(int)response.StatusCode}.");
        }
    }

    private static void AddField(MultipartFormDataContent form, string name, string value)
    {
        var content = new StringContent(value, Encoding.UTF8);
        content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data") { Name = $"\"{name}\"" };
        form.Add(content);
    }

    private static List<(string Name, string Content)> CollectAttachments(FeedbackReport report)
    {
        var files = new List<(string, string)>();

        if (report.IncludeAppLogs)
        {
            var dir = AppLogger.LogsDirectory;
            foreach (var name in new[] { "plumbobforge.log", "errors.log", "fatal_crash.log" })
            {
                var text = ReadTail(Path.Combine(dir, name), MaxLogTailBytes);
                if (text != null) files.Add((name, Redact(text)));
            }
            var exeFatal = ReadTail(Path.Combine(AppContext.BaseDirectory, "fatal_crash.log"), MaxLogTailBytes);
            if (exeFatal != null) files.Add(("fatal_crash.appdir.log", Redact(exeFatal)));
        }

        foreach (var path in report.GameLogFiles)
        {
            var text = ReadTail(path, MaxGameLogBytes);
            if (text != null) files.Add((GameFileAttachmentName(path), Redact(text)));
        }

        return files;
    }

    // Resource.cfg exists in more than one place, so name it after its folder (Sims3_Mods_Cache_Config_Resource.cfg).
    private static string GameFileAttachmentName(string path)
    {
        if (IsResourceCfg(path))
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            int mods = Array.FindLastIndex(parts, p => p.Equals("Mods", StringComparison.OrdinalIgnoreCase));
            if (mods >= 0) return "Sims3_" + string.Join("_", parts[mods..]);
        }
        return "Sims3_" + Redact(Path.GetFileName(path));
    }

    private static byte[] BuildZip(List<(string Name, string Content)> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Reads at most the last <paramref name="maxBytes"/> of a file, even while the logger has it open.</summary>
    private static string? ReadTail(string path, int maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length == 0) return null;
            bool truncated = fs.Length > maxBytes;
            if (truncated) fs.Seek(-maxBytes, SeekOrigin.End);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            return truncated ? "[... earlier lines trimmed ...]\n" + text : text;
        }
        catch
        {
            return null;
        }
    }

    public static string Redact(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            text = text.Replace(profile.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        // Any other C:\Users\<name>\ paths (other drives, redirected folders, forward slashes).
        text = Regex.Replace(text, @"(?i)([A-Z]:[\\/]+Users[\\/]+)[^\\/\r\n""<>|:*?]+", "$1<user>");

        var user = Environment.UserName;
        if (user.Length >= 3)
            text = Regex.Replace(text, $@"(?i)(?<![A-Za-z0-9]){Regex.Escape(user)}(?![A-Za-z0-9])", "<user>");

        var machine = Environment.MachineName;
        if (machine.Length >= 3)
            text = Regex.Replace(text, $@"(?i)(?<![A-Za-z0-9]){Regex.Escape(machine)}(?![A-Za-z0-9])", "PC");

        return text;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
