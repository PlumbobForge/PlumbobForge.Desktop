using System;
using System.Diagnostics;
using System.IO;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.Services;

public static class GameLaunchService
{
    // The chosen program, or TS3W.exe (the game with all expansions) under the game folder.
    public static string ResolveExecutable(string? configured, string? gameFilesDir)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        if (string.IsNullOrWhiteSpace(gameFilesDir)) return string.Empty;

        foreach (var name in new[] { "TS3W.exe", "TS3.exe" })
        {
            string candidate = Path.Combine(gameFilesDir.Trim(), "Game", "Bin", name);
            if (File.Exists(candidate)) return candidate;
        }
        return string.Empty;
    }

    // Null when the path can be started, otherwise the reason it can't.
    public static string? ValidateExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return LocalizationManager.Instance.GetString("play.exe_not_set");
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            return LocalizationManager.Instance.GetString("play.exe_not_exe");
        if (!File.Exists(path)) return LocalizationManager.Instance.GetString("play.exe_missing", path);
        return null;
    }

    public static void Launch(string path)
    {
        var error = ValidateExecutable(path);
        if (error != null) throw new InvalidOperationException(error);

        string fullPath = Path.GetFullPath(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            UseShellExecute = true
        })?.Dispose();
    }
}
