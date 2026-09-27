using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IconPacks.Avalonia.BoxIcons;
using PlumbobForge.Backend.Database;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ConfigSetItemViewModel : ObservableObject
{
    private readonly SetsEntity _entity;

    public long Id => _entity.Id;
    public string Name => _entity.Name;
    public long? ParentSetsEntityId => _entity.ParentSetsEntityId;
    public SetsEntity Entity => _entity;

    [ObservableProperty]
    private string _breadcrumbPath = string.Empty;

    [ObservableProperty]
    private int _descendantCount = 0;

    [ObservableProperty]
    private bool _isSelected = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    private bool _isEnabledInConfig = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    private string _icon = "RegularFolder";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    private string? _color;

    public PackIconBoxIconsKind IconKind =>
        Enum.TryParse<PackIconBoxIconsKind>(Icon, out var kind) ? kind : PackIconBoxIconsKind.RegularFolder;

    public bool HasCustomColor => !string.IsNullOrEmpty(Color) && Color != "#10b981" && Color != "#34d399";

    public IBrush IconBrush =>
        HasCustomColor
            ? Brush.Parse(Color!)
            : (IsEnabledInConfig ? ThemeService.GetCurrentAccentBrush() : Brush.Parse("#94a3b8"));

    public bool HasBreadcrumb => !string.IsNullOrWhiteSpace(BreadcrumbPath);
    public bool HasDescendants => DescendantCount > 0;

    public ConfigSetItemViewModel(SetsEntity entity, string breadcrumbPath = "", int descendantCount = 0, bool isEnabledInConfig = false)
    {
        _entity = entity;
        _breadcrumbPath = breadcrumbPath;
        _descendantCount = descendantCount;
        _isEnabledInConfig = isEnabledInConfig;
        _icon = string.IsNullOrWhiteSpace(entity.Icon) ? "RegularFolder" : entity.Icon;
        _color = string.IsNullOrWhiteSpace(entity.Color) ? null : entity.Color;
        ThemeService.AccentChanged += () => OnPropertyChanged(nameof(IconBrush));
    }
}
