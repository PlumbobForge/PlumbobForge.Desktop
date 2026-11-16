using System;
using System.Numerics;
using Avalonia.Media;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.Controls.MeshPreview;

public sealed class MeshPreviewItem
{
    public static readonly Color[] PartColors =
    {
        Color.FromRgb(0xd9, 0xb4, 0x8f),
        Color.FromRgb(0x8f, 0xb3, 0xd9),
        Color.FromRgb(0xa3, 0xd9, 0x8f),
        Color.FromRgb(0xd9, 0x8f, 0xb8),
    };

    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required uint[] Indices { get; init; }
    public float[]? Uvs { get; init; }
    public Color Color { get; init; } = PartColors[0];
    // Already has back faces, so only front faces are drawn.
    public bool IsDoubleSided { get; init; }
    public bool AllowCutout { get; init; } = true;
    // Null uses the control's texture.
    public MeshPreviewTexture? Texture { get; init; }

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;

    public static MeshPreviewItem FromGeom(GeomMesh geom, int partIndex = 0, bool doubleSided = false, bool allowCutout = true, MeshPreviewTexture? texture = null)
    {
        var positions = geom.GetFloats(GeomVertexFormat.UsagePosition, out _) ?? Array.Empty<float>();
        var normals = geom.GetFloats(GeomVertexFormat.UsageNormal, out int n);
        if (normals == null || n != 3) normals = ComputeNormals(positions, geom.Indices);
        var uvs = geom.GetFloats(GeomVertexFormat.UsageUv, out int uvN);
        if (uvs != null && uvN != 2)
        {
            float[]? first = null;
            if (uvN > 2)
            {
                first = new float[geom.VertexCount * 2];
                for (int v = 0; v < geom.VertexCount; v++) { first[v * 2] = uvs[v * uvN]; first[v * 2 + 1] = uvs[v * uvN + 1]; }
            }
            uvs = first;
        }
        return new MeshPreviewItem
        {
            Positions = positions,
            Normals = normals,
            Indices = geom.Indices,
            Uvs = uvs,
            Color = PartColors[Math.Abs(partIndex) % PartColors.Length],
            IsDoubleSided = doubleSided,
            AllowCutout = allowCutout,
            Texture = texture,
        };
    }

    private static float[] ComputeNormals(float[] pos, uint[] indices)
    {
        int count = pos.Length / 3;
        var acc = new Vector3[count];
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint a = indices[i], b = indices[i + 1], c = indices[i + 2];
            if (a >= count || b >= count || c >= count) continue;
            var pa = new Vector3(pos[a * 3], pos[a * 3 + 1], pos[a * 3 + 2]);
            var pb = new Vector3(pos[b * 3], pos[b * 3 + 1], pos[b * 3 + 2]);
            var pc = new Vector3(pos[c * 3], pos[c * 3 + 1], pos[c * 3 + 2]);
            var face = Vector3.Cross(pb - pa, pc - pa);
            acc[a] += face; acc[b] += face; acc[c] += face;
        }
        var result = new float[count * 3];
        for (int v = 0; v < count; v++)
        {
            var nrm = acc[v].LengthSquared() > 0 ? Vector3.Normalize(acc[v]) : Vector3.UnitY;
            result[v * 3] = nrm.X; result[v * 3 + 1] = nrm.Y; result[v * 3 + 2] = nrm.Z;
        }
        return result;
    }
}
