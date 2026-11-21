using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PlumbobForge.Backend.Database;

namespace PlumbobForge.Desktop.ViewModels;

// Both columns show the set tree. A parent that isn't in a column but has sub-sets there is a group row:
// it keeps the nesting visible and has no membership of its own in that column.
public partial class ConfigurationsViewModel
{
    // Closed groups per column and the order made by dragging. Both last while the app runs; nothing is saved,
    // because sets and configurations have no order field of their own.
    private readonly HashSet<long> _collapsedEnabled = new();
    private readonly HashSet<long> _collapsedDisabled = new();
    private readonly List<long> _manualConfigOrder = new();
    private readonly List<long> _manualSetOrder = new();

    // Set rows as a tree, then the item cards this configuration includes or excludes on their own; with
    // "Items first" the cards come before the tree.
    public ObservableCollection<ConfigColumnEntry> VisibleEnabledSets { get; } = new();
    public ObservableCollection<ConfigColumnEntry> VisibleDisabledSets { get; } = new();

    private void RefreshVisibleColumns()
    {
        ProjectColumn(true, EnabledSets, VisibleEnabledSets, _collapsedEnabled, HasEnabledSetsSearchQuery);
        ProjectColumn(false, DisabledSets, VisibleDisabledSets, _collapsedDisabled, HasDisabledSetsSearchQuery);
    }

    private void ProjectColumn(bool enabled, IEnumerable<ConfigColumnEntry> column,
        ObservableCollection<ConfigColumnEntry> visible, HashSet<long> collapsed, bool searching)
    {
        var entries = column.ToList();
        var memberRows = entries.OfType<ConfigSetItemViewModel>().ToDictionary(m => m.Id);
        var itemCards = entries.OfType<ConfigItemCardViewModel>().ToList();
        var needed = new HashSet<long>(memberRows.Keys);
        foreach (var id in memberRows.Keys)
        {
            var seen = new HashSet<long> { id };
            var parent = _setsById[id].ParentSetsEntityId;
            while (parent is long parentId && _setsById.TryGetValue(parentId, out var entity) && seen.Add(parentId))
            {
                needed.Add(parentId);
                parent = entity.ParentSetsEntityId;
            }
        }

        var childrenOf = _allSets
            .Where(s => needed.Contains(s.Id))
            .ToLookup(s => s.ParentSetsEntityId is long p && needed.Contains(p) ? p : (long?)null);

        var output = new List<ConfigSetItemViewModel>();
        void Visit(long? parent, int depth)
        {
            var rows = childrenOf[parent].Select(set => memberRows.TryGetValue(set.Id, out var row) ? row : CreateGroupRow(set, enabled)).ToList();
            foreach (var row in SortSetsList(rows))
            {
                row.ColumnEnabled = enabled;
                row.Depth = depth;
                row.VisibleChildCount = childrenOf[row.Id].Count();
                // Search results never hide inside a closed group.
                row.IsExpanded = searching || !collapsed.Contains(row.Id);
                output.Add(row);
                if (row.IsExpanded) Visit(row.Id, depth + 1);
            }
        }
        Visit(null, 0);

        visible.Clear();
        if (SortItemsFirst) foreach (var card in itemCards) visible.Add(card);
        foreach (var row in output) visible.Add(row);
        if (!SortItemsFirst) foreach (var card in itemCards) visible.Add(card);
    }

    private ConfigSetItemViewModel CreateGroupRow(SetsEntity set, bool column) =>
        new(set, BuildBreadcrumb(set), GetDescendantCount(set.Id), !column) { IsGroupOnly = true };

    public void ToggleColumnGroup(ConfigSetItemViewModel row)
    {
        if (!row.HasVisibleChildren) return;

        var collapsed = row.ColumnEnabled ? _collapsedEnabled : _collapsedDisabled;
        if (!collapsed.Add(row.Id)) collapsed.Remove(row.Id);
        RefreshVisibleColumns();
    }

    // The sets a dragged group row stands for: its descendants that are in this column.
    public List<long> MembersUnder(ConfigSetItemViewModel group)
    {
        var inColumn = (group.ColumnEnabled ? EnabledSets : DisabledSets).OfType<ConfigSetItemViewModel>().Select(s => s.Id).ToHashSet();
        return GetAllDescendantIds(group.Id).Where(inColumn.Contains).ToList();
    }

