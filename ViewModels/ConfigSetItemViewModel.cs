using System;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PlumbobForge.Desktop.Controls.Icons;
using PlumbobForge.Backend.Database;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ConfigSetItemViewModel : ConfigColumnEntry
{
    private readonly SetsEntity _entity;

    public long Id => _entity.Id;
    public string Name => _entity.Name;
    public long? ParentSetsEntityId => _entity.ParentSetsEntityId;
    public SetsEntity Entity => _entity;

    // A group row is shown in a column it has no membership of: it belongs to the column that shows it.
    public override bool IsInEnabledColumn => IsGroupOnly ? ColumnEnabled : IsEnabledInConfig;
    public override string SortName => Name;
    public override string SearchText => $"{Name} {BreadcrumbPath}";

    [ObservableProperty]
    private string _breadcrumbPath = string.Empty;

    [ObservableProperty]
    private int _descendantCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    private bool _isEnabledInConfig = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    private string _icon = "RegularFolder";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    [NotifyPropertyChangedFor(nameof(HasCustomColor))]
    private string? _color;

    public PackIconBoxIconsKind IconKind =>
        Enum.TryParse<PackIconBoxIconsKind>(Icon, out var kind) ? kind : PackIconBoxIconsKind.RegularFolder;

    public bool HasCustomColor => !string.IsNullOrEmpty(Color);

    public IBrush IconBrush =>
        HasCustomColor
            ? Brush.Parse(Color!)
            : (IsEnabledInConfig ? ThemeService.GetCurrentAccentBrush() : Brush.Parse("#94a3b8"));

    // Place in the column's tree.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowMargin))]
    private int _depth;

    [ObservableProperty]
    private bool _isGroupOnly;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVisibleChildren))]
    private int _visibleChildCount;

    public bool ColumnEnabled { get; set; }
    public bool HasVisibleChildren => VisibleChildCount > 0;
    public Thickness RowMargin => new(Depth * 16, 0, 0, 2);

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
        ThemeService.AccentChanged += () =>
        {
            OnPropertyChanged(nameof(IconBrush));
            OnPropertyChanged(nameof(HasCustomColor));
        };
    }
}
