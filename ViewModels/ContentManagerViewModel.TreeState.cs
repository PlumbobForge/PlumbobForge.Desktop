using System.Collections.Generic;
using System.Linq;

namespace PlumbobForge.Desktop.ViewModels;

// The set tree is rebuilt from the database on every reload (cache rebuild, imports, moves, deletes). Sets don't save
// whether they are open, so the open/closed state and the selection are taken from the old nodes and put back on
// the new ones. Mods folders do the same in PlaceModsNode.
public partial class ContentManagerViewModel
{
    private sealed record SetTreeState(Dictionary<long, bool> Expanded, HashSet<long> SelectedSetIds, SetNodeViewModel? Selected);

    private SetTreeState CaptureSetTreeState()
    {
        var expanded = new Dictionary<long, bool>();
        var selected = new HashSet<long>();
        foreach (var node in AllSidebarNodes())
        {
            if (node.IsSpecialNode || node.Id is not long id) continue;
            expanded[id] = node.IsExpanded;
            if (node.IsSelected) selected.Add(id);
        }
        return new SetTreeState(expanded, selected, SelectedSetNode);
    }

    private static void RestoreSetExpansion(IEnumerable<SetNodeViewModel> nodes, SetTreeState state)
    {
        foreach (var node in nodes)
        {
            if (!node.IsSpecialNode && node.Id is long id && state.Expanded.TryGetValue(id, out bool open)) node.IsExpanded = open;
            if (node.Children.Count > 0) RestoreSetExpansion(node.Children, state);
        }
    }

    // The rebuilt node that stands for the one selected before. Null for All Items and Mods nodes (PlaceModsNode
    // brings those back) and when the node is gone.
    private SetNodeViewModel? RestoredSelection(SetTreeState state, SetNodeViewModel favoritesNode)
    {
        var old = state.Selected;
        if (old == null || old.IsAllItems || old.IsModsNode) return null;
        if (old.IsFavorites) return favoritesNode;
        // Section headers and smart sets are kept objects: they stay as long as they are still in the tree.
        if (old.IsSetsHeader || old.IsSmartSet || old.IsSmartHeader)
            return AllSidebarNodes().Contains(old) ? old : null;
        return old.Id is long id ? FindNodeById(RootSets, id) : null;
    }

    // Marks the restored selection, plus the other sets of a multi-selection, as selected again.
    private void ApplyRestoredSelection(SetTreeState state)
    {
        foreach (var node in AllSidebarNodes())
        {
            bool wanted = ReferenceEquals(node, SelectedSetNode) ||
                          (SelectedSetNode is { IsSpecialNode: false } && !node.IsSpecialNode && node.Id is long id && state.SelectedSetIds.Contains(id));
            // Mods nodes are handled by PlaceModsNode.
            if (node.IsModsNode && !ReferenceEquals(node, SelectedSetNode)) continue;
            if (node.IsSelected != wanted) node.IsSelected = wanted;
        }
        UpdateSelectedSetsCount();
    }
}
