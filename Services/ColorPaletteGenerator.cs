using System;
using Avalonia.Media;

namespace PlumbobForge.Desktop.Services;

public record GeneratedThemePalette(
    Color BgDark,
    Color BgSidebar,
    Color BgCard,
    Color BgCardAlt,
    Color BgSurface,
    Color BgInput,
    Color BgDarkest,
    Color BgPanelDark,
    Color BorderMain,
    Color BorderSubtle,
    Color BorderActive,
    Color TextMain,
    Color TextMuted,
    Color TextSubtle,
    Color TextDisabled,
    Color CapsuleBg,
    Color CapsuleBorder,
    Color Accent,
    Color AccentHover,
    Color AccentPressed,
    Color AccentBadgeBg,
    Color AccentRubberband
);

public static class ColorPaletteGenerator
{
    public static GeneratedThemePalette Generate(Color baseColor, bool isDark, Color? customAccent = null, double intensity = 45.0)
    {
        RgbToHsl(baseColor, out float h, out float s, out _);

        float factor = Math.Clamp((float)(intensity / 100.0), 0f, 1f);

        Color bgDark, bgSidebar, bgCard, bgCardAlt, bgSurface, bgInput, bgDarkest, bgPanelDark;
        Color borderMain, borderSubtle, borderActive;
        Color textMain, textMuted, textSubtle, textDisabled;
        Color capsuleBg, capsuleBorder;

        if (isDark)
        {
            // Dark Mode: Scale saturation and lightness according to intensity
            float bgSat = s < 0.03f ? 0f : Math.Clamp(s * (0.08f + 0.50f * factor), 0f, 0.65f);
            float borderSat = s < 0.03f ? 0f : Math.Clamp(s * (0.15f + 0.60f * factor), 0f, 0.75f);
            float textSat = s < 0.03f ? 0f : Math.Clamp(s * 0.08f * factor, 0f, 0.10f);

            bgDarkest = HslToRgb(h, bgSat, Math.Clamp(0.04f + 0.015f * factor, 0.03f, 0.06f));
            bgPanelDark = HslToRgb(h, bgSat, Math.Clamp(0.05f + 0.020f * factor, 0.04f, 0.08f));
            bgDark = HslToRgb(h, bgSat, Math.Clamp(0.065f + 0.025f * factor, 0.05f, 0.10f));
            bgInput = HslToRgb(h, bgSat, Math.Clamp(0.07f + 0.025f * factor, 0.06f, 0.10f));
            bgSurface = HslToRgb(h, bgSat * 1.05f, Math.Clamp(0.08f + 0.030f * factor, 0.07f, 0.12f));
            bgSidebar = HslToRgb(h, bgSat * 1.10f, Math.Clamp(0.09f + 0.035f * factor, 0.08f, 0.13f));
            bgCard = HslToRgb(h, bgSat * 1.15f, Math.Clamp(0.105f + 0.040f * factor, 0.09f, 0.15f));
            bgCardAlt = HslToRgb(h, bgSat * 1.20f, Math.Clamp(0.13f + 0.050f * factor, 0.11f, 0.18f));

            borderMain = HslToRgb(h, borderSat, Math.Clamp(0.16f + 0.05f * factor, 0.14f, 0.22f));
            borderSubtle = HslToRgb(h, borderSat, Math.Clamp(0.24f + 0.06f * factor, 0.20f, 0.32f));
            borderActive = HslToRgb(h, borderSat * 1.1f, Math.Clamp(0.35f + 0.08f * factor, 0.30f, 0.45f));

            textMain = HslToRgb(h, textSat, 0.98f);
            textMuted = HslToRgb(h, textSat, 0.72f);
            textSubtle = HslToRgb(h, textSat, 0.52f);
            textDisabled = HslToRgb(h, textSat, 0.35f);

            capsuleBg = Color.FromArgb(0xCC, bgSidebar.R, bgSidebar.G, bgSidebar.B);
            capsuleBorder = Color.FromArgb(0x40, borderActive.R, borderActive.G, borderActive.B);
        }
        else
        {
            // Light Mode: Apply soft, readable pastel tinting instead of stark pure white
            float bgSat = s < 0.03f ? 0f : Math.Clamp(s * (0.05f + 0.42f * factor), 0f, 0.55f);
            float borderSat = s < 0.03f ? 0f : Math.Clamp(s * (0.08f + 0.52f * factor), 0f, 0.65f);
            float textSat = s < 0.03f ? 0f : Math.Clamp(s * 0.16f * factor, 0f, 0.22f);

            float bgDarkL = Math.Clamp(0.965f - 0.085f * factor, 0.88f, 0.97f);
            bgDark = HslToRgb(h, bgSat, bgDarkL);
            bgPanelDark = HslToRgb(h, bgSat, Math.Clamp(bgDarkL - 0.02f, 0.86f, 0.96f));
            bgDarkest = HslToRgb(h, bgSat * 1.2f, Math.Clamp(bgDarkL - 0.06f, 0.80f, 0.92f));

            float cardSat = bgSat * 0.55f;
            float cardL = Math.Clamp(1.0f - 0.05f * factor, 0.94f, 1.0f);
            bgCard = HslToRgb(h, cardSat, cardL);
            bgSidebar = HslToRgb(h, cardSat * 1.1f, Math.Clamp(cardL - 0.015f, 0.93f, 0.99f));
            bgInput = HslToRgb(h, cardSat * 0.4f, Math.Clamp(1.0f - 0.02f * factor, 0.97f, 1.0f));

            bgSurface = HslToRgb(h, bgSat * 1.1f, Math.Clamp(bgDarkL - 0.035f, 0.84f, 0.94f));
            bgCardAlt = HslToRgb(h, bgSat, Math.Clamp(bgDarkL - 0.025f, 0.85f, 0.95f));

            borderMain = HslToRgb(h, borderSat, Math.Clamp(0.88f - 0.12f * factor, 0.72f, 0.89f));
            borderSubtle = HslToRgb(h, borderSat, Math.Clamp(0.82f - 0.14f * factor, 0.65f, 0.83f));
            borderActive = HslToRgb(h, borderSat * 1.2f, Math.Clamp(0.66f - 0.16f * factor, 0.48f, 0.68f));

            textMain = HslToRgb(h, textSat, 0.10f);
            textMuted = HslToRgb(h, textSat, 0.38f);
            textSubtle = HslToRgb(h, textSat, 0.54f);
            textDisabled = HslToRgb(h, textSat, 0.72f);

            capsuleBg = HslToRgb(h, cardSat, Math.Clamp(cardL, 0.94f, 1.0f));
            capsuleBorder = borderMain;
        }

        // Accent resolution
        Color accent;
        if (customAccent.HasValue)
        {
            accent = customAccent.Value;
        }
        else
        {
            // Autogenerate accent from base hue. If base is achromatic, default to vibrant emerald (160).
            float aH = s < 0.08f ? 160f : h;
            float aS = Math.Clamp(Math.Max(s, 0.75f), 0.65f, 0.90f);
            float aL = isDark ? 0.52f : 0.44f;
            accent = HslToRgb(aH, aS, aL);
        }

        RgbToHsl(accent, out float aHue, out float aSat, out float aLight);

        Color accentHover = isDark
            ? HslToRgb(aHue, aSat, Math.Clamp(aLight + 0.07f, 0f, 1f))
            : HslToRgb(aHue, aSat, Math.Clamp(aLight - 0.06f, 0f, 1f));

        Color accentPressed = isDark
            ? HslToRgb(aHue, aSat, Math.Clamp(aLight - 0.07f, 0f, 1f))
            : HslToRgb(aHue, aSat, Math.Clamp(aLight - 0.12f, 0f, 1f));

        Color accentBadgeBg = isDark
            ? HslToRgb(aHue, Math.Clamp(aSat * 0.7f, 0.3f, 0.7f), 0.14f)
            : HslToRgb(aHue, Math.Clamp(aSat * 0.7f, 0.3f, 0.7f), 0.93f);

        Color accentRubberband = Color.FromArgb(0x33, accent.R, accent.G, accent.B);

        return new GeneratedThemePalette(
            bgDark,
            bgSidebar,
            bgCard,
            bgCardAlt,
            bgSurface,
            bgInput,
            bgDarkest,
            bgPanelDark,
            borderMain,
            borderSubtle,
            borderActive,
            textMain,
            textMuted,
            textSubtle,
            textDisabled,
            capsuleBg,
            capsuleBorder,
            accent,
            accentHover,
            accentPressed,
            accentBadgeBg,
            accentRubberband
        );
    }

