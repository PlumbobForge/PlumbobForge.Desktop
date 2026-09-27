using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Data;
using Avalonia.Platform;

namespace PlumbobForge.Desktop.Services.Localization;

public record LanguageOption(string Code, string DisplayName, string NativeName);

public class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? LanguageChanged;

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fallbackStrings = new(StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyList<LanguageOption> SupportedLanguages = new List<LanguageOption>
    {
        new("auto", "System Default (Auto)", "System Default (Auto)"),
        new("en", "English", "English"),
        new("pl", "Polish", "Polski")
    };

    public string CurrentLanguage { get; private set; } = "auto";
    public string EffectiveLanguage { get; private set; } = "en";

    public string this[string key] => GetString(key);

    public LocalizationManager()
    {
        LoadFallbackStrings();
    }

    public void Initialize(string? configuredLanguage)
    {
        SetLanguage(string.IsNullOrWhiteSpace(configuredLanguage) ? "auto" : configuredLanguage);
    }

    public void SetLanguage(string languageCode)
    {
        CurrentLanguage = string.IsNullOrWhiteSpace(languageCode) ? "auto" : languageCode;

        if (string.Equals(CurrentLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            string systemLang = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            var match = SupportedLanguages.FirstOrDefault(l =>
                !l.Code.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
                (l.Code.Equals(systemLang, StringComparison.OrdinalIgnoreCase) ||
                 l.Code.StartsWith(systemLang + "-", StringComparison.OrdinalIgnoreCase)));

            EffectiveLanguage = match != null ? match.Code : "en";
        }
        else
        {
            var match = SupportedLanguages.FirstOrDefault(l =>
                l.Code.Equals(CurrentLanguage, StringComparison.OrdinalIgnoreCase));
            EffectiveLanguage = match?.Code ?? "en";
        }

        try
        {
            var culture = new CultureInfo(EffectiveLanguage);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch
        {
            // Ignore invalid culture identifiers
        }

        LoadLanguage(EffectiveLanguage);

        // Notify Avalonia bindings of indexer update
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveLanguage)));

        LanguageChanged?.Invoke(EffectiveLanguage);
    }

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        if (_strings.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
        {
            return val;
        }

        if (_fallbackStrings.TryGetValue(key, out var fallback) && !string.IsNullOrEmpty(fallback))
        {
            return fallback;
        }

        return key;
    }

    public string GetString(string key, params object[] args)
    {
        string template = GetString(key);
        if (args == null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.CurrentUICulture, template, args);
        }
        catch
        {
            return template;
        }
    }

    private void LoadFallbackStrings()
    {
        _fallbackStrings.Clear();
        var dict = LoadDictionaryForLanguage("en");
        foreach (var kvp in dict)
        {
            _fallbackStrings[kvp.Key] = kvp.Value;
        }
    }

    private void LoadLanguage(string langCode)
    {
        _strings.Clear();

        if (string.Equals(langCode, "en", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var kvp in _fallbackStrings)
            {
                _strings[kvp.Key] = kvp.Value;
            }
            return;
        }

        var dict = LoadDictionaryForLanguage(langCode);
        foreach (var kvp in dict)
        {
            _strings[kvp.Key] = kvp.Value;
        }
    }

    private Dictionary<string, string> LoadDictionaryForLanguage(string langCode)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Check local file override next to app executable
        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Locales", $"{langCode}.json");
        if (File.Exists(localPath))
        {
            try
            {
                using var stream = File.OpenRead(localPath);
                ParseJsonStream(stream, result);
                if (result.Count > 0) return result;
            }
            catch { }
        }

        // 2. Load from embedded Avalonia resources
        var assetUri = new Uri($"avares://PlumbobForge.Desktop/Assets/Locales/{langCode}.json");
        try
        {
            if (AssetLoader.Exists(assetUri))
            {
                using var stream = AssetLoader.Open(assetUri);
                ParseJsonStream(stream, result);
            }
        }
        catch { }

        return result;
    }

    private static void ParseJsonStream(Stream stream, Dictionary<string, string> dict)
    {
        using var doc = JsonDocument.Parse(stream);
        FlattenElement(string.Empty, doc.RootElement, dict);
    }

    private static void FlattenElement(string prefix, JsonElement element, Dictionary<string, string> dict)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    string key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    FlattenElement(key, prop.Value, dict);
                }
                break;

            case JsonValueKind.String:
                dict[prefix] = element.GetString() ?? string.Empty;
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                dict[prefix] = element.ToString();
                break;
        }
    }
}
