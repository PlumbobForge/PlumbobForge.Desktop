using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;

namespace PlumbobForge.Desktop.Controls.MeshPreview;

// 3D mesh preview. Left drag turns, right drag moves, wheel zooms, double click frames.
public class MeshPreviewControl : OpenGlControlBase, Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<IReadOnlyList<MeshPreviewItem>?> MeshesProperty =
        AvaloniaProperty.Register<MeshPreviewControl, IReadOnlyList<MeshPreviewItem>?>(nameof(Meshes));

    public static readonly StyledProperty<bool> ShowWireframeProperty =
        AvaloniaProperty.Register<MeshPreviewControl, bool>(nameof(ShowWireframe), true);

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<MeshPreviewControl, bool>(nameof(ShowGrid), true);

    public static readonly StyledProperty<MeshPreviewTexture?> TextureProperty =
        AvaloniaProperty.Register<MeshPreviewControl, MeshPreviewTexture?>(nameof(Texture));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<MeshPreviewControl, IBrush?>(nameof(Background));

    public IReadOnlyList<MeshPreviewItem>? Meshes { get => GetValue(MeshesProperty); set => SetValue(MeshesProperty, value); }
    public bool ShowWireframe { get => GetValue(ShowWireframeProperty); set => SetValue(ShowWireframeProperty, value); }
    public bool ShowGrid { get => GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public MeshPreviewTexture? Texture { get => GetValue(TextureProperty); set => SetValue(TextureProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    private const float FieldOfView = 35f * MathF.PI / 180f;
    private static readonly Vector3 SkyColor = new(1f, 1f, 1f), GroundColor = new(0x44 / 255f, 0x50 / 255f, 0x66 / 255f);

    private float _yaw = MathF.Atan2(0.3f, 1f), _pitch = 0.143f, _distance = 3f;
    private Vector3 _target;
    private Vector3 _boundsMin, _boundsMax;
    private bool _hasFramedBounds;
    private float _sceneRadius = 1f;

    private Point? _dragStart;
    private bool _panning;

    private sealed class GpuMesh
    {
        public int Vbo, Ibo, IndexCount, WireIbo, WireCount;
        public Vector3 Color;
        public bool DoubleSided;
        public bool AllowCutout;
        public MeshPreviewTexture? Texture;
    }
    private readonly Dictionary<MeshPreviewTexture, int> _meshTextures = new(ReferenceEqualityComparer.Instance);
    private readonly List<GpuMesh> _gpu = new();
    private IReadOnlyList<MeshPreviewItem>? _uploaded;
    private int _solidProgram, _lineProgram, _vao, _gridVbo, _gridCount, _glTexture;
    private MeshPreviewTexture? _uploadedTexture;
    private bool _glReady;
    private unsafe delegate* unmanaged<float, float, void> _glPolygonOffset;
    private unsafe delegate* unmanaged<int, int, void> _glBlendFunc;
    private unsafe delegate* unmanaged<int, void> _glCullFace;
    private unsafe delegate* unmanaged<int, float, float, float, void> _glUniform3f;
    private unsafe delegate* unmanaged<int, float, float, float, float, void> _glUniform4f;
    private unsafe delegate* unmanaged<int, int, int, int, int, int, void*, void> _glReadPixels;
    private static unsafe delegate* unmanaged<int, void> _glDisableVertexAttribArray;
    private unsafe delegate* unmanaged<int, void> _glGenerateMipmap;

    static MeshPreviewControl()
    {
        FocusableProperty.OverrideDefaultValue<MeshPreviewControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<MeshPreviewControl>(true);
    }

    public MeshPreviewControl()
    {
        DoubleTapped += (_, _) => { FrameAll(); };
    }

    public void FrameAll()
    {
        if (!TryGetBounds(Meshes, out var min, out var max)) return;
        Frame(min, max);
        RequestNextFrameRendering();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MeshesProperty)
        {
            if (TryGetBounds(Meshes, out var min, out var max) && (!_hasFramedBounds || !SimilarBounds(min, max)))
            {
                Frame(min, max);
            }
            RequestNextFrameRendering();
        }
        else if (change.Property == ShowWireframeProperty || change.Property == ShowGridProperty || change.Property == BackgroundProperty
                 || change.Property == TextureProperty
                 || change.Property == BoundsProperty)
        {
            RequestNextFrameRendering();
        }
    }

    private void Frame(Vector3 min, Vector3 max)
    {
        _boundsMin = min; _boundsMax = max; _hasFramedBounds = true;
        var size = max - min;
        _target = (min + max) / 2;
        float radius = MathF.Max(size.Length() / 2, 1e-4f);
        _sceneRadius = radius;
        float half = FieldOfView / 2;
        float aspect = Bounds.Height > 0 ? (float)(Bounds.Width / Bounds.Height) : 1f;
        _distance = radius / MathF.Sin(MathF.Min(half, MathF.Atan(MathF.Tan(half) * aspect))) * 1.05f;
        _yaw = MathF.Atan2(0.3f, 1f);
        // Something lying flat (a rug, a floor tile) is seen from above; from near eye level it is only a sliver.
        bool lyingFlat = size.Y < 0.15f * MathF.Max(size.X, size.Z);
        _pitch = lyingFlat ? 0.9f : 0.143f;
    }

    private bool SimilarBounds(Vector3 min, Vector3 max)
    {
        float scale = MathF.Max((_boundsMax - _boundsMin).Length(), 1e-4f);
        return (min - _boundsMin).Length() < scale * 0.1f && (max - _boundsMax).Length() < scale * 0.1f;
    }

    private static bool TryGetBounds(IReadOnlyList<MeshPreviewItem>? meshes, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue); max = new Vector3(float.MinValue);
        if (meshes == null) return false;
        bool any = false;
        foreach (var m in meshes)
        {
            var p = m.Positions;
            for (int i = 0; i + 2 < p.Length; i += 3)
            {
                var v = new Vector3(p[i], p[i + 1], p[i + 2]);
                min = Vector3.Min(min, v); max = Vector3.Max(max, v);
                any = true;
            }
        }
        return any;
    }

    private Vector3 Eye => _target + _distance * new Vector3(MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Cos(_yaw));

    // Without this, clicks pass through the GL surface.
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        _panning = props.IsRightButtonPressed || props.IsMiddleButtonPressed;
        _dragStart = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start) return;
        var p = e.GetPosition(this);
        float dx = (float)(p.X - start.X), dy = (float)(p.Y - start.Y);
        _dragStart = p;
        if (_panning)
        {
            var forward = Vector3.Normalize(_target - Eye);
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            var up = Vector3.Cross(right, forward);
            float perPixel = 2 * _distance * MathF.Tan(FieldOfView / 2) / (float)Math.Max(Bounds.Height, 1);
            _target += (-dx * right + dy * up) * perPixel;
        }
        else
        {
            _yaw -= dx * 0.01f;
            _pitch = Math.Clamp(_pitch + dy * 0.01f, -1.5f, 1.5f);
        }
        RequestNextFrameRendering();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStart = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _distance = Math.Clamp(_distance * MathF.Pow(0.88f, (float)e.Delta.Y), _sceneRadius * 0.05f, _sceneRadius * 50f);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private const int GL_TRIANGLES = 0x0004, GL_LINES = 0x0001, GL_ARRAY_BUFFER = 0x8892, GL_ELEMENT_ARRAY_BUFFER = 0x8893,
        GL_STATIC_DRAW = 0x88E4, GL_FLOAT = 0x1406, GL_UNSIGNED_INT = 0x1405, GL_DEPTH_TEST = 0x0B71, GL_CULL_FACE = 0x0B44,
        GL_BLEND = 0x0BE2, GL_POLYGON_OFFSET_FILL = 0x8037, GL_COLOR_BUFFER_BIT = 0x4000, GL_DEPTH_BUFFER_BIT = 0x0100,
        GL_LEQUAL = 0x0203, GL_SRC_ALPHA = 0x0302, GL_ONE_MINUS_SRC_ALPHA = 0x0303, GL_VERTEX_SHADER = 0x8B31,
        GL_FRAGMENT_SHADER = 0x8B30, GL_BACK = 0x0405, GL_RGBA = 0x1908, GL_UNSIGNED_BYTE = 0x1401,
        GL_TEXTURE_2D = 0x0DE1, GL_TEXTURE0 = 0x84C0, GL_TEXTURE_MIN_FILTER = 0x2801, GL_TEXTURE_MAG_FILTER = 0x2800,
        GL_TEXTURE_WRAP_S = 0x2802, GL_TEXTURE_WRAP_T = 0x2803, GL_LINEAR = 0x2601, GL_LINEAR_MIPMAP_LINEAR = 0x2703, GL_REPEAT = 0x2901;

    private const int AttrPosition = 0, AttrNormal = 1, AttrUv = 2;
    private const int VertexFloats = 8; // position, normal, uv

    private string ShaderHeader => GlVersion.Type == GlProfileType.OpenGLES
        ? "#version 300 es\nprecision highp float;\n"
        : "#version 330 core\n";

    private const string SolidVertex = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUv;
