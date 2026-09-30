using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PlumbobForge.Desktop.Services;

public static class IgnoredConflictsService
{
    private static readonly object _lock = new();
    private static HashSet<string>? _cachedFingerprints;

    private static string GetFilePath()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, "ignored_conflicts.json");
    }

    public static HashSet<string> GetIgnoredFingerprints()
    {
        lock (_lock)
        {
            if (_cachedFingerprints != null)
            {
                return new HashSet<string>(_cachedFingerprints, StringComparer.OrdinalIgnoreCase);
            }

            var path = GetFilePath();
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    _cachedFingerprints = list != null
                        ? new HashSet<string>(list, StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    _cachedFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            else
            {
                _cachedFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(_cachedFingerprints, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static bool IsIgnored(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return false;
        var set = GetIgnoredFingerprints();
        return set.Contains(fingerprint);
    }

    public static void Ignore(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return;
        lock (_lock)
        {
            var set = GetIgnoredFingerprints();
            if (set.Add(fingerprint))
            {
                _cachedFingerprints = set;
                SaveToDisk(set);
            }
        }
    }

    public static void Unignore(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return;
        lock (_lock)
        {
            var set = GetIgnoredFingerprints();
            if (set.Remove(fingerprint))
            {
                _cachedFingerprints = set;
                SaveToDisk(set);
            }
        }
    }

    public static void ClearAll()
    {
        lock (_lock)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _cachedFingerprints = set;
            SaveToDisk(set);
        }
    }

    private static void SaveToDisk(HashSet<string> set)
    {
        try
        {
            var path = GetFilePath();
            var json = JsonSerializer.Serialize(new List<string>(set), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IgnoredConflictsService] Failed to save ignored conflicts: {ex.Message}");
        }
    }
}
