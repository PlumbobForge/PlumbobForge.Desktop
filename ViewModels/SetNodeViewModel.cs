using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconPacks.Avalonia.BoxIcons;
using PlumbobForge.Backend.Database;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class SetNodeViewModel : ObservableObject
{
    private readonly SetsEntity? _entity;

    public long? Id => _entity?.Id;

    [ObservableProperty]
    private string _name = string.Empty;

    public bool IsDefault => string.Equals(Name, "Default", StringComparison.OrdinalIgnoreCase);
    public bool CanCustomize => !IsAllItems && !IsDefault;
    public bool CanDelete => !IsAllItems && !IsDefault;
    public bool Dirty => _entity?.Dirty ?? false;
    public SetsEntity? Entity => _entity;
    public bool IsAllItems => _entity == null && !Id.HasValue;
    public bool HasChildren => Children.Count > 0;
    public bool CanSelect => !IsAllItems;
    public bool CanDrag => !IsAllItems;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    private string _icon = "RegularFolder";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    [NotifyPropertyChangedFor(nameof(HasCustomColor))]
    private string? _color;

    public PackIconBoxIconsKind IconKind =>
        Enum.TryParse<PackIconBoxIconsKind>(Icon, out var kind) ? kind : PackIconBoxIconsKind.RegularFolder;

    public bool HasCustomColor => !string.IsNullOrEmpty(Color) && Color != "#10b981" && Color != "#34d399";

    public IBrush IconBrush =>
        HasCustomColor ? Brush.Parse(Color!) : ThemeService.GetCurrentAccentBrush();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    public event System.Action? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }

    [RelayCommand]
    public void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    public void ToggleSelection()
    {
        if (!CanSelect) return;
        IsSelected = !IsSelected;
    }

    [ObservableProperty]
    private int _itemCount;

    public ObservableCollection<SetNodeViewModel> Children { get; } = new();

    public SetNodeViewModel(string name, long? id = null)
    {
        _name = name;
        _entity = null;
        _icon = "RegularFolder";
        _color = null;
        ThemeService.AccentChanged += OnAccentChanged;
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
    }

    public SetNodeViewModel(SetsEntity entity)
    {
        _entity = entity;
        _name = entity.Name;
        _isExpanded = entity.IsExpanded;
        _icon = string.IsNullOrWhiteSpace(entity.Icon) ? "RegularFolder" : entity.Icon;
        _color = string.IsNullOrWhiteSpace(entity.Color) ? null : entity.Color;
        ThemeService.AccentChanged += OnAccentChanged;
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
    }

    private void OnAccentChanged()
    {
        OnPropertyChanged(nameof(IconBrush));
        OnPropertyChanged(nameof(HasCustomColor));
    }

    public void CalculateItemCount(System.Collections.Generic.IDictionary<long, int> itemCountsBySetId)
    {
        int directCount = 0;
        if (Id.HasValue && itemCountsBySetId.TryGetValue(Id.Value, out var count))
        {
            directCount = count;
        }

        foreach (var c in Children)
        {
            c.CalculateItemCount(itemCountsBySetId);
        }

        ItemCount = directCount;
    }

    public IEnumerable<SetNodeViewModel> GetAllDescendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var descendant in child.GetAllDescendants())
            {
                yield return descendant;
            }
        }
    }

    public bool ContainsDescendant(long setId)
    {
        if (Id == setId) return true;
        return Children.Any(c => c.ContainsDescendant(setId));
    }
}
