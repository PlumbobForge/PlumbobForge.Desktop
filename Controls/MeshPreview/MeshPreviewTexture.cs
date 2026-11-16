namespace PlumbobForge.Desktop.Controls.MeshPreview;

// An RGBA8 texture for MeshPreviewControl, rows in UV order (v = 0 first).
// Cutout: pixels with alpha below about 35% are not drawn (hair strands, lace).
public sealed record MeshPreviewTexture(int Width, int Height, byte[] Rgba, bool Cutout = false);
