using System;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IconPacks.Avalonia.BoxIcons;

namespace PlumbobForge.Desktop.ViewModels;

public enum QuickSwitcherItemType
{
    Configuration,
    Set,
    Item,
    Place,
    Collection,
    Command
}

public partial class QuickSwitcherItemViewModel : ObservableObject
{
    public QuickSwitcherItemType Type { get; init; }
    public long TargetId { get; init; }
    public long? SecondaryId { get; init; } // e.g. SetId for an Item
    public string? PlaceTarget { get; init; } // e.g. "Tab:Library", "Tools:Cache", "Settings:Appearance"
    public string? Keywords { get; init; }

    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? DetailBadge { get; init; }

    public PackIconBoxIconsKind IconKind { get; init; } = PackIconBoxIconsKind.RegularSearch;
    public IBrush IconBrush { get; init; } = Brush.Parse("#94a3b8");

    public Func<Task>? CommandAction { get; init; }

    public string? CompletionText { get; init; }

    public string BadgeText => Type switch
    {
        QuickSwitcherItemType.Configuration => "CONFIG",
        QuickSwitcherItemType.Set => "SET",
        QuickSwitcherItemType.Item => "ITEM",
        QuickSwitcherItemType.Place => "PLACE",
        QuickSwitcherItemType.Collection => "COLLECTION",
        QuickSwitcherItemType.Command => "CMD",
        _ => ""
    };

    public bool UsesAccentBadge => Type is QuickSwitcherItemType.Place or QuickSwitcherItemType.Command;

    public string BadgeBackground => Type switch
    {
        QuickSwitcherItemType.Configuration => "#1e3a5f",
        QuickSwitcherItemType.Set => "#14382c",
        QuickSwitcherItemType.Item => "#2d1f4d",
        QuickSwitcherItemType.Collection => "#3b2a10",
        _ => "#1e293b"
    };

    public string BadgeForeground => Type switch
    {
        QuickSwitcherItemType.Configuration => "#60a5fa",
        QuickSwitcherItemType.Set => "#34d399",
        QuickSwitcherItemType.Item => "#c084fc",
        QuickSwitcherItemType.Collection => "#fbbf24",
        _ => "#94a3b8"
    };

    [ObservableProperty]
    private bool _isSelected;
}
