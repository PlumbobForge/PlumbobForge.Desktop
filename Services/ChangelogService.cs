using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Platform;

namespace PlumbobForge.Desktop.Services;

public class ChangelogEntry
{
    public string Version { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
}

// A bullet written as "**Lead:** rest" shows the lead in bold.
public record ChangelogHighlight(string Lead, string Text)
{
    public bool HasLead => Lead.Length > 0;

    public static ChangelogHighlight Parse(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("**"))
        {
            int end = s.IndexOf("**", 2, StringComparison.Ordinal);
            if (end > 2)
            {
                return new ChangelogHighlight(s[2..end], " " + s[(end + 2)..].TrimStart());
            }
        }
        return new ChangelogHighlight(string.Empty, s);
    }
}

public class ChangelogData
{
    public List<ChangelogEntry> Versions { get; set; } = new();
}

// Short "what's new" bullets shown once after an update. Full notes stay on the GitHub release.
public static class ChangelogService
{
    // Keeps the toast small when several versions were skipped.
    public const int MaxHighlights = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Highlights for every version after lastSeen up to and including current, newest first.
    // With no lastSeen only the current version's highlights are returned.
    public static List<ChangelogHighlight> GetHighlights(string? lastSeen, string current, string language)
    {
        var entries = ReadFile(language)?.Versions;
        if (language != "en" && (entries == null || entries.Count == 0))
        {
            entries = ReadFile("en")?.Versions;
        }
        if (entries == null) return new List<ChangelogHighlight>();

        var currentVer = Parse(current);
        var lastVer = string.IsNullOrWhiteSpace(lastSeen) ? null : Parse(lastSeen);

        return entries
            .Select(e => (Entry: e, Ver: Parse(e.Version)))
            .Where(x => x.Ver != null && x.Ver <= currentVer &&
                        (lastVer == null ? x.Ver == currentVer : x.Ver > lastVer))
            .OrderByDescending(x => x.Ver)
            .SelectMany(x => x.Entry.Highlights)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Take(MaxHighlights)
            .Select(ChangelogHighlight.Parse)
            .ToList();
    }

    public static Version? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = version.Trim().TrimStart('v', 'V').Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count < 4) parts.Add("0");
        return Version.TryParse(string.Join('.', parts.Take(4)), out var v) ? v : null;
    }

    private static ChangelogData? ReadFile(string language)
    {
        try
        {
            // A file next to the exe wins, so notes can be fixed or translated without a rebuild.
            var local = Path.Combine(AppContext.BaseDirectory, "Changelog", $"{language}.json");
            if (File.Exists(local))
            {
                using var fs = File.OpenRead(local);
                return JsonSerializer.Deserialize<ChangelogData>(fs, JsonOptions);
            }

            var uri = new Uri($"avares://PlumbobForge.Desktop/Assets/Changelog/{language}.json");
            if (!AssetLoader.Exists(uri)) return null;
            using var stream = AssetLoader.Open(uri);
            return JsonSerializer.Deserialize<ChangelogData>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