uniform mat4 uMvp;
out vec3 vNormal;
out vec3 vWorld;
out vec2 vUv;
void main() { vNormal = aNormal; vWorld = aPos; vUv = aUv; gl_Position = uMvp * vec4(aPos, 1.0); }";

    private const string SolidFragment = @"
in vec3 vNormal;
in vec3 vWorld;
in vec2 vUv;
uniform vec3 uColor;
uniform sampler2D uTex;
uniform int uUseTexture;
uniform int uCutout;
uniform vec3 uEye;
uniform vec3 uSky;
uniform vec3 uGround;
out vec4 fragColor;
void main() {
    vec3 base = uColor;
    if (uUseTexture == 1) {
        vec4 t = texture(uTex, vUv);
        if (uCutout == 1 && t.a < 0.35) discard;
        base = t.rgb;
    }
    vec3 n = normalize(vNormal);
    if (!gl_FrontFacing) n = -n;
    vec3 hemi = mix(uGround, uSky, n.y * 0.5 + 0.5) * 0.55;
    vec3 key = vec3(0.75) * max(dot(n, normalize(vec3(1.0, 2.0, 3.0))), 0.0);
    vec3 rim = vec3(0.616, 0.722, 1.0) * 0.35 * max(dot(n, normalize(vec3(-2.0, 1.0, -2.0))), 0.0);
    vec3 v = normalize(uEye - vWorld);
    vec3 h = normalize(v + normalize(vec3(1.0, 2.0, 3.0)));
    float spec = pow(max(dot(n, h), 0.0), 24.0) * 0.08;
    fragColor = vec4(min(base * (hemi + key + rim) + spec, vec3(1.0)), 1.0);
}";

    private const string LineVertex = @"