    public static void RgbToHsl(Color rgb, out float h, out float s, out float l)
    {
        float r = rgb.R / 255.0f;
        float g = rgb.G / 255.0f;
        float b = rgb.B / 255.0f;

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        l = (max + min) / 2.0f;

        if (delta <= float.Epsilon)
        {
            h = 0f;
            s = 0f;
            return;
        }

        s = l > 0.5f ? delta / (2.0f - max - min) : delta / (max + min);

        if (Math.Abs(max - r) <= float.Epsilon)
        {
            h = (g - b) / delta + (g < b ? 6.0f : 0.0f);
        }
        else if (Math.Abs(max - g) <= float.Epsilon)
        {
            h = (b - r) / delta + 2.0f;
        }
        else
        {
            h = (r - g) / delta + 4.0f;
        }

        h *= 60.0f;
        if (h < 0f) h += 360.0f;
    }

    public static Color HslToRgb(float h, float s, float l, byte a = 255)
    {
        h = ((h % 360.0f) + 360.0f) % 360.0f;
        s = Math.Clamp(s, 0f, 1f);
        l = Math.Clamp(l, 0f, 1f);

        if (s <= float.Epsilon)
        {
            byte grey = (byte)Math.Clamp((int)Math.Round(l * 255f), 0, 255);
            return Color.FromArgb(a, grey, grey, grey);
        }

        float q = l < 0.5f ? l * (1.0f + s) : l + s - (l * s);
        float p = 2.0f * l - q;
        float hk = h / 360.0f;

        byte r = (byte)Math.Clamp((int)Math.Round(HueToRgb(p, q, hk + 1.0f / 3.0f) * 255f), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(HueToRgb(p, q, hk) * 255f), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(HueToRgb(p, q, hk - 1.0f / 3.0f) * 255f), 0, 255);

        return Color.FromArgb(a, r, g, b);
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1.0f;
        if (t > 1.0f) t -= 1.0f;
        if (t < 1.0f / 6.0f) return p + (q - p) * 6.0f * t;
        if (t < 1.0f / 2.0f) return q;
        if (t < 2.0f / 3.0f) return p + (q - p) * (2.0f / 3.0f - t) * 6.0f;
        return p;
    }

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        hex = hex.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;

        try
        {
            color = Color.Parse(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ColorToHex(Color color, bool includeAlpha = false)
    {
        return includeAlpha
            ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}".ToLowerInvariant()
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}".ToLowerInvariant();
    }
}
