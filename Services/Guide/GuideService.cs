using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Platform;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.Services.Guide;

public sealed class GuideData
{
    public List<GuideCategory> Categories { get; set; } = new();
    public List<GuideArticle> Articles { get; set; } = new();
    public List<List<string>> Synonyms { get; set; } = new();
    public List<string> Stopwords { get; set; } = new();
}

public sealed class GuideCategory
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "RegularBook";
    public List<GuideSection> Sections { get; set; } = new();
}

public sealed class GuideSection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class GuideArticle
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Section { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public List<string> Questions { get; set; } = new();
    public List<string> Steps { get; set; } = new();
    public List<string> Tips { get; set; } = new();
    public List<string> Related { get; set; } = new();
    public GuideShowMe? ShowMe { get; set; }
}

public sealed class GuideShowMe
{
    public string Target { get; set; } = "";
    public string? Highlight { get; set; }
}

public sealed record GuideSearchResult(GuideArticle Article, double Score, IReadOnlyList<string> MatchedWords);

public sealed class GuideService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public GuideData Data { get; private set; } = new();
    public string Language { get; private set; } = "en";

    private Dictionary<string, GuideArticle> _byId = new();
    private Dictionary<string, List<string>> _synonyms = new();
    private HashSet<string> _stopwords = new();
    private List<IndexedArticle> _index = new();

    public GuideArticle? Find(string id) => _byId.GetValueOrDefault(id);

    public GuideCategory? FindCategory(string id) => Data.Categories.FirstOrDefault(c => c.Id == id);

    public void Load(string language)
    {
        Language = language;
        var data = ReadFile("en") ?? new GuideData();
        GuideData? english = language == "en" ? null : CloneViaJson(data);

        if (language != "en" && ReadFile(language) is { } local)
            Overlay(data, local);

        Data = data;
        _byId = data.Articles.Where(a => !string.IsNullOrEmpty(a.Id))
            .GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());

        // Run these through the same tokenizer as queries, so "don't" becomes "don" like it does there.
        _stopwords = new HashSet<string>(data.Stopwords.SelectMany(Tokenize).Select(w => w.Normalized));
        _synonyms = new Dictionary<string, List<string>>();
        foreach (var group in data.Synonyms)
        {
            var stems = group.SelectMany(Tokenize).Select(w => w.Stem).Distinct().ToList();
            foreach (var s in stems)
            {
                if (!_synonyms.TryGetValue(s, out var list)) _synonyms[s] = list = new List<string>();
                list.AddRange(stems.Where(o => o != s && !list.Contains(o)));
            }
        }

        // People often type English terms (CC, CAS, mods) whatever their app language,
        // so a translated guide also searches the English text, at a lower weight.
        var englishById = english?.Articles.ToDictionary(a => a.Id, a => a) ?? new();
        _index = data.Articles.Select(a =>
        {
            var cat = data.Categories.FirstOrDefault(c => c.Id == a.Category);
            var sec = cat?.Sections.FirstOrDefault(s => s.Id == a.Section);
            var idx = new IndexedArticle(a);
            idx.Add(a.Title, 5);
            foreach (var k in a.Keywords) idx.Add(k, 4, phrase: true);
            foreach (var q in a.Questions) idx.Add(q, 3, phrase: true);
            idx.Add(a.Summary, 2);
            if (sec != null) idx.Add(sec.Title, 2);
            if (cat != null) idx.Add(cat.Title, 1);
            foreach (var s in a.Steps) idx.Add(s, 1);
            foreach (var t in a.Tips) idx.Add(t, 1);
            if (englishById.TryGetValue(a.Id, out var en))
            {
                idx.Add(en.Title, 4);
                foreach (var k in en.Keywords) idx.Add(k, 3, phrase: true);
                foreach (var q in en.Questions) idx.Add(q, 2, phrase: true);
            }
            return idx;
        }).ToList();
    }

    public IReadOnlyList<GuideSearchResult> Search(string query, int max = 8)
    {
        var words = Tokenize(query).ToList();
        if (words.Count == 0) return Array.Empty<GuideSearchResult>();

        var meaningful = words.Where(w => !_stopwords.Contains(w.Normalized)).ToList();
        if (meaningful.Count == 0) meaningful = words;

        var results = new List<GuideSearchResult>();
        foreach (var article in _index)
        {
            double total = 0;
            var matched = new List<string>();
            var matchedStems = new List<string>();

            foreach (var word in meaningful)
            {
                double best = BestMatch(article, word.Stem, out _);
                if (_synonyms.TryGetValue(word.Stem, out var syns))
                {
                    foreach (var syn in syns)
                        best = Math.Max(best, BestMatch(article, syn, out _) * 0.85);
                }
                if (best <= 0) continue;
                total += best;
                if (best >= 1.5)
                {
                    matched.Add(word.Original);
                    matchedStems.Add(word.Stem);
                }
            }

            if (total <= 0) continue;

            // Questions and keywords that use several of the words together ("wrong" + "category").
            if (matchedStems.Count >= 2)
            {
                int bestPhrase = article.Phrases.Max(p => matchedStems.Count(s =>
                    p.Any(t => Similarity(t, s) >= 0.7) ||
                    (_synonyms.TryGetValue(s, out var syns) && syns.Any(y => p.Any(t => Similarity(t, y) >= 0.7)))));
                if (bestPhrase >= 2) total += 3 * bestPhrase;
            }

            double coverage = (double)matched.Count / meaningful.Count;
            double score = total * (0.4 + 0.6 * coverage);
            if (score >= 3.5)
                results.Add(new GuideSearchResult(article.Article, score, matched.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
        }

        return results.OrderByDescending(r => r.Score).Take(max).ToList();
    }

    private static double BestMatch(IndexedArticle article, string stem, out string? hit)
    {
        double best = 0;
        hit = null;
        foreach (var (token, weight) in article.Tokens)
        {
            if (weight <= best) continue; // can't beat what we have
            double sim = Similarity(token, stem);
            if (sim <= 0) continue;
            double score = sim * weight;
            if (score > best)
            {
                best = score;
                hit = token;
            }
        }
        return best;
    }

    internal static double Similarity(string a, string b)
    {
        if (a == b) return 1;
        int min = Math.Min(a.Length, b.Length);
        if (min >= 4 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)))
            return 0.85;

        int prefix = 0;
        while (prefix < min && a[prefix] == b[prefix]) prefix++;
        if (prefix >= 5 && prefix >= 0.7 * min) return 0.75;

        int max = Math.Max(a.Length, b.Length);
        if (min >= 5 && max - min <= 2)
        {
            int allowed = min >= 8 ? 2 : 1;
            if (Levenshtein(a, b, allowed) <= allowed) return 0.7;
        }
        return 0;
    }

    private static int Levenshtein(string a, string b, int cap)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, cur[j]);
            }
            if (rowMin > cap) return cap + 1;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    private readonly record struct Word(string Original, string Normalized, string Stem);

    private static IEnumerable<Word> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var sb = new StringBuilder();
        foreach (var ch in text + " ")
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                continue;
            }
            if (sb.Length >= 2)
            {
                var original = sb.ToString();
                var norm = Normalize(original);
                yield return new Word(original, norm, Stem(norm));
            }
            sb.Clear();
        }
    }

    internal static string Normalize(string s)
    {
        var lower = s.Trim().ToLowerInvariant().Replace('ł', 'l');
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    internal static string Stem(string w)
    {
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) return w[..^3] + "y";
        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal)) return TrimDouble(w[..^3]);
        if (w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal)) return TrimDouble(w[..^2]);
        if (w.Length > 4 && w.EndsWith("es", StringComparison.Ordinal) &&
            (w.EndsWith("shes", StringComparison.Ordinal) || w.EndsWith("ches", StringComparison.Ordinal) || w.EndsWith("xes", StringComparison.Ordinal) || w.EndsWith("sses", StringComparison.Ordinal)))
            return w[..^2];
        if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) && !w.EndsWith("us", StringComparison.Ordinal))
            return w[..^1];
        return w;
    }

    private static string TrimDouble(string w) =>
        w.Length > 3 && w[^1] == w[^2] && "bdgmnprt".Contains(w[^1]) ? w[..^1] : w;

    private sealed class IndexedArticle
    {
        public IndexedArticle(GuideArticle article) => Article = article;
        public GuideArticle Article { get; }
        public List<(string Token, double Weight)> Tokens { get; } = new();
        public List<List<string>> Phrases { get; } = new() { new List<string>() };

        public void Add(string? text, double weight, bool phrase = false)
        {
            var stems = Tokenize(text).Select(w => w.Stem).ToList();
            foreach (var s in stems) Tokens.Add((s, weight));
            if (phrase && stems.Count > 0) Phrases.Add(stems);
        }
    }

    private static GuideData? ReadFile(string language)
    {
        try
        {
            // A file next to the exe wins, so guides can be fixed or translated without a rebuild.
            var local = Path.Combine(AppContext.BaseDirectory, "Guides", $"{language}.json");
            if (File.Exists(local))
            {
                using var fs = File.OpenRead(local);
                return JsonSerializer.Deserialize<GuideData>(fs, JsonOptions);
            }

            var uri = new Uri($"avares://PlumbobForge.Desktop/Assets/Guides/{language}.json");
            if (!AssetLoader.Exists(uri)) return null;
            using var stream = AssetLoader.Open(uri);
            return JsonSerializer.Deserialize<GuideData>(stream, JsonOptions);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Could not read the {language} guide: {ex.Message}", ex, "Guide");
            return null;
        }
    }

    private static GuideData CloneViaJson(GuideData data) =>
        JsonSerializer.Deserialize<GuideData>(JsonSerializer.Serialize(data, JsonOptions), JsonOptions)!;

    private static void Overlay(GuideData target, GuideData local)
    {
        foreach (var lc in local.Categories)
        {
            var c = target.Categories.FirstOrDefault(x => x.Id == lc.Id);
            if (c == null) continue;
            if (!string.IsNullOrWhiteSpace(lc.Title)) c.Title = lc.Title;
            if (!string.IsNullOrWhiteSpace(lc.Description)) c.Description = lc.Description;
            foreach (var ls in lc.Sections)
            {
                var s = c.Sections.FirstOrDefault(x => x.Id == ls.Id);
                if (s != null && !string.IsNullOrWhiteSpace(ls.Title)) s.Title = ls.Title;
            }
        }

        foreach (var la in local.Articles)
        {
            var a = target.Articles.FirstOrDefault(x => x.Id == la.Id);
            if (a == null) continue;
            if (!string.IsNullOrWhiteSpace(la.Title)) a.Title = la.Title;
            if (!string.IsNullOrWhiteSpace(la.Summary)) a.Summary = la.Summary;
            if (la.Steps.Count > 0) a.Steps = la.Steps;
            if (la.Tips.Count > 0) a.Tips = la.Tips;
            if (la.Questions.Count > 0) a.Questions = la.Questions;
            if (la.Keywords.Count > 0) a.Keywords = la.Keywords;
        }

        target.Synonyms.AddRange(local.Synonyms);
        target.Stopwords.AddRange(local.Stopwords);
    }
}
