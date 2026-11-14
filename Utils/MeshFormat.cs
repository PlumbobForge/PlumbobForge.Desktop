using System.Linq;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.Utils;

public static class MeshFormat
{
    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    public static string Compact(int triangles) =>
        triangles >= 1000 ? $"{triangles / 1000.0:0.#}k" : triangles.ToString();

    public static string WeightName(MeshWeightLevel level) => level switch
    {
        MeshWeightLevel.Medium => L("mesh.weight_medium"),
        MeshWeightLevel.Heavy => L("mesh.weight_heavy"),
        MeshWeightLevel.VeryHeavy => L("mesh.weight_very_heavy"),
        _ => L("mesh.weight_light")
    };

    public static string WeightTip(bool isCas)
    {
        var (medium, heavy, veryHeavy) = MeshWeight.Limits(isCas);
        return L(isCas ? "mesh.weight_tip_cas" : "mesh.weight_tip_bb", medium.ToString("N0"), heavy.ToString("N0"), veryHeavy.ToString("N0"));
    }

    public static string LodsText(MeshStats stats)
    {
        if (stats.Lods.Count == 0) return string.Empty;
        if (stats.Lods.Count <= 4)
        {
            return string.Join("  ·  ", stats.Lods.Select((l, i) => $"LOD{i} {l.Triangles:N0}"));
        }
        return L("mesh.largest_meshes", stats.Lods.Count, string.Join("  ·  ", stats.Lods.Take(4).Select(l => l.Triangles.ToString("N0"))));
    }
}
