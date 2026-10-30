using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using PlumbobForge.Backend.Configuration;

namespace PlumbobForge.Desktop.Services;

public static class CustomColorPresetsService
{
    private static readonly List<string> _presets = new();
    private static PlumbobForgeOptions? _options;

    public static IReadOnlyList<string> Presets => _presets;
    public static event Action? PresetsChanged;

    public static readonly IBrush RainbowWheelBrush = new ConicGradientBrush
    {
        Angle = 0,
        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(Color.FromRgb(255, 0, 0), 0.0),
            new(Color.FromRgb(255, 136, 0), 0.17),
            new(Color.FromRgb(255, 255, 0), 0.33),
            new(Color.FromRgb(0, 255, 0), 0.50),
            new(Color.FromRgb(0, 255, 255), 0.67),
            new(Color.FromRgb(0, 0, 255), 0.83),
            new(Color.FromRgb(255, 0, 255), 0.95),
            new(Color.FromRgb(255, 0, 0), 1.0)
        }
    };

    public static void Initialize(PlumbobForgeOptions options)
    {
        _options = options;
        _presets.Clear();
        if (options.CustomColorPresets != null)
        {
            foreach (var p in options.CustomColorPresets)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    var hex = NormalizeHex(p);
                    if (!_presets.Contains(hex, StringComparer.OrdinalIgnoreCase))
                    {
                        _presets.Add(hex);
                    }
                }
            }
        }
    }

    public static bool AddPreset(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var normalized = NormalizeHex(hex);
        if (!ColorPaletteGenerator.TryParseHex(normalized, out _)) return false;

        if (_presets.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        _presets.Add(normalized);
        SavePresets();
        PresetsChanged?.Invoke();
        return true;
    }

    public static bool RemovePreset(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var normalized = NormalizeHex(hex);
        var existing = _presets.FirstOrDefault(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _presets.Remove(existing);
            SavePresets();
            PresetsChanged?.Invoke();
            return true;
        }
        return false;
    }

    private static string NormalizeHex(string hex)
    {
        hex = hex.Trim();
        if (!hex.StartsWith("#"))
        {
            hex = "#" + hex;
        }
        return hex.ToLowerInvariant();
    }

    private static void SavePresets()
    {
        if (_options != null)
        {
            _options.CustomColorPresets = new List<string>(_presets);
            _ = AppSettingsService.SaveOptionsAsync(_options);
        }
    }
}
