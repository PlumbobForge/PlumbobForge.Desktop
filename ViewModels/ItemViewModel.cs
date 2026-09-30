using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using SkiaSharp;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ItemViewModel : ObservableObject
{
    private readonly MetaEntity _entity;

    public long Id => _entity.Id;
    public string FileName => _entity.FileName;
    public double FileSize => _entity.FileSize;
    public string PackageType => _entity.PackageType;
    public bool Enabled => _entity.Enabled;
    public bool IsFavorite => _entity.IsFavorite;

    private static readonly IBrush BrushFavoriteGold = new SolidColorBrush(Color.Parse("#f59e0b"));
    private static readonly IBrush BrushFavoriteBgActive = new SolidColorBrush(Color.Parse("#40f59e0b"));
    private static readonly IBrush BrushFavoriteBorderActive = new SolidColorBrush(Color.Parse("#f59e0b"));
    private static readonly IBrush BrushFavoriteBorderInactive = new SolidColorBrush(Color.Parse("#40ffffff"));

    public IBrush FavoriteBadgeBackground => IsFavorite ? BrushFavoriteBgActive : BrushSemiBlack;
    public IBrush FavoriteBadgeBorder => IsFavorite ? BrushFavoriteBorderActive : BrushFavoriteBorderInactive;
    public IBrush FavoriteStarForeground => IsFavorite ? BrushFavoriteGold : BrushWhite;
    public string? UserTags => _entity.UserTags;
    public MetaEntity Entity => _entity;

    private static IBrush BrushGreen => ThemeService.GetCurrentAccentBrush();
    private static readonly IBrush BrushRed = new SolidColorBrush(Color.Parse("#ef4444"));
    private static readonly IBrush BrushSemiBlack = new SolidColorBrush(Color.Parse("#60000000"));
    private static IBrush BrushBorderMuted => ThemeService.GetCapsuleBorderBrush();
    private static readonly IBrush BrushWhite = new SolidColorBrush(Color.Parse("#ffffff"));
    private static readonly Thickness ThicknessSelected = new(2);
    private static readonly Thickness ThicknessNormal = new(1);

    public static double GlobalThumbnailHeight { get; set; } = 180;
    public double ThumbnailHeight => GlobalThumbnailHeight;

    public void NotifyThumbnailHeightChanged()
    {
        OnPropertyChanged(nameof(ThumbnailHeight));
    }

    public string FileSizeFormatted => FileSize > 1024 * 1024 ? $"{FileSize / (1024.0 * 1024.0):F2} MB" : $"{FileSize / 1024.0:F2} KB";
    public string FileSizeText => FileSizeFormatted;
    public string StatusText => Enabled ? "Enabled" : "Disabled";
    public IBrush StatusColor => Enabled ? BrushGreen : BrushRed;
    public IBrush EnableIconColor => Enabled ? BrushGreen : BrushRed;
    public IBrush SelectionBadgeBackground => IsSelected ? BrushGreen : BrushSemiBlack;
    public IBrush CardBorderBrush => IsSelected ? BrushGreen : BrushBorderMuted;
    public Thickness CardBorderThickness => IsSelected ? ThicknessSelected : ThicknessNormal;

    public TextDecorationCollection? FileNameTextDecoration => Enabled ? null : TextDecorations.Strikethrough;

    public string CategoryTagDisplay
    {
        get
        {
            var type = PackageType ?? "";
            if (type.Equals("CAS", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(_entity.CASCategories))
                {
                    return _entity.CASCategories;
                }
                return "CAS";
            }
            if (type.Equals("BuildBuy", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("Build/Buy", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("Object", StringComparison.OrdinalIgnoreCase))
            {
                return "Build-Buy";
            }
            if (type.Equals("World", StringComparison.OrdinalIgnoreCase) || FileName.EndsWith(".world", StringComparison.OrdinalIgnoreCase))
            {
                return "World";
            }
            if (type.Equals("Sim", StringComparison.OrdinalIgnoreCase) || FileName.EndsWith(".sim", StringComparison.OrdinalIgnoreCase))
            {
                return "Sim";
            }
            if (type.Equals("Lot", StringComparison.OrdinalIgnoreCase))
            {
                return "Lot";
            }
            if (type.Equals("Pattern", StringComparison.OrdinalIgnoreCase))
            {
                return "Pattern";
            }
            return string.IsNullOrWhiteSpace(PackageType) ? "Other" : PackageType;
        }
    }

    public IBrush CategoryTagForeground => BrushWhite;

    private IReadOnlyList<string>? _cachedTagsList;
    public IReadOnlyList<string> TagsList => _cachedTagsList ??= (!string.IsNullOrWhiteSpace(_entity.UserTags)
        ? _entity.UserTags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : Array.Empty<string>());

    public bool HasUserTags => TagsList.Count > 0;

    [ObservableProperty]
    private bool _isTagsExpanded = false;

    public IEnumerable<string> VisibleTags => IsTagsExpanded ? TagsList : TagsList.Take(3);
    public int RemainingTagsCount => Math.Max(0, TagsList.Count - 3);
    public bool HasRemainingTags => !IsTagsExpanded && RemainingTagsCount > 0;
    public string RemainingTagsText => $"+{RemainingTagsCount}";

    [RelayCommand]
    public void ToggleTagsExpanded()
    {
        IsTagsExpanded = !IsTagsExpanded;
        OnPropertyChanged(nameof(VisibleTags));
        OnPropertyChanged(nameof(HasRemainingTags));
    }

    public string CasCategoryDisplay => !string.IsNullOrWhiteSpace(_entity.CASCategories)
        ? _entity.CASCategories
        : (string.IsNullOrWhiteSpace(PackageType) ? "Details" : PackageType);
    public string AddedDateDisplay => !string.IsNullOrWhiteSpace(_entity.InstallDate) ? $"Added: {_entity.InstallDate}" : "Added: 12.08.2026 19:03:53";

    public Action? OnSelectionChanged { get; set; }

    public static ThumbnailService? GlobalThumbnailService { get; set; }
    public static string? GlobalThumbnailDirectory { get; set; }
    private string? _cachedThumbnailPath;

    public double ThumbnailOpacity => Enabled ? 1.0 : 0.75;

    public Bitmap? DisplayThumbnailBitmap
    {
        get
        {
            var bmp = ThumbnailCache.TryGet(Id, grayscale: !Enabled);
            if (bmp == null && !IsLoadingThumbnail)
            {
                _ = LoadThumbnailAsync();
            }
            return bmp;
        }
    }

    public Bitmap? ThumbnailBitmap => DisplayThumbnailBitmap;

    [ObservableProperty]
    private bool _isLoadingThumbnail = false;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isInfoExpanded;

    [RelayCommand]
    public void ToggleInfoExpanded()
    {
        IsInfoExpanded = !IsInfoExpanded;
    }

    [ObservableProperty]
    private string _note = string.Empty;

    public static Action<ItemViewModel, string?>? OnNoteChangedCallback { get; set; }

    private System.Threading.CancellationTokenSource? _saveNoteCts;

    partial void OnNoteChanged(string value)
    {
        _entity.Description = string.IsNullOrWhiteSpace(value) ? null : value;
        _saveNoteCts?.Cancel();
        _saveNoteCts?.Dispose();
        _saveNoteCts = new System.Threading.CancellationTokenSource();
        var token = _saveNoteCts.Token;

        Task.Delay(350, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                OnNoteChangedCallback?.Invoke(this, _entity.Description);
            }
        }, TaskScheduler.Default);
    }

    public static Action<ItemViewModel>? OnRenameRequested { get; set; }
    public static Action<ItemViewModel>? OnEditTagsRequested { get; set; }
    public static Action<ItemViewModel>? OnToggleEnableRequested { get; set; }
    public static Action<ItemViewModel>? OnToggleFavoriteRequested { get; set; }
    public static Action<ItemViewModel>? OnShowDetailsRequested { get; set; }

    [RelayCommand]
    public void Rename() => OnRenameRequested?.Invoke(this);

    [RelayCommand]
    public void EditTags() => OnEditTagsRequested?.Invoke(this);

    [RelayCommand]
    public void ToggleEnable() => OnToggleEnableRequested?.Invoke(this);

    [RelayCommand]
    public void ToggleFavorite()
    {
        _entity.IsFavorite = !_entity.IsFavorite;
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteBadgeBackground));
        OnPropertyChanged(nameof(FavoriteBadgeBorder));
        OnPropertyChanged(nameof(FavoriteStarForeground));
        OnToggleFavoriteRequested?.Invoke(this);
    }

    [RelayCommand]
    public void ShowDetails() => OnShowDetailsRequested?.Invoke(this);

    [RelayCommand]
    public void ToggleSelection()
    {
        IsSelected = !IsSelected;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
        OnPropertyChanged(nameof(SelectionBadgeBackground));
        OnSelectionChanged?.Invoke();
    }

    public void NotifyEntityChanged()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(PackageType));
        OnPropertyChanged(nameof(UserTags));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(CategoryTagDisplay));
        OnPropertyChanged(nameof(CategoryTagForeground));
        OnPropertyChanged(nameof(TagsList));
        OnPropertyChanged(nameof(HasUserTags));
        OnPropertyChanged(nameof(VisibleTags));
        OnPropertyChanged(nameof(HasRemainingTags));
        OnPropertyChanged(nameof(RemainingTagsText));
        OnPropertyChanged(nameof(CasCategoryDisplay));
        OnPropertyChanged(nameof(AddedDateDisplay));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(EnableIconColor));
        OnPropertyChanged(nameof(FileNameTextDecoration));
        OnPropertyChanged(nameof(ThumbnailOpacity));
        OnPropertyChanged(nameof(DisplayThumbnailBitmap));
        OnPropertyChanged(nameof(ThumbnailBitmap));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteBadgeBackground));
        OnPropertyChanged(nameof(FavoriteBadgeBorder));
        OnPropertyChanged(nameof(FavoriteStarForeground));
        Note = _entity.Description ?? string.Empty;
        OnPropertyChanged(nameof(Note));
    }

    [RelayCommand]
    public void SearchGoogle()
    {
        try
        {
            var url = $"https://www.google.com/search?q={Uri.EscapeDataString(FileName)}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public void OpenInExplorer()
    {
        try
        {
            if (File.Exists(Entity.CompleteFileName))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{Entity.CompleteFileName}\"",
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    public ItemViewModel(MetaEntity entity)
    {
        _entity = entity;
        _note = entity.Description ?? string.Empty;
    }

    private static readonly System.Threading.SemaphoreSlim _thumbnailSemaphore = new(4, 4);

    public void UnloadThumbnail()
    {
        // Persistent cache keeps decoded 140px bitmaps for 0-allocation smooth scrolling
    }

    public async Task LoadThumbnailAsync(ThumbnailService? thumbnailService = null)
    {
        if (ThumbnailCache.TryGet(Id, grayscale: !Enabled) != null || IsLoadingThumbnail) return;

        IsLoadingThumbnail = true;
        try
        {
            string? path = _cachedThumbnailPath;
            if (string.IsNullOrEmpty(path))
            {
                if (!string.IsNullOrEmpty(GlobalThumbnailDirectory))
                {
                    var directPath = Path.Combine(GlobalThumbnailDirectory, $"{Id}.thumb");
                    if (File.Exists(directPath))
                    {
                        path = directPath;
                    }
                }
                if (string.IsNullOrEmpty(path))
                {
                    var service = thumbnailService ?? GlobalThumbnailService;
                    if (service != null)
                    {
                        path = await service.GetThumbnailPathAsync(Id);
                    }
                }
            }

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _cachedThumbnailPath = path;
                var bmp = await ThumbnailCache.GetOrLoadAsync(Id, path, grayscale: !Enabled);
                if (bmp != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        OnPropertyChanged(nameof(DisplayThumbnailBitmap));
                        OnPropertyChanged(nameof(ThumbnailBitmap));
                    });
                }
            }
        }
        catch
        {
            // Ignore
        }
        finally
        {
            IsLoadingThumbnail = false;
        }
    }
}
