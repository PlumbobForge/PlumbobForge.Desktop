namespace PlumbobForge.Desktop.Controls.MeshPreview;

// RGBA texture. Cutout: low-alpha pixels are not drawn.
public sealed record MeshPreviewTexture(int Width, int Height, byte[] Rgba, bool Cutout = false);
