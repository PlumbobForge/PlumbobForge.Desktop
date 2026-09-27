using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace PlumbobForge.Desktop.Services;

public record AccentOption(
    string Name,
    string DisplayName,
    string Hex,
    string HoverHex,
    string PressedHex,
    string DarkBadgeBg,
    string LightBadgeBg
);

public static class ThemeService
{
    public static readonly IReadOnlyList<AccentOption> Accents = new List<AccentOption>
    {
        new("Emerald", "Emerald", "#10b981", "#059669", "#047857", "#093829", "#d1fae5"),
        new("Teal", "Teal", "#14b8a6", "#0d9488", "#0f766e", "#042f2e", "#ccfbf1"),
        new("Cyan", "Cyan", "#06b6d4", "#0891b2", "#0e7490", "#0c2d38", "#cffafe"),
        new("Sapphire", "Sapphire", "#3b82f6", "#2563eb", "#1d4ed8", "#122544", "#dbeafe"),
        new("Indigo", "Indigo", "#6366f1", "#4f46e5", "#4338ca", "#1e1b4b", "#e0e7ff"),
        new("Violet", "Violet", "#8b5cf6", "#7c3aed", "#6d28d9", "#261a3d", "#ede9fe"),
        new("Pink", "Pink", "#ec4899", "#db2777", "#be185d", "#381226", "#fce7f3"),
        new("Rose", "Rose", "#f43f5e", "#e11d48", "#be123c", "#331320", "#ffe4e6"),
        new("Ruby", "Ruby", "#ef4444", "#dc2626", "#b91c1c", "#331518", "#fee2e2"),
        new("Orange", "Orange", "#f97316", "#ea580c", "#c2410c", "#431407", "#ffedd5"),
        new("Amber", "Amber", "#f59e0b", "#d97706", "#b45309", "#33240f", "#fef3c7")
    };

    public static string CurrentTheme { get; private set; } = "Dark";
    public static string CurrentAccent { get; private set; } = "Emerald";

    public static event Action? ThemeChanged;
    public static event Action? AccentChanged;

    public static void Initialize(string? theme, string? accent)
    {
        ApplyTheme(theme ?? "Dark");
        ApplyAccent(accent ?? "Emerald");
    }

