using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.Services;

// Starts The Sims 3 straight from its Bin folder, skipping the launcher. TS3W.exe loads every installed pack.
public static class GameLauncher
{
    // Tests replace the real start; returns whether the game started.
    public static Func<string, bool>? StartOverride { get; set; }

    public static string? FindGameExe(string? gameFilesDir)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameFilesDir)) dirs.Add(gameFilesDir);
        try
        {
            var registry = S3ForgeTools.Utils.Registry.InstallationInfo.Instance.BaseGame?.InstallDir;
            if (!string.IsNullOrWhiteSpace(registry)) dirs.Add(registry);
        }
        catch (Exception)
        {
        }

        foreach (var dir in dirs)
        {
            foreach (var exe in new[] { "TS3W.exe", "TS3.exe" })
            {
                foreach (var path in new[] { Path.Combine(dir, "Game", "Bin", exe), Path.Combine(dir, "Bin", exe), Path.Combine(dir, exe) })
                {
                    try
                    {
                        if (File.Exists(path)) return path;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        return null;
    }

    public static bool Start(string exe)
    {
        if (StartOverride != null) return StartOverride(exe);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not start The Sims 3", ex, "GameLauncher");
            return false;
        }
    }
}
