using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Controls.MeshPreview;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Utils;
using S3ForgeTools.GameFiles.Package;

namespace PlumbobForge.Desktop.ViewModels;

// Preview pieces shared by the CC Optimizer and the 3D preview window.
public static class CcPreview
{
    private static string L(string key) => LocalizationManager.Instance.GetString(key);

    public static List<CcTextureOption> TextureOptions(PackageInspection? inspection, CcAnalysis? analysis, CasPreset? preset)
    {
        var options = new List<CcTextureOption> { new(CcOptimizerViewModel.ClayMode, L("optimizer.texture_clay")) };
        List<string> roles;
        bool ingame;
        if (analysis is { UsesObjectTextures: true })
        {
            roles = inspection == null ? new List<string>()
                : CompositorPresets(inspection, analysis, null).Values.SelectMany(p => CcMaterial.TextureRoles(inspection, p)).Distinct().ToList();
            ingame = roles.Count > 0 || analysis.Meshes.Any(m => m.Diffuse != null);
        }
        else
        {
            roles = inspection != null && preset != null ? CcMaterial.TextureRoles(inspection, preset) : new List<string>();
            ingame = roles.Count > 0;
        }
        if (ingame) options.Add(new CcTextureOption(CcMaterial.InGame, L("optimizer.texture_ingame")));
        foreach (var role in roles) options.Add(new CcTextureOption(role, role));
        return options;
    }

    // Each object mesh's texture compositor, read as a preset; colours gives the pattern colours.
    private static Dictionary<InspectedResource, CasPreset> CompositorPresets(PackageInspection inspection, CcAnalysis analysis, CasPreset? colours)
    {
        var o = new Dictionary<InspectedResource, CasPreset>();
        foreach (var txtc in analysis.Meshes.Select(m => m.Compositor).OfType<InspectedResource>().Distinct())
        {
            try
            {
                if (CcMaterial.CompositorPreset(inspection, txtc, colours) is { } preset) o[txtc] = preset;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Could not read texture compositor {txtc.Key}", ex, "CcPreview");
            }
        }
        return o;
    }

    // A few colour dots for a preset button: the hair colours, or the enabled pattern colours.
    public static IReadOnlyList<IBrush> Swatches(CasPreset preset)
    {
        var colours = new List<double[]>();
        if (preset.IsHair)
        {
            var (b, tints) = CcMaterial.HairColours(preset);
            colours.Add(b);
            if (tints[1] is { } highlight) colours.Add(highlight);
            if (tints[2] is { } tip) colours.Add(tip);
        }
        else
        {
            foreach (var name in new[] { "Pattern A", "Pattern B", "Pattern C", "Pattern D" })
                if (preset.Patterns.TryGetValue(name, out var p) && p.Enabled && p.Color is { } c) colours.Add(c);
        }
        static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
        return colours
            .Select(c => Color.FromRgb(B(c[0]), B(c[1]), B(c[2])))
            .Distinct()
            .Take(4)
            .Select(c => (IBrush)new SolidColorBrush(c))
            .ToList();
    }

    // The chosen mode if it's there, else in-game, else clay.
    public static CcTextureOption Pick(IReadOnlyList<CcTextureOption> options, string? chosen) =>
        options.FirstOrDefault(t => t.Mode == chosen)
        ?? options.FirstOrDefault(t => t.Mode == CcMaterial.InGame)
        ?? options[0];

    // Runs off the UI thread.
    public static (RgbaImage? Image, bool Cutout) CasTexture(PackageInspection inspection, CcAnalysis analysis, CasPreset preset, string mode,
        Dictionary<InspectedResource, RgbaImage> decoded)
    {
        var top = analysis.TopMeshes.ToList();
        var img = CcMaterial.PreviewTexture(inspection, preset, mode, decoded, top);
        var strands = top.Where(g => g.IsHairShader).ToList();
        bool cutout = img != null && mode == CcMaterial.InGame && CcMaterial.UsesCutout(img, strands.Count > 0 ? strands : top);
        return (img, cutout);
    }

    // Runs off the UI thread. Meshes painted by a compositor show its images, others their diffuse texture.
    public static Dictionary<CcMesh, (MeshPreviewTexture Texture, bool Cutout)> ObjectTextures(PackageInspection inspection, CcAnalysis analysis,
        Dictionary<InspectedResource, RgbaImage> decoded, bool transparency, string mode, CasPreset? colours)
    {
        var composed = new Dictionary<InspectedResource, (RgbaImage Image, MeshPreviewTexture Shown)?>();
        foreach (var (txtc, preset) in CompositorPresets(inspection, analysis, colours))
        {
            try
            {
                composed[txtc] = CcMaterial.PreviewTexture(inspection, preset, mode, decoded) is { } img
                    ? (img, new MeshPreviewTexture(img.Width, img.Height, img.Rgba, transparency))
                    : null;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Could not build the texture of {txtc.Key}", ex, "CcPreview");
            }
        }

        var textures = new Dictionary<InspectedResource, (RgbaImage Image, MeshPreviewTexture Shown)>();
        var result = new Dictionary<CcMesh, (MeshPreviewTexture, bool)>();
        foreach (var m in analysis.Meshes)
        {
            if (m.Compositor != null && composed.GetValueOrDefault(m.Compositor) is { } c)
            {
                result[m] = (c.Shown, mode == CcMaterial.InGame && CcMaterial.UsesCutout(c.Image, new[] { m.Geom }, wrap: true));
                continue;
            }
            if (m.Diffuse is not { } res) continue;
            if (!textures.TryGetValue(res, out var tex))
            {
                try
                {
                    if (!decoded.TryGetValue(res, out var img)) decoded[res] = img = DdsDecoder.Decode(inspection.Read(res));
                    tex = (img, new MeshPreviewTexture(img.Width, img.Height, img.Rgba, transparency));
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"Could not decode texture {res.Key}", ex, "CcPreview");
                    continue;
                }
                textures[res] = tex;
            }
            result[m] = (tex.Shown, CcMaterial.UsesCutout(tex.Image, new[] { m.Geom }, wrap: true));
        }
        return result;
    }

    // Only hair strands are cut out, not a hat.
    public static bool AllowCutout(CcMesh mesh, bool hasStrands, Dictionary<CcMesh, (MeshPreviewTexture Texture, bool Cutout)>? objectTextures) =>
        objectTextures != null
            ? objectTextures.TryGetValue(mesh, out var t) && t.Cutout
            : !hasStrands || mesh.Geom.IsHairShader;

    public static MeshPreviewTexture? OwnTexture(CcMesh mesh, Dictionary<CcMesh, (MeshPreviewTexture Texture, bool Cutout)>? objectTextures) =>
        objectTextures != null && objectTextures.TryGetValue(mesh, out var t) ? t.Texture : null;
}
