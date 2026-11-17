using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Controls.MeshPreview;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Utils;
using S3ForgeTools.GameFiles.Package;

namespace PlumbobForge.Desktop.ViewModels;

// Read-only 3D preview of one CAS part or object, opened from Look Inside.
public sealed partial class MeshViewerViewModel : ObservableObject, IDisposable
{
    // Kept between openings.
    private static string? _chosenTexture;
    private static bool _wireframe;
    private static bool _transparency = true;
    private static bool _layersOpen;

    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    private readonly PackageInspection _inspection;
    private readonly TGI_Key _key;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _work = new(1, 1);
    private readonly Dictionary<InspectedResource, RgbaImage> _decoded = new();
    private readonly Dictionary<(int Preset, string Mode), (RgbaImage? Image, bool Cutout)> _textures = new();
    private Dictionary<CcMesh, (MeshPreviewTexture Texture, bool Cutout)>? _objectTextures;
    private CcAnalysis? _analysis;
    private List<CasPreset> _presets = new();
    private int _version;

    public string Title { get; }
    public bool CanOpenInOptimizer { get; }

    public ObservableCollection<CcLodOption> Lods { get; } = new();
    public ObservableCollection<CcTextureOption> TextureOptions { get; } = new();
    public ObservableCollection<CcTextureOption> MainTextureOptions { get; } = new();
    public ObservableCollection<CcTextureOption> TextureLayers { get; } = new();
    public ObservableCollection<CcPresetOption> Presets { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowViewer))]
    private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(ShowViewer))]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrianglesText))]
    private CcLodOption? _selectedLod;

    [ObservableProperty]
    private bool _showWireframe = _wireframe;

    [ObservableProperty]
    private bool _useTransparency = _transparency;

    [ObservableProperty]
    private bool _showLayers = _layersOpen;

    [ObservableProperty]
    private CcTextureOption? _selectedTexture;

    [ObservableProperty]
    private CcPresetOption? _selectedPreset;

    [ObservableProperty]
    private IReadOnlyList<MeshPreviewItem>? _previewMeshes;

    [ObservableProperty]
    private MeshPreviewTexture? _previewTexture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTransparencyToggle))]
    private bool _textureHasCutout;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool ShowViewer => !IsLoading && !HasError;
    public bool HasSeveralLods => Lods.Count > 1;
    public bool HasTextures => TextureOptions.Count > 1;
    public bool HasPresetChoice => Presets.Count > 1;
    public bool HasTextureLayers => TextureLayers.Count > 0;
    public bool ShowTransparencyToggle => TextureHasCutout;
    public string TrianglesText => SelectedLod == null ? string.Empty : L("mesh_viewer.triangles", SelectedLod.CountText);

    public MeshViewerViewModel(PackageInspection inspection, TGI_Key key, string title, bool canOpenInOptimizer)
    {
        _inspection = inspection;
        _key = key;
        Title = title;
        CanOpenInOptimizer = canOpenInOptimizer;
    }

    public async Task LoadAsync()
    {
        try
        {
            var (analysis, presets) = await Task.Run(() =>
            {
                var a = CcOptimizerService.Analyse(_inspection, _key, _cts.Token);
                return (a, a.ReadPresets(_inspection));
            }, _cts.Token);
            if (_cts.IsCancellationRequested) return;
            _analysis = analysis;
            _presets = presets;

            foreach (int lod in analysis.Lods)
            {
                int triangles = analysis.RealTrianglesIn(lod);
                if (triangles > 0) Lods.Add(new CcLodOption(lod, triangles));
            }
            if (Lods.Count == 0)
            {
                ErrorMessage = L("mesh_viewer.no_meshes");
                return;
            }
            OnPropertyChanged(nameof(HasSeveralLods));

            for (int i = 0; i < presets.Count; i++) Presets.Add(new CcPresetOption(i, presets[i]));
            OnPropertyChanged(nameof(HasPresetChoice));
            SelectPresetCore(Presets.FirstOrDefault());
            BuildTextureOptions();
            SelectLodCore(Lods[0]);
            IsLoading = false;
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"3D preview could not read {_key}", ex, "MeshViewerViewModel");
            ErrorMessage = L("mesh_viewer.error", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private CasPreset? CurrentPreset =>
        SelectedPreset != null && SelectedPreset.Index < _presets.Count ? _presets[SelectedPreset.Index] : null;

    private void BuildTextureOptions()
    {
        var options = CcPreview.TextureOptions(_inspection, _analysis, CurrentPreset);
        TextureOptions.Clear();
        MainTextureOptions.Clear();
        TextureLayers.Clear();
        foreach (var o in options)
        {
            TextureOptions.Add(o);
            (o.IsLayer ? TextureLayers : MainTextureOptions).Add(o);
        }
        OnPropertyChanged(nameof(HasTextureLayers));
        OnPropertyChanged(nameof(HasTextures));
        SelectTextureCore(CcPreview.Pick(options, _chosenTexture));
    }

    [RelayCommand]
    private void SelectLod(CcLodOption option)
    {
        SelectLodCore(option);
        _ = RefreshAsync();
    }

    private void SelectLodCore(CcLodOption option)
    {
        foreach (var l in Lods) l.IsSelected = l == option;
        SelectedLod = option;
    }

    [RelayCommand]
    private void SelectTexture(CcTextureOption option)
    {
        _chosenTexture = option.Mode;
        SelectTextureCore(option);
        _objectTextures = null;
        _ = RefreshAsync();
    }

    private void SelectTextureCore(CcTextureOption option)
    {
        foreach (var t in TextureOptions) t.IsSelected = t == option;
        SelectedTexture = option;
    }

    [RelayCommand]
    private void SelectPreset(CcPresetOption option)
    {
        SelectPresetCore(option);
        BuildTextureOptions();
        _objectTextures = null;
        _ = RefreshAsync();
    }

    private void SelectPresetCore(CcPresetOption? option)
    {
        foreach (var p in Presets) p.IsSelected = p == option;
        SelectedPreset = option;
    }

    partial void OnShowWireframeChanged(bool value) => _wireframe = value;

    // Hiding the layers while one is shown goes back to the in-game look.
    partial void OnShowLayersChanged(bool value)
    {
        _layersOpen = value;
        if (!value && SelectedTexture is { IsLayer: true }) SelectTexture(CcPreview.Pick(TextureOptions, CcMaterial.InGame));
    }

    partial void OnUseTransparencyChanged(bool value)
    {
        _transparency = value;
        _objectTextures = null;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var analysis = _analysis;
        var lod = SelectedLod;
        if (analysis == null || lod == null) return;
        int version = Interlocked.Increment(ref _version);
        IsWorking = true;
        try
        {
            await _work.WaitAsync(_cts.Token);
            try
            {
                if (version != _version) return;
                bool textured = SelectedTexture is { IsClay: false };
                bool transparency = UseTransparency;
                var decoded = _decoded;

                if (analysis.IsObject)
                {
                    if (!textured) _objectTextures = null;
                    else
                    {
                        string mode = SelectedTexture!.Mode;
                        var colours = CurrentPreset;
                        _objectTextures ??= await Task.Run(() => CcPreview.ObjectTextures(_inspection, analysis, decoded, transparency, mode, colours), _cts.Token);
                    }
                    TextureHasCutout = _objectTextures?.Values.Any(t => t.Cutout) == true;
                    PreviewTexture = null;
                }
                else
                {
                    await UpdateCasTextureAsync(analysis);
                }

                var objectTextures = analysis.IsObject ? _objectTextures ?? new() : null;
                var items = await Task.Run(() =>
                {
                    var shown = analysis.MeshesIn(lod.Lod).Where(m => !m.IsPlaceholder).ToList();
                    bool hasStrands = shown.Any(m => m.Geom.IsHairShader);
                    return shown.Select((m, i) => MeshPreviewItem.FromGeom(m.Geom, i, m.IsDoubleSided,
                        CcPreview.AllowCutout(m, hasStrands, objectTextures), CcPreview.OwnTexture(m, objectTextures))).ToList();
                }, _cts.Token);
                if (version != _version) return;
                PreviewMeshes = items;
            }
            finally
            {
                _work.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.LogError("3D preview failed", ex, "MeshViewerViewModel");
            ErrorMessage = L("mesh_viewer.error", ex.Message);
        }
        finally
        {
            if (version == _version) IsWorking = false;
        }
    }

    private async Task UpdateCasTextureAsync(CcAnalysis analysis)
    {
        var mode = SelectedTexture?.Mode;
        var preset = CurrentPreset;
        if (mode == null || mode == CcOptimizerViewModel.ClayMode || preset == null || SelectedPreset == null)
        {
            PreviewTexture = null;
            TextureHasCutout = false;
            return;
        }
        try
        {
            var cacheKey = (SelectedPreset.Index, mode);
            if (!_textures.TryGetValue(cacheKey, out var entry))
            {
                var decoded = _decoded;
                entry = await Task.Run(() => CcPreview.CasTexture(_inspection, analysis, preset, mode, decoded), _cts.Token);
                _textures[cacheKey] = entry;
            }
            TextureHasCutout = entry.Cutout;
            PreviewTexture = entry.Image == null ? null
                : new MeshPreviewTexture(entry.Image.Width, entry.Image.Height, entry.Image.Rgba, entry.Cutout && UseTransparency);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"3D preview could not build the {mode} texture", ex, "MeshViewerViewModel");
            PreviewTexture = null;
            TextureHasCutout = false;
        }
    }

    public void Dispose() => _cts.Cancel();
}
