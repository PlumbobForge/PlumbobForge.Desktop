using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

// Smart sets, called "Saved Filters" in the app: saved filter panels (search included) that show up in the
// sidebar between Favorites and Sets.
// Picking one loads its filters over everything in the library; leaving it puts back the filters you had
// before. They live in the library's UI state, not in the database, so nothing about the game changes.
public partial class ContentManagerViewModel
{
    // Every filter-panel value: the Filter* switches, the price range and the search text.
    private static readonly PropertyInfo[] FilterProps = typeof(ContentManagerViewModel).GetProperties()
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                    && ((p.Name.StartsWith("Filter", StringComparison.Ordinal) && (p.PropertyType == typeof(bool) || p.PropertyType == typeof(decimal?)))
                        || p.Name == nameof(SearchQuery)))
        .ToArray();

    // The generated backing fields, for counting a smart set's items without touching the panel.
    private static readonly FieldInfo?[] FilterFields = FilterProps
        .Select(p => typeof(ContentManagerViewModel).GetField("_" + char.ToLowerInvariant(p.Name[0]) + p.Name[1..], BindingFlags.Instance | BindingFlags.NonPublic))
        .ToArray();

    private List<SmartSetState> _smartSets = new();
    private SetNodeViewModel? _smartHeaderNode;
    private readonly Dictionary<string, SetNodeViewModel> _smartNodes = new();
    private bool _smartSectionCollapsed;
    private string? _savedSmartSetId;
    // The smart set whose filters are loaded, and the panel as it was before the first one was picked.
    private SetNodeViewModel? _appliedSmartNode;
    private Dictionary<string, string>? _filtersBeforeSmart;

    // A new smart set opens its name for editing in the sidebar. The sidebar shown last answers first and the
    // first one that takes it ends the request.
    public event Func<SetNodeViewModel, bool>? RequestSidebarRename;

    private void AskSidebarToRename(SetNodeViewModel node)
    {
        if (RequestSidebarRename == null) return;
        foreach (var handler in RequestSidebarRename.GetInvocationList().Reverse().Cast<Func<SetNodeViewModel, bool>>())
        {
            if (handler(node)) return;
        }
    }

    public bool HasSmartSets => _smartSets.Count > 0;

    // Most filters are on by default; "only" filters are off.
    private static string DefaultFilterValue(PropertyInfo p) =>
        p.PropertyType != typeof(bool) ? string.Empty : p.Name == nameof(FilterModeOptimizedOnly) ? "False" : "True";

    private static string FormatFilterValue(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "True" : "False",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static object? ParseFilterValue(PropertyInfo p, string? text)
    {
        if (p.PropertyType == typeof(bool)) return !string.Equals(text, "False", StringComparison.OrdinalIgnoreCase);
        if (p.PropertyType == typeof(decimal?))
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        return text ?? string.Empty;
    }

    // Only what differs from an untouched panel is stored.
    public Dictionary<string, string> SnapshotFilters()
    {
        var result = new Dictionary<string, string>();
        foreach (var p in FilterProps)
        {
            string value = FormatFilterValue(p.GetValue(this));
            if (p.Name == nameof(SearchQuery)) value = value.Trim();
            if (value != DefaultFilterValue(p)) result[p.Name] = value;
        }
        return result;
    }

    private void LoadFilters(IReadOnlyDictionary<string, string> filters)
    {
        foreach (var p in FilterProps)
        {
            filters.TryGetValue(p.Name, out var text);
            var value = ParseFilterValue(p, text ?? DefaultFilterValue(p));
            if (!Equals(p.GetValue(this), value)) p.SetValue(this, value);
        }
        RequestFilterUpdate(0);
    }

    private static bool SameFilters(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public bool IsAnyFilterActive => SnapshotFilters().Count > 0;

    private SmartSetState? SmartOf(SetNodeViewModel? node) =>
        node?.SmartSetId is { } id ? _smartSets.FirstOrDefault(s => s.Id == id) : null;

    // "Save filters" in the Filters header: something is filtered and it isn't a smart set's own filters.
    public bool ShowSaveSmartSet => SelectedSetNode?.IsSmartSet != true && IsAnyFilterActive;

    // On a smart set whose filters were changed: save them into it (or as a new one).
    public bool ShowUpdateSmartSet => SmartOf(SelectedSetNode) is { } smart && !SameFilters(smart.Filters, SnapshotFilters());

    public string UpdateSmartSetLabel => SmartOf(SelectedSetNode) is { } smart ? string.Format(L("library.smart_update"), smart.Name) : string.Empty;

    // The one button in the Filters header: "Save filters", or "Save Changes" on a changed smart set.
    public bool ShowSmartSaveButton => ShowSaveSmartSet || ShowUpdateSmartSet;
    public string SmartSaveButtonLabel => L(ShowUpdateSmartSet ? "library.smart_save_changes" : "library.smart_save_short");
    public string SmartSaveButtonTip => ShowUpdateSmartSet ? UpdateSmartSetLabel : L("library.smart_save_tip");

    [RelayCommand]
    public void SaveOrUpdateSmartSet()
    {
        if (ShowUpdateSmartSet) UpdateSmartSet();
        else SaveSmartSet();
    }

    // Edit Filters: the view opens the panel at the top and puts the cursor in the search box.
    public event Action? RequestEditFilters;

    private void RaiseSmartState()
    {
        OnPropertyChanged(nameof(ShowSaveSmartSet));
        OnPropertyChanged(nameof(ShowUpdateSmartSet));
        OnPropertyChanged(nameof(UpdateSmartSetLabel));
        OnPropertyChanged(nameof(ShowSmartSaveButton));
        OnPropertyChanged(nameof(SmartSaveButtonLabel));
        OnPropertyChanged(nameof(SmartSaveButtonTip));
    }

    // Picking a smart set loads its filters; leaving smart sets puts back what the panel had before.
    private void SyncSmartFilters(SetNodeViewModel? node)
    {
        if (SmartOf(node) is { } smart)
        {
            if (_appliedSmartNode == null) _filtersBeforeSmart = SnapshotFilters();
            _appliedSmartNode = node;
            LoadFilters(smart.Filters);
        }
        else if (_appliedSmartNode != null)
        {
            _appliedSmartNode = null;
            var before = _filtersBeforeSmart ?? new Dictionary<string, string>();
            _filtersBeforeSmart = null;
            LoadFilters(before);
        }
        RaiseSmartState();
    }

    // The section sits right after All Items and Favorites, and only while there is a smart set.
    private void PlaceSmartSets()
    {
        if (_smartSets.Count == 0)
        {
            if (_smartHeaderNode != null) RootSets.Remove(_smartHeaderNode);
            OnPropertyChanged(nameof(HasSmartSets));
            return;
        }

        if (_smartHeaderNode == null)
        {
            _smartHeaderNode = new SetNodeViewModel(L("library.smart_sets")) { IsSmartHeader = true, IsSectionHeader = true, IsExpanded = !_smartSectionCollapsed };
            _smartHeaderNode.ExpandedChanged += (_, expanded) =>
            {
                if (_smartSectionCollapsed != expanded) return;
                _smartSectionCollapsed = !expanded;
                SaveCurrentUiState();
            };
        }

        // Keep the node objects (and so their selection) across rebuilds.
        var wanted = new List<SetNodeViewModel>();
        foreach (var smart in _smartSets)
        {
            if (!_smartNodes.TryGetValue(smart.Id, out var node))
            {
                node = new SetNodeViewModel(smart.Name) { SmartSetId = smart.Id, Icon = "RegularFilter" };
                _smartNodes[smart.Id] = node;
            }
            node.Name = smart.Name;
            wanted.Add(node);
        }
        foreach (var gone in _smartNodes.Keys.Except(_smartSets.Select(s => s.Id)).ToList()) _smartNodes.Remove(gone);
        if (!_smartHeaderNode.Children.SequenceEqual(wanted))
        {
            _smartHeaderNode.Children.Clear();
            foreach (var n in wanted) _smartHeaderNode.Children.Add(n);
        }
        _smartHeaderNode.ItemCount = _smartSets.Count;

        int at = RootSets.TakeWhile(n => n.IsAllItems || n.IsFavorites).Count();
        int now = RootSets.IndexOf(_smartHeaderNode);
        if (now != at)
        {
            if (now >= 0) RootSets.RemoveAt(now);
            RootSets.Insert(Math.Min(at, RootSets.Count), _smartHeaderNode);
        }

        // Back on the smart set that was open when the app closed; its filters were saved with the panel.
        if (_savedSmartSetId != null && _smartNodes.TryGetValue(_savedSmartSetId, out var saved) && (SelectedSetNode == null || SelectedSetNode.IsAllItems))
        {
            _appliedSmartNode = saved;
            DeselectAllSets();
            SelectedSetNode = saved;
            saved.IsSelected = true;
        }
        _savedSmartSetId = null;

        RecountSmartSets();
        OnPropertyChanged(nameof(HasSmartSets));
    }

    // Each smart set's count, worked out on the backing fields so the panel and the list don't move.
    private void RecountSmartSets()
    {
        if (_smartSets.Count == 0 || FilterFields.Any(f => f == null)) return;
        var saved = FilterFields.Select(f => f!.GetValue(this)).ToArray();
        try
        {
            foreach (var smart in _smartSets)
            {
                if (!_smartNodes.TryGetValue(smart.Id, out var node)) continue;
                for (int i = 0; i < FilterProps.Length; i++)
                {
                    smart.Filters.TryGetValue(FilterProps[i].Name, out var text);
                    FilterFields[i]!.SetValue(this, ParseFilterValue(FilterProps[i], text ?? DefaultFilterValue(FilterProps[i])));
                }
                node.ItemCount = FilterByPanel(Items).Count();
            }
        }
        finally
        {
            for (int i = 0; i < FilterFields.Length; i++) FilterFields[i]!.SetValue(this, saved[i]);
        }
    }

    private string UniqueSmartName(string wanted)
    {
        string name = wanted;
        for (int n = 2; _smartSets.Any(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase)); n++) name = $"{wanted} {n}";
        return name;
    }

    private SetNodeViewModel? SelectSmartSet(string id)
    {
        if (!_smartNodes.TryGetValue(id, out var node)) return null;
        if (_smartHeaderNode != null) _smartHeaderNode.IsExpanded = true;
        SelectSetNode(node, isCtrlPressed: false, isShiftPressed: false);
        return node;
    }

    // Saves the panel as it is now. Named after the search when there is one; the name opens for editing.
    [RelayCommand]
    public void SaveSmartSet()
    {
        var filters = SnapshotFilters();
        if (filters.Count == 0)
        {
            IsFiltersSidebarCollapsed = false;
            ShowToast(L("library.smart_pick_filters"));
            return;
        }
        string search = SearchQuery.Trim();
        var smart = new SmartSetState
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = UniqueSmartName(search.Length is > 0 and <= 40 ? search : L("library.smart_new_name")),
            Filters = filters,
        };
        _smartSets.Add(smart);
        PlaceSmartSets();
        // Leaving the new smart set goes back to an untouched panel, not to these same filters.
        if (_smartNodes.TryGetValue(smart.Id, out var node))
        {
            _appliedSmartNode ??= node;
            _filtersBeforeSmart ??= new Dictionary<string, string>();
        }
        SaveCurrentUiState();
        var selected = SelectSmartSet(smart.Id);
        if (selected != null) AskSidebarToRename(selected);
    }

    [RelayCommand]
    public void UpdateSmartSet()
    {
        if (SmartOf(SelectedSetNode) is not { } smart) return;
        var old = smart.Filters;
        smart.Filters = SnapshotFilters();
        SaveCurrentUiState();
        RecountSmartSets();
        RaiseSmartState();
        string id = smart.Id;
        ShowToast(string.Format(L("library.smart_updated"), smart.Name), () =>
        {
            if (_smartSets.FirstOrDefault(s => s.Id == id) is { } back)
            {
                back.Filters = old;
                SaveCurrentUiState();
                RecountSmartSets();
                if (SelectedSetNode?.SmartSetId == id) LoadFilters(old);
                RaiseSmartState();
            }
            return System.Threading.Tasks.Task.CompletedTask;
        });
    }

    // "Save as new" while a smart set's filters are changed: the changed filters become their own smart set.
    [RelayCommand]
    public void SaveSmartSetAsNew()
    {
        if (SmartOf(SelectedSetNode) is not { } from) return;
        var smart = new SmartSetState { Id = Guid.NewGuid().ToString("N"), Name = UniqueSmartName(from.Name), Filters = SnapshotFilters() };
        _smartSets.Insert(_smartSets.IndexOf(from) + 1, smart);
        PlaceSmartSets();
        SaveCurrentUiState();
        var selected = SelectSmartSet(smart.Id);
        if (selected != null) AskSidebarToRename(selected);
    }

    public void EditSmartSetFilters(SetNodeViewModel node)
    {
        if (!ReferenceEquals(SelectedSetNode, node)) SelectSetNode(node, isCtrlPressed: false, isShiftPressed: false);
        IsFiltersSidebarCollapsed = false;
        RequestEditFilters?.Invoke();
    }

    public void DeleteSmartSet(SetNodeViewModel node)
    {
        if (SmartOf(node) is not { } smart) return;
        int index = _smartSets.IndexOf(smart);
        bool wasSelected = ReferenceEquals(SelectedSetNode, node);
        if (wasSelected)
        {
            var allItems = RootSets.FirstOrDefault(n => n.IsAllItems);
            if (allItems != null) SelectSetNode(allItems, isCtrlPressed: false, isShiftPressed: false);
        }
        _smartSets.Remove(smart);
        PlaceSmartSets();
        SaveCurrentUiState();
        ShowToast(string.Format(L("library.smart_deleted"), smart.Name), () =>
        {
            if (_smartSets.All(s => s.Id != smart.Id))
            {
                _smartSets.Insert(Math.Min(index, _smartSets.Count), smart);
                PlaceSmartSets();
                SaveCurrentUiState();
                if (wasSelected) SelectSmartSet(smart.Id);
            }
            return System.Threading.Tasks.Task.CompletedTask;
        });
    }

    private string? RenameSmartSetInline(SetNodeViewModel node, string newName)
    {
        if (SmartOf(node) is not { } smart) return null;
        if (_smartSets.Any(s => s != smart && string.Equals(s.Name, newName, StringComparison.CurrentCultureIgnoreCase)))
            return string.Format(L("library.smart_duplicate"), newName);
        smart.Name = newName;
        node.Name = newName;
        node.IsRenaming = false;
        SaveCurrentUiState();
        RaiseSmartState();
        return null;
    }
}
