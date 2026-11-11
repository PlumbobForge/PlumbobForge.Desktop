using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconPacks.Avalonia.BoxIcons;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ConfigItemPickerViewModel : ObservableObject
{
    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    private readonly IReadOnlyDictionary<long, bool> _existingRules;
    private readonly Dictionary<long, SetsEntity> _setsById;
    private List<PickerItemRow> _allItems = new();

    public bool Include { get; }
    public bool ForCollection { get; }
    public string Title => L(Include ? "dialogs.item_picker.title_include" : "dialogs.item_picker.title_exclude");
    public string Subtitle => ForCollection
        ? L("dialogs.item_picker.subtitle_collection", _configName)
        : L(Include ? "dialogs.item_picker.subtitle_include" : "dialogs.item_picker.subtitle_exclude", _configName);
    private readonly string _configName;

    public List<PickerSetRow> Sets { get; } = new();
    private readonly List<PickerSetRow> _allSetRows = new();

    [ObservableProperty]
    private PickerSetRow? _selectedSet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchQuery))]
    private string _searchQuery = string.Empty;

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<PickerItemRow> _visibleItems = Array.Empty<PickerItemRow>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading = true;

    public bool IsEmpty => !IsLoading && VisibleItems.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicked), nameof(ConfirmText), nameof(SelectedText))]
    private int _pickedCount;

    public bool HasPicked => PickedCount > 0;
    public string ConfirmText => L(Include ? "dialogs.item_picker.confirm_include" : "dialogs.item_picker.confirm_exclude", PickedCount);
    public string SelectedText => L("dialogs.item_picker.selected", PickedCount);

    public IReadOnlyList<long> Result { get; private set; } = Array.Empty<long>();

    public ConfigItemPickerViewModel(bool include, string configName, IReadOnlyList<SetsEntity> sets, IReadOnlyDictionary<long, bool> existingRules, bool forCollection = false)
    {
        Include = include;
        ForCollection = forCollection;
        _configName = configName;
        _existingRules = existingRules;
        _setsById = sets.ToDictionary(s => s.Id);

        // Same special entries as the library's sets sidebar.
        Sets.Add(new PickerSetRow(null, L("library.all_items"), null, null));
        Sets.Add(new PickerSetRow(null, L("library.favorites"), null, null) { IsFavorites = true });
        _allSetRows.AddRange(Sets);
        AddSetRows(sets, parentId: null, Sets, new HashSet<long>());
        _selectedSet = Sets[0];
        _selectedSet.IsSelected = true;
    }

    private void AddSetRows(IReadOnlyList<SetsEntity> sets, long? parentId, List<PickerSetRow> target, HashSet<long> visited)
    {
        foreach (var set in sets.Where(s => s.ParentSetsEntityId == parentId || (parentId == null && s.ParentSetsEntityId.HasValue && !_setsById.ContainsKey(s.ParentSetsEntityId.Value)))
                                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!visited.Add(set.Id)) continue;
            var row = new PickerSetRow(set.Id, set.Name, set.Icon, set.Color);
            target.Add(row);
            _allSetRows.Add(row);
            AddSetRows(sets, set.Id, row.Children, visited);
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var services = App.Services ?? throw new InvalidOperationException("Services are not available");
            bool forCollection = ForCollection;
            var rows = await Task.Run(async () =>
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // Rules apply to whole items: a Sims3Pack's parts follow the pack, so only top-level items are listed.
                var query = db.MetaEntities.AsNoTracking().Where(m => m.ParentMetaEntityId == null);
                if (forCollection)
                {
                    // A collection only holds Build/Buy objects (a Sims3Pack counts when one of its parts is one).
                    var types = PackageTypeHelper.BuildBuyTypes;
                    query = query.Where(m => types.Contains(m.PackageType) ||
                        db.MetaEntities.Any(c => c.ParentMetaEntityId == m.Id && types.Contains(c.PackageType)));
                }
                return await query
                    .Select(m => new { m.Id, m.FileName, m.SetsEntityId, m.IsFavorite })
                    .ToListAsync();
            });

            _allItems = rows
                .OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(r =>
                {
                    bool? rule = _existingRules.TryGetValue(r.Id, out bool inc) ? inc : null;
                    var row = new PickerItemRow(r.Id, r.FileName, r.SetsEntityId, r.IsFavorite, GetLocation(r.SetsEntityId), rule, Include);
                    row.CheckedChanged += () => PickedCount = _allItems.Count(i => i.IsChecked);
                    return row;
                })
                .ToList();

            var counts = _allItems.Where(i => i.SetId.HasValue).GroupBy(i => i.SetId!.Value).ToDictionary(g => g.Key, g => g.Count());
            foreach (var setRow in _allSetRows)
            {
                setRow.ItemCount = setRow.IsAllItems ? _allItems.Count
                    : setRow.IsFavorites ? _allItems.Count(i => i.IsFavorite)
                    : counts.GetValueOrDefault(setRow.Id!.Value);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not load items for the configuration item picker", ex, "ConfigItemPickerViewModel");
        }
        finally
        {
            IsLoading = false;
            ApplyFilter();
        }
    }

    private string GetLocation(long? setId)
    {
        var names = new List<string>();
        var visited = new HashSet<long>();
        long? current = setId;
        while (current.HasValue && visited.Add(current.Value) && _setsById.TryGetValue(current.Value, out var set))
        {
            names.Insert(0, set.Name);
            current = set.ParentSetsEntityId;
        }
        return string.Join("  ›  ", names);
    }

    partial void OnSelectedSetChanged(PickerSetRow? oldValue, PickerSetRow? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
        ApplyFilter();
    }
    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    public void ClearSearch() => SearchQuery = string.Empty;

    private void ApplyFilter()
    {
        IEnumerable<PickerItemRow> items = _allItems;
        if (SelectedSet?.IsFavorites == true) items = items.Where(i => i.IsFavorite);
        else if (SelectedSet?.Id is long setId) items = items.Where(i => i.SetId == setId);
        if (HasSearchQuery)
        {
            var q = SearchQuery.Trim();
            items = items.Where(i => i.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Location.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        VisibleItems = items.ToList();
    }

    public void Confirm()
    {
        Result = _allItems.Where(i => i.IsChecked).Select(i => i.Id).ToList();
    }
}

public sealed partial class PickerSetRow : ObservableObject
{
    public long? Id { get; }
    public string Name { get; }
    public PackIconBoxIconsKind IconKind { get; }
    public IBrush IconBrush { get; }
    public bool IsFavorites { get; init; }
    public bool IsAllItems => Id == null && !IsFavorites;
    public bool IsSpecialNode => Id == null;

    public List<PickerSetRow> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _itemCount;

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    public PickerSetRow(long? id, string name, string? icon, string? color)
    {
        Id = id;
        Name = name;
        IconKind = Enum.TryParse<PackIconBoxIconsKind>(icon, out var kind) ? kind : PackIconBoxIconsKind.RegularFolder;
        IconBrush = !string.IsNullOrWhiteSpace(color) ? Brush.Parse(color) : ThemeService.GetCurrentAccentBrush();
    }
}

public sealed partial class PickerItemRow : ObservableObject
{
    private readonly ItemThumbnail _thumbnail;

    public long Id { get; }
    public string FileName { get; }
    public long? SetId { get; }
    public bool IsFavorite { get; }
    public string Location { get; }
    public bool HasLocation => !string.IsNullOrEmpty(Location);
    public bool CanPick { get; }
    public string? RuleNote { get; }
    public bool HasRuleNote => RuleNote != null;

    public Bitmap? Thumbnail => _thumbnail.Get();

    [ObservableProperty]
    private bool _isChecked;

    public event Action? CheckedChanged;

    public PickerItemRow(long id, string fileName, long? setId, bool isFavorite, string location, bool? existingRule, bool pickerIncludes)
    {
        Id = id;
        FileName = fileName;
        SetId = setId;
        IsFavorite = isFavorite;
        Location = location;
        CanPick = existingRule != pickerIncludes;
        RuleNote = existingRule switch
        {
            true => LocalizationManager.Instance.GetString(pickerIncludes ? "dialogs.item_picker.already_included" : "dialogs.item_picker.included_now"),
            false => LocalizationManager.Instance.GetString(pickerIncludes ? "dialogs.item_picker.excluded_now" : "dialogs.item_picker.already_excluded"),
            null => null
        };
        _thumbnail = new ItemThumbnail(id, () => OnPropertyChanged(nameof(Thumbnail)));
    }

    partial void OnIsCheckedChanged(bool value) => CheckedChanged?.Invoke();
}