layout(location = 0) in vec3 aPos;
uniform mat4 uMvp;
void main() { gl_Position = uMvp * vec4(aPos, 1.0); }";

    private const string LineFragment = @"
uniform vec4 uColor;
out vec4 fragColor;
void main() { fragColor = uColor; }";

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        _glPolygonOffset = (delegate* unmanaged<float, float, void>)gl.GetProcAddress("glPolygonOffset");
        _glBlendFunc = (delegate* unmanaged<int, int, void>)gl.GetProcAddress("glBlendFunc");
        _glCullFace = (delegate* unmanaged<int, void>)gl.GetProcAddress("glCullFace");
        _glUniform3f = (delegate* unmanaged<int, float, float, float, void>)gl.GetProcAddress("glUniform3f");
        _glUniform4f = (delegate* unmanaged<int, float, float, float, float, void>)gl.GetProcAddress("glUniform4f");
        _glReadPixels = (delegate* unmanaged<int, int, int, int, int, int, void*, void>)gl.GetProcAddress("glReadPixels");
        _glDisableVertexAttribArray = (delegate* unmanaged<int, void>)gl.GetProcAddress("glDisableVertexAttribArray");
        _glGenerateMipmap = (delegate* unmanaged<int, void>)gl.GetProcAddress("glGenerateMipmap");
        if (_glUniform3f == null || _glUniform4f == null || _glBlendFunc == null || _glDisableVertexAttribArray == null)
        {
            Backend.Services.AppLogger.LogWarning("3D preview: OpenGL functions are missing");
            return;
        }

        _solidProgram = BuildProgram(gl, SolidVertex, SolidFragment, withNormal: true);
        _lineProgram = BuildProgram(gl, LineVertex, LineFragment, withNormal: false);
        if (gl.IsGenVertexArraysAvailable) _vao = gl.GenVertexArray();
        _glReady = _solidProgram != 0 && _lineProgram != 0;
    }

    private int BuildProgram(GlInterface gl, string vertex, string fragment, bool withNormal)
    {
        int vs = gl.CreateShader(GL_VERTEX_SHADER);
        int fs = gl.CreateShader(GL_FRAGMENT_SHADER);
        string? error = gl.CompileShaderAndGetError(vs, ShaderHeader + vertex) ?? gl.CompileShaderAndGetError(fs, ShaderHeader + fragment);
        int program = 0;
        if (error == null)
        {
            program = gl.CreateProgram();
            gl.AttachShader(program, vs);
            gl.AttachShader(program, fs);
            gl.BindAttribLocationString(program, AttrPosition, "aPos");
            if (withNormal)
            {
                gl.BindAttribLocationString(program, AttrNormal, "aNormal");
                gl.BindAttribLocationString(program, AttrUv, "aUv");
            }
            error = gl.LinkProgramAndGetError(program);
            if (error != null) { gl.DeleteProgram(program); program = 0; }
        }
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        if (error != null) Backend.Services.AppLogger.LogWarning($"3D preview shader failed ({GlVersion}): {error}");
        return program;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        FreeMeshes(gl);
        if (_gridVbo != 0) gl.DeleteBuffer(_gridVbo);
        if (_glTexture != 0) gl.DeleteTexture(_glTexture);
        _glTexture = 0;
        _uploadedTexture = null;
        foreach (var id in _meshTextures.Values) gl.DeleteTexture(id);
        _meshTextures.Clear();
        if (_vao != 0 && gl.IsDeleteVertexArraysAvailable) gl.DeleteVertexArray(_vao);
        if (_solidProgram != 0) gl.DeleteProgram(_solidProgram);
        if (_lineProgram != 0) gl.DeleteProgram(_lineProgram);
        _gridVbo = _vao = _solidProgram = _lineProgram = 0;
        _uploaded = null;
        _glReady = false;
        base.OnOpenGlDeinit(gl);
    }

    private void FreeMeshes(GlInterface gl)
    {
        foreach (var m in _gpu)
        {
            gl.DeleteBuffer(m.Vbo);
            gl.DeleteBuffer(m.Ibo);
            if (m.WireIbo != 0) gl.DeleteBuffer(m.WireIbo);
        }
        _gpu.Clear();
    }

    private unsafe void Upload(GlInterface gl, IReadOnlyList<MeshPreviewItem>? meshes)
    {
        FreeMeshes(gl);
        _uploaded = meshes;
        if (meshes == null) return;
        foreach (var m in meshes)
        {
            if (m.Indices.Length < 3 || m.Positions.Length < 9) continue;
            int count = m.VertexCount;
            var interleaved = new float[count * VertexFloats];
            for (int v = 0; v < count; v++)
            {
                int o = v * VertexFloats;
                interleaved[o] = m.Positions[v * 3]; interleaved[o + 1] = m.Positions[v * 3 + 1]; interleaved[o + 2] = m.Positions[v * 3 + 2];
                if (m.Normals.Length >= (v + 1) * 3)
                {
                    interleaved[o + 3] = m.Normals[v * 3]; interleaved[o + 4] = m.Normals[v * 3 + 1]; interleaved[o + 5] = m.Normals[v * 3 + 2];
                }
                if (m.Uvs != null && m.Uvs.Length >= (v + 1) * 2)
                {
                    interleaved[o + 6] = m.Uvs[v * 2]; interleaved[o + 7] = m.Uvs[v * 2 + 1];
                }
            }
            var gpu = new GpuMesh
            {
                Vbo = gl.GenBuffer(),
                Ibo = gl.GenBuffer(),
                IndexCount = m.Indices.Length - m.Indices.Length % 3,
                Color = new Vector3(m.Color.R / 255f, m.Color.G / 255f, m.Color.B / 255f),
                DoubleSided = m.IsDoubleSided,
                AllowCutout = m.AllowCutout,
                Texture = m.Texture,
            };
            if (m.Texture != null && !_meshTextures.ContainsKey(m.Texture))
            {
                int id = FillTexture(gl, 0, m.Texture);
                if (id != 0) _meshTextures[m.Texture] = id;
            }
            gl.BindBuffer(GL_ARRAY_BUFFER, gpu.Vbo);
            fixed (float* p = interleaved) gl.BufferData(GL_ARRAY_BUFFER, interleaved.Length * sizeof(float), (IntPtr)p, GL_STATIC_DRAW);
            gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, gpu.Ibo);
            fixed (uint* p = m.Indices) gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, gpu.IndexCount * sizeof(uint), (IntPtr)p, GL_STATIC_DRAW);

            var edges = UniqueEdges(m.Indices, gpu.IndexCount);
            gpu.WireIbo = gl.GenBuffer();
            gpu.WireCount = edges.Length;
            gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, gpu.WireIbo);
            fixed (uint* p = edges) gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, edges.Length * sizeof(uint), (IntPtr)p, GL_STATIC_DRAW);
            _gpu.Add(gpu);
        }

        foreach (var unused in _meshTextures.Keys.Where(t => !meshes.Any(m => ReferenceEquals(m.Texture, t))).ToList())
        {
            gl.DeleteTexture(_meshTextures[unused]);
            _meshTextures.Remove(unused);
        }
    }

    private static uint[] UniqueEdges(uint[] indices, int count)
    {
        var seen = new HashSet<ulong>();
        var list = new List<uint>(count * 2);
        for (int i = 0; i + 2 < count; i += 3)
        {
            for (int j = 0; j < 3; j++)
            {
                uint a = indices[i + j], b = indices[i + (j + 1) % 3];
                ulong key = a < b ? ((ulong)a << 32) | b : ((ulong)b << 32) | a;
                if (seen.Add(key)) { list.Add(a); list.Add(b); }
            }
        }
        return list.ToArray();
    }

    private unsafe void UploadGrid(GlInterface gl)
    {
        if (_gridVbo != 0) return;
        var lines = new List<float>();
        for (int i = 0; i <= 20; i++)
        {
            float t = -1f + i * 0.1f;
            lines.AddRange(new[] { t, 0f, -1f, t, 0f, 1f, -1f, 0f, t, 1f, 0f, t });
        }
        var data = lines.ToArray();
        _gridVbo = gl.GenBuffer();
        _gridCount = data.Length / 3;
        gl.BindBuffer(GL_ARRAY_BUFFER, _gridVbo);
        fixed (float* p = data) gl.BufferData(GL_ARRAY_BUFFER, data.Length * sizeof(float), (IntPtr)p, GL_STATIC_DRAW);
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)(Bounds.Width * scaling)), height = Math.Max(1, (int)(Bounds.Height * scaling));
        gl.Viewport(0, 0, width, height);

        var bg = (Background as ISolidColorBrush)?.Color ?? Color.FromRgb(0x1b, 0x1d, 0x22);
        gl.ClearColor(bg.R / 255f, bg.G / 255f, bg.B / 255f, 1f);
        gl.ClearDepth(1f);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        if (!_glReady) return;

        var meshes = Meshes;
        if (!ReferenceEquals(meshes, _uploaded)) Upload(gl, meshes);
        var texture = Texture;
        if (!ReferenceEquals(texture, _uploadedTexture)) UploadTexture(gl, texture);
        if (_vao != 0) gl.BindVertexArray(_vao);

        var eye = Eye;
        float near = MathF.Max(_distance * 0.01f, 1e-4f), far = _distance + _sceneRadius * 6f;
        var view = Matrix4x4.CreateLookAt(eye, _target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, width / (float)height, near, far);
        var mvp = view * proj;

        gl.Enable(GL_DEPTH_TEST);
        gl.DepthFunc(GL_LEQUAL);
        gl.DepthMask(1);
        gl.Disable(GL_BLEND);

        gl.UseProgram(_solidProgram);
        gl.UniformMatrix4fv(gl.GetUniformLocationString(_solidProgram, "uMvp"), 1, false, &mvp);
        _glUniform3f(gl.GetUniformLocationString(_solidProgram, "uEye"), eye.X, eye.Y, eye.Z);
        _glUniform3f(gl.GetUniformLocationString(_solidProgram, "uSky"), SkyColor.X, SkyColor.Y, SkyColor.Z);
        _glUniform3f(gl.GetUniformLocationString(_solidProgram, "uGround"), GroundColor.X, GroundColor.Y, GroundColor.Z);
        int colorLoc = gl.GetUniformLocationString(_solidProgram, "uColor");
        bool textured = texture != null && _glTexture != 0;
        int useTextureLoc = gl.GetUniformLocationString(_solidProgram, "uUseTexture");
        int cutoutLoc = gl.GetUniformLocationString(_solidProgram, "uCutout");
        bool cutout = textured && texture!.Cutout;
        gl.Uniform1i(gl.GetUniformLocationString(_solidProgram, "uTex"), 0);
        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, textured ? _glTexture : 0);
        gl.Enable(GL_POLYGON_OFFSET_FILL);
        if (_glPolygonOffset != null) _glPolygonOffset(1f, 1f);
        foreach (var m in _gpu)
        {
            if (m.DoubleSided && _glCullFace != null) { gl.Enable(GL_CULL_FACE); _glCullFace(GL_BACK); }
            else gl.Disable(GL_CULL_FACE);
            _glUniform3f(colorLoc, m.Color.X, m.Color.Y, m.Color.Z);
            if (m.Texture != null && _meshTextures.TryGetValue(m.Texture, out int own))
            {
                gl.Uniform1i(useTextureLoc, 1);
                gl.BindTexture(GL_TEXTURE_2D, own);
                gl.Uniform1i(cutoutLoc, m.Texture.Cutout && m.AllowCutout ? 1 : 0);
            }
            else
            {
                gl.Uniform1i(useTextureLoc, textured ? 1 : 0);
                gl.BindTexture(GL_TEXTURE_2D, textured ? _glTexture : 0);
                gl.Uniform1i(cutoutLoc, cutout && m.AllowCutout ? 1 : 0);
            }
            BindAttributes(gl, m.Vbo, withNormal: true);
            gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, m.Ibo);
            gl.DrawElements(GL_TRIANGLES, m.IndexCount, GL_UNSIGNED_INT, IntPtr.Zero);
        }
        gl.Disable(GL_POLYGON_OFFSET_FILL);
        gl.Disable(GL_CULL_FACE);

        gl.Enable(GL_BLEND);
        _glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        gl.UseProgram(_lineProgram);
        int lineMvp = gl.GetUniformLocationString(_lineProgram, "uMvp");
        int lineColor = gl.GetUniformLocationString(_lineProgram, "uColor");
        gl.UniformMatrix4fv(lineMvp, 1, false, &mvp);
        if (ShowWireframe)
        {
            _glUniform4f(lineColor, 0.043f, 0.047f, 0.059f, 0.35f);
            foreach (var m in _gpu)
            {
                BindAttributes(gl, m.Vbo, withNormal: false);
                gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, m.WireIbo);
                gl.DrawElements(GL_LINES, m.WireCount, GL_UNSIGNED_INT, IntPtr.Zero);
            }
        }
        if (ShowGrid && _hasFramedBounds)
        {
            UploadGrid(gl);
            var size = _boundsMax - _boundsMin;
            float scale = MathF.Max(MathF.Max(size.X, MathF.Max(size.Y, size.Z)), 0.5f);
            var center = (_boundsMin + _boundsMax) / 2;
            var gridMvp = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(center.X, _boundsMin.Y, center.Z) * mvp;
            gl.UniformMatrix4fv(lineMvp, 1, false, &gridMvp);
            bool light = bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114 > 140;
            if (light) _glUniform4f(lineColor, 0f, 0f, 0f, 0.12f);
            else _glUniform4f(lineColor, 1f, 1f, 1f, 0.08f);
            BindAttributes(gl, _gridVbo, withNormal: false, stride: 3);
            gl.DrawArrays(GL_LINES, 0, _gridCount);
        }
        gl.Disable(GL_BLEND);
        gl.BindTexture(GL_TEXTURE_2D, 0);
        gl.UseProgram(0);

        OnFrameRendered(gl, width, height);
    }

    private static void BindAttributes(GlInterface gl, int vbo, bool withNormal, int stride = VertexFloats)
    {
        gl.BindBuffer(GL_ARRAY_BUFFER, vbo);
        gl.VertexAttribPointer(AttrPosition, 3, GL_FLOAT, 0, stride * sizeof(float), IntPtr.Zero);
        gl.EnableVertexAttribArray(AttrPosition);
        if (withNormal)
        {
            gl.VertexAttribPointer(AttrNormal, 3, GL_FLOAT, 0, stride * sizeof(float), 3 * sizeof(float));
            gl.EnableVertexAttribArray(AttrNormal);
            gl.VertexAttribPointer(AttrUv, 2, GL_FLOAT, 0, stride * sizeof(float), 6 * sizeof(float));
            gl.EnableVertexAttribArray(AttrUv);
        }
        else
        {
            unsafe { _glDisableVertexAttribArray(AttrNormal); _glDisableVertexAttribArray(AttrUv); }
        }
    }

    private void UploadTexture(GlInterface gl, MeshPreviewTexture? texture)
    {
        _uploadedTexture = texture;
        if (texture == null) return;
        _glTexture = FillTexture(gl, _glTexture, texture);
    }

    private unsafe int FillTexture(GlInterface gl, int id, MeshPreviewTexture texture)
    {
        if (texture.Rgba.Length < texture.Width * texture.Height * 4) return id;
        if (id == 0) id = gl.GenTexture();
        gl.BindTexture(GL_TEXTURE_2D, id);
        fixed (byte* p = texture.Rgba)
        {
            gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, texture.Width, texture.Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, (IntPtr)p);
        }
        bool mipmaps = _glGenerateMipmap != null;
        if (mipmaps) _glGenerateMipmap(GL_TEXTURE_2D);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, mipmaps ? GL_LINEAR_MIPMAP_LINEAR : GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
        gl.BindTexture(GL_TEXTURE_2D, 0);
        return id;
    }

    protected virtual void OnFrameRendered(GlInterface gl, int width, int height) { }

    protected unsafe byte[] ReadPixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        if (_glReadPixels == null) return pixels;
        fixed (byte* p = pixels) _glReadPixels(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, p);
        return pixels;
    }
}
