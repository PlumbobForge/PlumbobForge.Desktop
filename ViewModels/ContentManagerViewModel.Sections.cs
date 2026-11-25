using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Views.Dialogs;

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

    // The Mods section's + button: a new folder at the top of Mods, next to Packages and Overrides, which is added
    // to Resource.cfg so the game loads it. Sub-folders are made from a folder's own menu (AddModsFolderAsync).
    [RelayCommand]
    public async Task AddModsRootFolderAsync()
    {
        if (_modsRootNode == null) return;
        var name = await DialogHelper.ShowInputAsync(L("library.mods_new_folder"), L("library.mods_new_root_prompt"), L("library.mods_new_root_default"));
        if (string.IsNullOrWhiteSpace(name)) return;
        await CreateModsRootFolderAsync(name);
    }

    // Returns the new folder's path inside Mods, or null after telling the user why it could not be made.
    public async Task<string?> CreateModsRootFolderAsync(string name)
    {
        ModsFolderService.RootFolderResult result;
        using (var scope = App.Services!.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<ModsFolderService>().CreateRootFolderAsync(name);
        }
        if (result.RelativePath == null)
        {
            string shown = name.Trim();
            string message = result.Error switch
            {
                ModsFolderService.RootFolderError.HasSpaces => string.Format(L("library.mods_root_has_spaces"), string.Concat(shown.Where(c => !char.IsWhiteSpace(c)))),
                ModsFolderService.RootFolderError.Reserved => string.Format(L("library.mods_root_reserved"), shown),
                ModsFolderService.RootFolderError.NoResourceCfg => L("library.mods_root_no_cfg"),
                _ => string.Format(L("library.mods_create_folder_failed"), shown)
            };
            await DialogHelper.ShowErrorAsync(L("library.mods"), message);
            return null;
        }
        if (_modsRootNode != null) _modsRootNode.IsExpanded = true;
        await LoadDataAsync();
        return result.RelativePath;
    }

    // Selecting the Sets header shows every library item, like the Mods Folder header shows every Mods item.
    private static IEnumerable<ItemViewModel> FilterBySetsHeader(IEnumerable<ItemViewModel> query) =>
        query.Where(i => !i.Entity.IsModsItem);
}
