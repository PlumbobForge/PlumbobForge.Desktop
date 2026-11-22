using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlumbobForge.Desktop.ViewModels;

// When the sidebar lists both sets and the Mods folder, each gets a quiet section header with its own
// actions, and the top bar only says "Library". Sets only: the classic "Sets" bar. Mods only: no headers,
// the Mods folders sit flush under All Items and Favorites.
public partial class ContentManagerViewModel
{
    private SetNodeViewModel? _setsHeaderNode;
    private SetNodeViewModel? _watchedModsRoot;

    // Remembered across restarts with the rest of the library's UI state.
    private bool _setsSectionCollapsed;
    private bool _modsSectionCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSetsTopBar))]
    private bool _showSectionHeaders;

    // The top bar's "Sets" title, sort and new-set buttons.
    public bool ShowSetsTopBar => !IsModsOnly && !ShowSectionHeaders;

    private void PlaceSectionHeaders()
    {
        bool show = _modsRootNode != null && !IsModsOnly;
        ShowSectionHeaders = show;
        if (_modsRootNode != null)
        {
            if (!ReferenceEquals(_watchedModsRoot, _modsRootNode))
            {
                _modsRootNode.ExpandedChanged += OnModsSectionExpandedChanged;
                _watchedModsRoot = _modsRootNode;
            }
            if (show) _modsRootNode.IsExpanded = !_modsSectionCollapsed;
            _modsRootNode.IsSectionHeader = show;
            _modsRootNode.IsRowHidden = IsModsOnly;
            if (IsModsOnly) _modsRootNode.IsExpanded = true;
        }

        if (_setsHeaderNode != null) RootSets.Remove(_setsHeaderNode);
        // The Sets view goes away with its header (e.g. the Mods folder was turned off): fall back to All Items.
        if (!show && SelectedSetNode?.IsSetsHeader == true)
        {
            _setsHeaderNode!.IsSelected = false;
            var allItems = RootSets.FirstOrDefault(n => n.IsAllItems);
            SelectedSetNode = allItems;
            if (allItems != null) allItems.IsSelected = true;
        }
        if (show)
        {
            if (_setsHeaderNode == null)
            {
                _setsHeaderNode = new SetNodeViewModel(L("library.sets")) { IsSetsHeader = true, IsSectionHeader = true, IsExpanded = !_setsSectionCollapsed };
                _setsHeaderNode.ExpandedChanged += (_, expanded) =>
                {
                    ApplySetsSectionVisibility();
                    if (_setsSectionCollapsed == expanded)
                    {
                        _setsSectionCollapsed = !expanded;
                        SaveCurrentUiState();
                    }
                };
            }
            int at = RootSets.TakeWhile(n => n.IsAllItems || n.IsFavorites).Count();
            RootSets.Insert(at, _setsHeaderNode);
        }
        ApplySetsSectionVisibility();
        UpdateSetsHeaderCount();
        PlaceSmartSets();
    }

    // A collapsed Sets section hides the sets at the top level (their sub-sets go with them).
    private void ApplySetsSectionVisibility()
    {
        bool hide = _setsHeaderNode != null && ShowSectionHeaders && !_setsHeaderNode.IsExpanded;
        foreach (var node in RootSets)
        {
            if (!node.IsSpecialNode) node.IsHiddenBySection = hide;
        }
    }

    // Only while it is a section header: Mods-only keeps the root open without changing the saved choice.
    private void OnModsSectionExpandedChanged(SetNodeViewModel node, bool expanded)
    {
        if (!ShowSectionHeaders || _modsSectionCollapsed != expanded) return;
        _modsSectionCollapsed = !expanded;
        SaveCurrentUiState();
    }

    private void UpdateSetsHeaderCount()
    {
        if (_setsHeaderNode != null) _setsHeaderNode.ItemCount = Items.Count(i => !i.Entity.IsModsItem);
    }

    private void ExpandSetsSection()
    {
        if (_setsHeaderNode != null) _setsHeaderNode.IsExpanded = true;
    }

    // New folders go in Packages: a new folder at the top of Mods would not be loaded by Resource.cfg.
    [RelayCommand]
    public async Task AddPackagesFolderAsync()
    {
        if (_modsRootNode == null) return;
        var packagesId = DefaultModsImportFolderId();
        var parent = (packagesId.HasValue ? FindModsNode(_modsRootNode, packagesId.Value) : null)
                     ?? _modsRootNode.Children.FirstOrDefault();
        if (parent == null) return;
        _modsRootNode.IsExpanded = true;
        await AddModsFolderAsync(parent);
    }

    // Selecting the Sets header shows every library item, like the Mods Folder header shows every Mods item.
    private static IEnumerable<ItemViewModel> FilterBySetsHeader(IEnumerable<ItemViewModel> query) =>
        query.Where(i => !i.Entity.IsModsItem);
}
