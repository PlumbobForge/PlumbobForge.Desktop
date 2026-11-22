using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PlumbobForge.Desktop.Controls.Icons;
using PlumbobForge.Backend.Database;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

// A card in a configuration's columns that stands for a group of items: a set or a Mods folder.
public abstract partial class ConfigGroupEntry : ConfigColumnEntry
{
    public abstract string Name { get; }
    public abstract string DescendantText { get; }

    public override bool IsInEnabledColumn => IsEnabledInConfig;
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

    public bool HasBreadcrumb => !string.IsNullOrWhiteSpace(BreadcrumbPath);
    public bool HasDescendants => DescendantCount > 0;

    protected ConfigGroupEntry(string? icon, string? color, string breadcrumbPath, int descendantCount, bool isEnabledInConfig)
    {
        _breadcrumbPath = breadcrumbPath;
        _descendantCount = descendantCount;
        _isEnabledInConfig = isEnabledInConfig;
        _icon = string.IsNullOrWhiteSpace(icon) ? "RegularFolder" : icon;
        _color = string.IsNullOrWhiteSpace(color) ? null : color;
        ThemeService.AccentChanged += () =>
        {
            OnPropertyChanged(nameof(IconBrush));
            OnPropertyChanged(nameof(HasCustomColor));
        };
    }
}

public partial class ConfigSetItemViewModel : ConfigGroupEntry
{
    private readonly SetsEntity _entity;

    public long Id => _entity.Id;
    public override string Name => _entity.Name;
    public long? ParentSetsEntityId => _entity.ParentSetsEntityId;
    public SetsEntity Entity => _entity;
    public override string DescendantText => $"{DescendantCount} sub-sets";

    public ConfigSetItemViewModel(SetsEntity entity, string breadcrumbPath = "", int descendantCount = 0, bool isEnabledInConfig = false)
        : base(entity.Icon, entity.Color, breadcrumbPath, descendantCount, isEnabledInConfig)
    {
        _entity = entity;
    }
}

public partial class ConfigModsFolderViewModel : ConfigGroupEntry
{
    private readonly ModsFolderEntity _entity;

    public long Id => _entity.Id;
    public override string Name => _entity.Name;
    public long? ParentModsFolderId => _entity.ParentModsFolderId;
    public string RelativePath => _entity.RelativePath;
    public override bool IsModsEntry => true;
    public override string DescendantText => LocalizationManager.Instance.GetString("configurations.mods_subfolders", DescendantCount);

    public ConfigModsFolderViewModel(ModsFolderEntity entity, string breadcrumbPath, int descendantCount, bool isEnabledInConfig)
        : base(entity.Icon, entity.Color, breadcrumbPath, descendantCount, isEnabledInConfig)
    {
        _entity = entity;
    }
}
