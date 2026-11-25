using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Desktop.Controls.Icons;
using PlumbobForge.Backend.Database;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class SetNodeViewModel : ObservableObject
{
    private readonly SetsEntity? _entity;

    public long? Id => _entity?.Id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isFavorites = false;

    // The "Mods" section header and the real folders below it. Their Id is null: Id always means a set.
    public bool IsModsRoot { get; init; }
    public ModsFolderEntity? ModsFolder { get; }
    public bool IsModsFolder => ModsFolder != null;
    public bool IsModsNode => IsModsRoot || IsModsFolder;
    // A folder Resource.cfg names (Packages, Overrides, ...): renaming or deleting it would stop the game loading it.
    public bool IsModsCfgRoot { get; init; }
    public bool CanAddModsFolder => IsModsFolder;
    // Any Mods folder but Packages and Overrides; a root folder takes its Resource.cfg lines with it.
    public bool CanDeleteModsFolder => IsModsFolder && !PlumbobForge.Backend.Services.ModsFolderService.IsStandardRoot(ModsFolder!.RelativePath);
    public bool CanRenameModsFolder => IsModsFolder && !IsModsCfgRoot;
    public bool ShowDeleteEntry => CanDelete || CanDeleteModsFolder;

    // The "Sets" section header shown above the sets when the Mods folder is listed too.
    public bool IsSetsHeader { get; init; }

    // A smart set (saved filters) and the "Smart Sets" section header above them.
    public string? SmartSetId { get; init; }
    public bool IsSmartSet => SmartSetId != null;
    public bool IsSmartHeader { get; init; }
    public bool ShowsPlusAdd => IsSetsHeader || IsSmartHeader;

    // Drawn as a quiet section header (the Sets header, or the Mods root next to sets); its children aren't indented.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChildrenIndent))]
    private bool _isSectionHeader;

    // The Mods root in Mods-only storage: no row, its folders sit flush at the top level.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChildrenIndent))]
    private bool _isRowHidden;

    // A top-level set while the Sets section is collapsed.
    [ObservableProperty]
    private bool _isHiddenBySection;

    public Avalonia.Thickness ChildrenIndent => IsSectionHeader || IsRowHidden ? new Avalonia.Thickness(0) : new Avalonia.Thickness(16, 0, 0, 0);
    public string SectionAddToolTip => LocalizationManager.Instance.GetString(IsSmartHeader ? "library.smart_save_tip" : IsSetsHeader ? "library.add_set" : "library.mods_new_folder_in_packages");

    public bool IsSpecialNode => IsAllItems || IsFavorites || IsModsNode || IsSetsHeader || IsSmartSet || IsSmartHeader;

    // Inline rename in the sidebar (F2 or a slow second click): what Customize could rename, nothing else.
    public bool CanQuickRename => CanRenameModsFolder || IsSmartSet || (Id.HasValue && !IsSpecialNode && !IsDefault);

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;

    // Why the typed name can't be used; shown on the box while it stays open.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRenameError))]
    private string? _renameError;

    public bool HasRenameError => !string.IsNullOrEmpty(RenameError);

    // Set while a rename is being saved, so Enter and losing focus don't save it twice.
    public bool IsRenameSaving { get; set; }

    partial void OnRenameTextChanged(string value) => RenameError = null;
    public bool ShowsOwnIcon => !IsAllItems && !IsFavorites;
    public bool IsDefault => !IsModsNode && string.Equals(Name, "Default", StringComparison.OrdinalIgnoreCase);
    public bool CanCustomize => IsModsFolder || (!IsSpecialNode && !IsDefault);
    public bool CanDelete => !IsSpecialNode && !IsDefault;
    public bool Dirty => _entity?.Dirty ?? false;
    public SetsEntity? Entity => _entity;
    public bool IsAllItems => _entity == null && !Id.HasValue && !IsFavorites && !IsModsNode && !IsSetsHeader && !IsSmartSet && !IsSmartHeader;
    public bool HasChildren => Children.Count > 0;
    public bool CanSelect => !IsSpecialNode;
    public bool CanDrag => !IsSpecialNode;
    public bool CanAddSubSet => !IsSpecialNode;

    // Sets and Mods folders the active configuration doesn't load are crossed out in the sidebar.
    public bool CanToggleInConfiguration => IsModsFolder || (Id.HasValue && !IsSpecialNode);

    [ObservableProperty]
    private bool _isOffInConfiguration;

    // "Turn off in “Name”" / "Turn on in “Name”"; empty when there is no active configuration.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConfigurationToggle))]
    private string _configurationToggleHeader = string.Empty;

    // Tooltip on a crossed-out name; null when the node is on.
    [ObservableProperty]
    private string? _configurationOffToolTip;

    public bool ShowConfigurationToggle => CanToggleInConfiguration && ConfigurationToggleHeader.Length > 0;

    public string CustomizeHeader => LocalizationManager.Instance.GetString(IsModsFolder ? "library.customize_mods_folder" : "library.customize_set");
    public string ExpandToolTip => LocalizationManager.Instance.GetString(IsModsNode ? "library.toggle_subfolders" : "library.toggle_subsets");

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
        HasCustomColor ? Brush.Parse(Color!) : ThemeService.GetCurrentAccentBrush();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    public event System.Action? SelectionChanged;

    partial void OnIsExpandedChanged(bool value)
    {
        if (ModsFolder != null) ModsFolder.IsExpanded = value;
        ExpandedChanged?.Invoke(this, value);
    }

    public event System.Action<SetNodeViewModel, bool>? ExpandedChanged;

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

    // The set's own Resource.cfg priority (null takes the parent's); shown as a badge when it has one.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnPriority), nameof(PriorityBadge), nameof(PriorityToolTip))]
    private int? _priority;

    public bool HasOwnPriority => Priority.HasValue;
    public string PriorityBadge => Priority?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    public string PriorityToolTip => Priority is int p
        ? string.Format(PlumbobForge.Desktop.Services.Localization.LocalizationManager.Instance.GetString("library.set_priority_tip"), p)
        : string.Empty;

    public SetNodeViewModel(SetsEntity entity)
    {
        _entity = entity;
        _name = entity.Name;
        _isExpanded = entity.IsExpanded;
        _priority = entity.Priority;
        _icon = string.IsNullOrWhiteSpace(entity.Icon) ? "RegularFolder" : entity.Icon;
        _color = string.IsNullOrWhiteSpace(entity.Color) ? null : entity.Color;
        ThemeService.AccentChanged += OnAccentChanged;
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
    }

    public SetNodeViewModel(ModsFolderEntity folder)
    {
        ModsFolder = folder;
        _entity = null;
        _name = folder.Name;
        _isExpanded = folder.IsExpanded;
        _icon = string.IsNullOrWhiteSpace(folder.Icon) ? "RegularFolder" : folder.Icon;
        _color = string.IsNullOrWhiteSpace(folder.Color) ? null : folder.Color;
        ThemeService.AccentChanged += OnAccentChanged;
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
    }

    // Item counts by Mods folder id; like sets, a folder shows the items directly in it.
    public void CalculateModsItemCount(IReadOnlyDictionary<long, int> itemCountsByFolderId)
    {
        foreach (var c in Children) c.CalculateModsItemCount(itemCountsByFolderId);
        if (ModsFolder != null) ItemCount = itemCountsByFolderId.GetValueOrDefault(ModsFolder.Id, 0);
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