    public static void ApplyTheme(string themeName)
    {
        CurrentTheme = string.IsNullOrWhiteSpace(themeName) ? "Dark" : themeName;

        if (Application.Current != null)
        {
            var isLight = CurrentTheme.Equals("Light", StringComparison.OrdinalIgnoreCase);
            Application.Current.RequestedThemeVariant = isLight ? ThemeVariant.Light : ThemeVariant.Dark;

            var appRes = Application.Current.Resources;
            var isDark = !isLight;

            var bgDark = Color.Parse(isDark ? "#0f172a" : "#f8fafc");
            var bgSidebar = Color.Parse(isDark ? "#182234" : "#ffffff");
            var bgCard = Color.Parse(isDark ? "#111827" : "#ffffff");
            var bgCardAlt = Color.Parse(isDark ? "#1e293b" : "#f1f5f9");
            var bgSurface = Color.Parse(isDark ? "#101928" : "#f1f5f9");
            var bgInput = Color.Parse(isDark ? "#0f172a" : "#ffffff");
            var bgDarkest = Color.Parse(isDark ? "#080c14" : "#e2e8f0");
            var bgPanelDark = Color.Parse(isDark ? "#090e17" : "#f8fafc");
            var borderMain = Color.Parse(isDark ? "#1e293b" : "#e2e8f0");
            var borderSubtle = Color.Parse(isDark ? "#334155" : "#cbd5e1");
            var borderActive = Color.Parse(isDark ? "#475569" : "#94a3b8");
            var textMain = Color.Parse(isDark ? "#f8fafc" : "#0f172a");
            var textMuted = Color.Parse(isDark ? "#94a3b8" : "#64748b");
            var textSubtle = Color.Parse(isDark ? "#64748b" : "#94a3b8");
            var textDisabled = Color.Parse(isDark ? "#475569" : "#cbd5e1");

            appRes["BgDark"] = new SolidColorBrush(bgDark);
            appRes["BgSidebar"] = new SolidColorBrush(bgSidebar);
            appRes["BgCard"] = new SolidColorBrush(bgCard);
            appRes["BgCardAlt"] = new SolidColorBrush(bgCardAlt);
            appRes["BgSurface"] = new SolidColorBrush(bgSurface);
            appRes["BgInput"] = new SolidColorBrush(bgInput);
            appRes["BgDarkest"] = new SolidColorBrush(bgDarkest);
            appRes["BgPanelDark"] = new SolidColorBrush(bgPanelDark);
            appRes["BorderMain"] = new SolidColorBrush(borderMain);
            appRes["BorderSubtle"] = new SolidColorBrush(borderSubtle);
            appRes["BorderActive"] = new SolidColorBrush(borderActive);
            appRes["TextMain"] = new SolidColorBrush(textMain);
            appRes["TextMuted"] = new SolidColorBrush(textMuted);
            appRes["TextSubtle"] = new SolidColorBrush(textSubtle);
            appRes["TextDisabled"] = new SolidColorBrush(textDisabled);
            appRes["CapsuleBgBrush"] = new SolidColorBrush(Color.Parse(isDark ? "#cc182234" : "#f2ffffff"));
            appRes["CapsuleBorderBrush"] = new SolidColorBrush(Color.Parse(isDark ? "#40475569" : "#e2e8f0"));

            var topFadeGradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.Parse(isDark ? "#f00f172a" : "#f0f8fafc"), 0.0),
                    new(Color.Parse(isDark ? "#c00f172a" : "#c0f8fafc"), 0.5),
                    new(Color.Parse(isDark ? "#500f172a" : "#50f8fafc"), 0.85),
                    new(Color.Parse(isDark ? "#000f172a" : "#00f8fafc"), 1.0)
                }
            };

            var itemCardGradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.Parse(isDark ? "#00182234" : "#00ffffff"), 0.0),
                    new(Color.Parse(isDark ? "#50182234" : "#50ffffff"), 0.35),
                    new(Color.Parse(isDark ? "#c0182234" : "#c0ffffff"), 0.75),
                    new(Color.Parse(isDark ? "#ff182234" : "#ffffffff"), 1.0)
                }
            };

            appRes["TopFadeGradientBrush"] = topFadeGradient;
            appRes["ItemCardGradientBrush"] = itemCardGradient;
        }

        // Re-evaluate badge background for the new theme
        ApplyAccent(CurrentAccent);
        ThemeChanged?.Invoke();
    }

    public static void ApplyAccent(string accentName)
    {
        var accent = Accents.FirstOrDefault(a => a.Name.Equals(accentName, StringComparison.OrdinalIgnoreCase))
                     ?? Accents[0];

        CurrentAccent = accent.Name;

        if (Application.Current != null)
        {
            var appRes = Application.Current.Resources;
            var isDark = !CurrentTheme.Equals("Light", StringComparison.OrdinalIgnoreCase);

            var accentColor = Color.Parse(accent.Hex);
            var hoverColor = Color.Parse(accent.HoverHex);
            var pressedColor = Color.Parse(accent.PressedHex);
            var badgeBgColor = Color.Parse(isDark ? accent.DarkBadgeBg : accent.LightBadgeBg);
            var rubberbandColor = Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B); // ~20% alpha

            appRes["AccentColor"] = accentColor;
            appRes["AccentBrush"] = new SolidColorBrush(accentColor);
            appRes["AccentHoverBrush"] = new SolidColorBrush(hoverColor);
            appRes["AccentPressedBrush"] = new SolidColorBrush(pressedColor);
            appRes["AccentBadgeBgBrush"] = new SolidColorBrush(badgeBgColor);
            appRes["AccentRubberbandBgBrush"] = new SolidColorBrush(rubberbandColor);
            appRes["SystemHighlightColor"] = accentColor;
            appRes["SystemControlHighlightListAccentLowBrush"] = new SolidColorBrush(accentColor);
            appRes["SystemControlHighlightListAccentMediumBrush"] = new SolidColorBrush(hoverColor);
            appRes["SystemControlHighlightListAccentHighBrush"] = new SolidColorBrush(pressedColor);
        }

        AccentChanged?.Invoke();
    }

    public static string GetCurrentAccentHex()
    {
        var accent = Accents.FirstOrDefault(a => a.Name.Equals(CurrentAccent, StringComparison.OrdinalIgnoreCase))
                     ?? Accents[0];
        return accent.Hex;
    }

    public static IBrush GetCurrentAccentBrush()
    {
        var accent = Accents.FirstOrDefault(a => a.Name.Equals(CurrentAccent, StringComparison.OrdinalIgnoreCase))
                     ?? Accents[0];
        return new SolidColorBrush(Color.Parse(accent.Hex));
    }

    public static IBrush GetCapsuleBorderBrush()
    {
        var isLight = CurrentTheme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        return new SolidColorBrush(Color.Parse(isLight ? "#e2e8f0" : "#40475569"));
    }
}