    private IEnumerable<ConfigSetItemViewModel> VisibleColumn(bool enabled) => (enabled ? VisibleEnabledSets : VisibleDisabledSets).OfType<ConfigSetItemViewModel>();

    // Drops the rows in front of beforeId (null = after the last of their siblings) and switches to Manual order.
    // Rows stay under their own parent: reordering never changes the set tree.
    public void PlaceConfigSets(IReadOnlyCollection<long> ids, long? beforeId, bool columnEnabled)
    {
        var first = VisibleColumn(columnEnabled).FirstOrDefault(r => ids.Contains(r.Id));
        if (first == null) return;

        // The first drag starts from the order shown now. All sets are ranked, so closed groups keep it too.
        if (SetSortMode != "Manual")
        {
            _manualSetOrder.Clear();
            _manualSetOrder.AddRange(SortSetsList(_allSets.Select(s => new ConfigSetItemViewModel(s, descendantCount: GetDescendantCount(s.Id))).ToList()).Select(s => s.Id));
        }

        var siblings = VisibleColumn(columnEnabled).Where(r => r.ParentSetsEntityId == first.ParentSetsEntityId).Select(r => r.Id).ToList();
        if (!PlaceInOrder(_manualSetOrder, siblings, ids, beforeId)) return;

        var keepSelected = EnabledSets.Concat(DisabledSets).OfType<ConfigSetItemViewModel>().Where(s => s.IsSelected).Select(s => s.Id).ToHashSet();
        if (SetSortMode != "Manual") SetSetSortMode("Manual");
        else RefreshSetsColumns();

        // The columns are rebuilt, so put the selection back.
        foreach (var set in EnabledSets.Concat(DisabledSets).OfType<ConfigSetItemViewModel>().Where(s => keepSelected.Contains(s.Id))) set.IsSelected = true;
        UpdateSelectionState();
    }

    // Drops a configuration in front of beforeId (null = at the end) and switches the list to Manual order.
    public void PlaceConfiguration(long id, long? beforeId)
    {
        var shown = Configurations.Select(c => c.Id).ToList();
        if (ConfigSortMode != "Manual")
        {
            _manualConfigOrder.Clear();
            _manualConfigOrder.AddRange(shown);
        }
        if (!PlaceInOrder(_manualConfigOrder, shown, new[] { id }, beforeId)) return;
        if (ConfigSortMode != "Manual") SetConfigSortMode("Manual");
        else SortConfigurationsList();
    }

    // Ids with a place in the order come in that order; ids without one (added since) keep the fallback order
    // and go first, so new sets and configurations are easy to find.
    private static List<T> ApplyManualOrder<T>(List<long> order, IEnumerable<T> fallbackOrdered, Func<T, long> id)
    {
        var items = fallbackOrdered.ToList();
        var rank = new Dictionary<long, int>();
        for (int i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);
        return items.Where(i => !rank.ContainsKey(id(i)))
            .Concat(items.Where(i => rank.ContainsKey(id(i))).OrderBy(i => rank[id(i)]))
            .ToList();
    }

    // Moves the given ids, which keep their shown order, in front of beforeId (null = after the last shown id).
    // Every other id keeps its place, so the other column and closed groups don't change. Returns false if
    // nothing moved.
    private static bool PlaceInOrder(List<long> order, IReadOnlyList<long> shown, IReadOnlyCollection<long> moving, long? beforeId)
    {
        var block = shown.Where(moving.Contains).ToList();
        var rest = shown.Where(id => !moving.Contains(id)).ToList();
        if (block.Count == 0 || rest.Count == 0) return false;

        var placed = rest.ToList();
        int insertAt = beforeId is long before ? placed.IndexOf(before) : -1;
        placed.InsertRange(insertAt < 0 ? placed.Count : insertAt, block);
        if (placed.SequenceEqual(shown)) return false;

        // Ids without a place are shown first (see ApplyManualOrder), so they get the first places.
        order.InsertRange(0, shown.Where(id => !order.Contains(id)));
        order.RemoveAll(block.Contains);
        int at = insertAt >= 0 ? order.IndexOf(rest[insertAt]) : order.IndexOf(rest[^1]) + 1;
        order.InsertRange(at, block);
        return true;
    }
}
