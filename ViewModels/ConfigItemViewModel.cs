using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IconPacks.Avalonia.BoxIcons;
using PlumbobForge.Backend.Database;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ConfigItemViewModel : ObservableObject
{
    private readonly ConfigEntity _entity;

    public long Id => _entity.Id;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    [NotifyPropertyChangedFor(nameof(BadgeBackground))]
    [NotifyPropertyChangedFor(nameof(BadgeForeground))]
    [NotifyPropertyChangedFor(nameof(BadgeBorderBrush))]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    private bool _active;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    private string _icon = "RegularSlider";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    [NotifyPropertyChangedFor(nameof(HasCustomColor))]
    private string? _color;

    public PackIconBoxIconsKind IconKind =>
        Enum.TryParse<PackIconBoxIconsKind>(Icon, out var kind) ? kind : PackIconBoxIconsKind.RegularSlider;

    public bool HasCustomColor => !string.IsNullOrEmpty(Color);

    public IBrush IconBrush =>
        HasCustomColor
            ? Brush.Parse(Color!)
            : (Active ? ThemeService.GetCurrentAccentBrush() : Brush.Parse("#94a3b8"));

    public bool Default => _entity.Default;
    public ConfigEntity Entity => _entity;

    public bool HasBadge => Active || Default;
    public string BadgeText => Active ? "ACTIVE" : (Default ? "DEFAULT" : "");
    public IBrush BadgeBackgroundBrush => Active ? ThemeService.GetCurrentAccentBrush() : Brush.Parse("#1e293b");
    public IBrush BadgeForegroundBrush => Active ? Brushes.White : Brush.Parse("#94a3b8");
    public IBrush BadgeBorderBrush => Active ? Brushes.Transparent : Brush.Parse("#334155");
    public string BadgeBackground => Active ? ThemeService.GetCurrentAccentHex() : "#1e293b";
    public string BadgeForeground => Active ? "#ffffff" : "#94a3b8";

    public List<long> SetIds { get; set; } = new();

    public ConfigItemViewModel(ConfigEntity entity)
    {
        _entity = entity;
        _name = entity.Name;
        _description = entity.Description;
        _active = entity.Active;
        _icon = string.IsNullOrWhiteSpace(entity.Icon) ? "RegularSlider" : entity.Icon;
        _color = string.IsNullOrWhiteSpace(entity.Color) ? null : entity.Color;

        if (entity.ConfigSetsEntities != null)
        {
            foreach (var cs in entity.ConfigSetsEntities)
            {
                SetIds.Add(cs.SetsEntityId);
            }
        }

        ThemeService.AccentChanged += () =>
        {
            OnPropertyChanged(nameof(IconBrush));
            OnPropertyChanged(nameof(HasCustomColor));
            OnPropertyChanged(nameof(BadgeBackgroundBrush));
            OnPropertyChanged(nameof(BadgeBackground));
        };
    }

    public void RefreshEntity()
    {
        _entity.Name = Name;
        _entity.Description = Description;
        _entity.Active = Active;
        _entity.Icon = Icon;
        _entity.Color = Color;
    }
}
