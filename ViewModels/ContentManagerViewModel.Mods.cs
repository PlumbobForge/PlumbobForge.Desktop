using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

// The "Mods Folder" section of the sets sidebar: the real folders in the game's Mods folder that hold files
// Resource.cfg loads. It sits below the sets and only shows when there are Mods items.
public partial class ContentManagerViewModel
{
    // Mods-only storage: no sets, the sidebar only lists the Mods folder.
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(ShowSetsTopBar))]
    private bool _isModsOnly;

    private SetNodeViewModel? _modsRootNode;
    private List<ModsFolderEntity> _modsFolders = new();
    private HashSet<string> _modsRootFolderPaths = new(StringComparer.OrdinalIgnoreCase);

    private async Task<List<ModsFolderEntity>> LoadModsFoldersAsync(AppDbContext db)
    {
        try
        {
            using var scope = App.Services!.CreateScope();
            _modsRootFolderPaths = scope.ServiceProvider.GetRequiredService<ModsFolderService>().GetRootFolderPaths();
        }
        catch
        {
            _modsRootFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        if (!PlumbobForge.Backend.Configuration.ModsFolderSupport.IsEnabled) return new List<ModsFolderEntity>();
        return await db.ModsFolders.AsNoTracking().ToListAsync();
    }

    private long? DefaultModsImportFolderId() =>
        _modsFolders.FirstOrDefault(f => string.Equals(f.RelativePath, ModsFolderService.DefaultImportFolder, StringComparison.OrdinalIgnoreCase))?.Id;

    // Rebuilds the Mods nodes from _modsFolders and puts the section at the end of the tree (or removes it).
    // restore: the Mods node that was selected before the tree was rebuilt (defaults to the current selection).
    private void PlaceModsNode(bool hasModsItems, SetNodeViewModel? restore = null)
    {
        var previous = restore ?? SelectedSetNode;
        if (_modsRootNode != null) RootSets.Remove(_modsRootNode);
        // Mods-only storage has no library, so its (empty) sets are not shown.
        if (IsModsOnly)
        {
            foreach (var setNode in RootSets.Where(n => !n.IsSpecialNode).ToList()) RootSets.Remove(setNode);
        }
        // Shown while there are Mods items or folders Resource.cfg loads from.
        if (!hasModsItems && _modsFolders.Count == 0)
        {
            if (SelectedSetNode?.IsModsNode == true) SelectedSetNode = RootSets.FirstOrDefault(n => n.IsAllItems);
            if (SelectedSetNode != null && !SelectedSetNode.IsSelected) SelectedSetNode.IsSelected = true;
            _modsRootNode = null;
            PlaceSectionHeaders();
            return;
        }

        long? selectedFolderId = previous?.ModsFolder?.Id;
        bool rootSelected = previous?.IsModsRoot == true;

        _modsRootNode ??= new SetNodeViewModel(LocalizationManager.Instance.GetString("library.mods"))
        {
            IsModsRoot = true,
            Icon = "RegularJoystick"
        };
        _modsRootNode.Children.Clear();

        var nodes = _modsFolders.ToDictionary(f => f.Id, f => new SetNodeViewModel(f) { IsModsCfgRoot = _modsRootFolderPaths.Contains(f.RelativePath) });
        foreach (var folder in _modsFolders)
        {
            var node = nodes[folder.Id];
            node.ExpandedChanged += OnModsFolderExpandedChanged;
            if (folder.ParentModsFolderId.HasValue && nodes.TryGetValue(folder.ParentModsFolderId.Value, out var parent))
                parent.Children.Add(node);
            else
                _modsRootNode.Children.Add(node);
        }
        SortModsChildren(_modsRootNode);
        RootSets.Add(_modsRootNode);
        PlaceSectionHeaders();

        SetNodeViewModel? reselect = rootSelected ? _modsRootNode
            : selectedFolderId.HasValue ? FindModsNode(_modsRootNode, selectedFolderId.Value) ?? _modsRootNode
            : null;
        if (reselect != null)
        {
            if (SelectedSetNode != null && !ReferenceEquals(SelectedSetNode, reselect)) SelectedSetNode.IsSelected = false;
            SelectedSetNode = reselect;
            reselect.IsSelected = true;
        }
    }

    // Folders always sort by name: their order on disk is the order the game reads them in.
    private static void SortModsChildren(SetNodeViewModel node)
    {
        var sorted = node.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        node.Children.Clear();
        foreach (var child in sorted)
        {
            SortModsChildren(child);
            node.Children.Add(child);
        }
    }

    private static SetNodeViewModel? FindModsNode(SetNodeViewModel node, long folderId)
    {
        if (node.ModsFolder?.Id == folderId) return node;
        foreach (var child in node.Children)
        {
            var found = FindModsNode(child, folderId);
            if (found != null) return found;
        }
        return null;
    }

    // Selects a Mods item's folder (opening the folders above it) and the item in it. False when it isn't a Mods item.
    private bool TrySelectModsItem(long itemId)
    {
        var item = Items.FirstOrDefault(i => i.Entity.Id == itemId);
        if (item == null || !item.Entity.IsModsItem || _modsRootNode == null) return false;

        var path = new List<SetNodeViewModel>();
        if (item.Entity.ModsFolderId is long folderId) FindModsPath(_modsRootNode, folderId, path);
        var node = path.Count > 0 ? path[^1] : _modsRootNode;
        _modsRootNode.IsExpanded = true;
        foreach (var ancestor in path.Take(path.Count - 1)) ancestor.IsExpanded = true;

        DeselectAllSets();
        node.IsSelected = true;
        SelectedSetNode = node;
        SelectItemAndScroll(itemId);
        return true;
    }

    private static bool FindModsPath(SetNodeViewModel node, long folderId, List<SetNodeViewModel> path)
    {
        foreach (var child in node.Children)
        {
            path.Add(child);
            if (child.ModsFolder?.Id == folderId || FindModsPath(child, folderId, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    private void ClearModsHighlight()
    {
        // The Sets header's highlight goes too: a section view is replaced by whatever is picked next.
        if (_setsHeaderNode != null) _setsHeaderNode.IsSelected = false;
        if (_modsRootNode == null) return;
        _modsRootNode.IsSelected = false;
        foreach (var node in _modsRootNode.GetAllDescendants()) node.IsSelected = false;
    }

    private void RecalculateModsCounts()
    {
        if (_modsRootNode == null) return;
        var counts = new Dictionary<long, int>();
        int total = 0;
        foreach (var item in Items)
        {
            if (!item.Entity.IsModsItem) continue;
            total++;
            if (item.Entity.ModsFolderId.HasValue)
                counts[item.Entity.ModsFolderId.Value] = counts.GetValueOrDefault(item.Entity.ModsFolderId.Value, 0) + 1;
        }
        _modsRootNode.ItemCount = total;
        _modsRootNode.CalculateModsItemCount(counts);
    }

    private void OnModsFolderExpandedChanged(SetNodeViewModel node, bool expanded)
    {
        if (node.ModsFolder == null) return;
        long id = node.ModsFolder.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = App.Services!.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ModsFolderService>().SetFolderExpandedAsync(id, expanded);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("Could not save a Mods folder's expanded state", ex, "ContentManagerViewModel");
            }
        });
    }

    // Items shown for a selected Mods node: everything in Mods for the section header, else the folder's own items.
    private static IEnumerable<ItemViewModel> FilterByModsNode(IEnumerable<ItemViewModel> query, SetNodeViewModel node)
    {
        if (node.IsModsRoot) return query.Where(i => i.Entity.IsModsItem);
        long folderId = node.ModsFolder!.Id;
        return query.Where(i => i.Entity.IsModsItem && i.Entity.ModsFolderId == folderId);
    }

    public async Task CustomizeModsFolderAsync(SetNodeViewModel node)
    {
        if (node.ModsFolder == null) return;
        var result = await DialogHelper.ShowCustomizeSetAsync(node.Name, node.ModsFolder.Icon, node.ModsFolder.Color, nameEditable: node.CanRenameModsFolder, isModsFolder: true);
        if (result == null || !result.Confirmed) return;

        try
        {
            using var scope = App.Services!.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ModsFolderService>();
            string newName = result.Name?.Trim() ?? string.Empty;
            if (node.CanRenameModsFolder && newName.Length > 0 && newName != node.Name)
            {
                if (await service.RenameFolderAsync(node.ModsFolder.Id, newName))
                {
                    node.Name = newName;
                    node.ModsFolder.Name = newName;
                }
                else
                {
                    await DialogHelper.ShowErrorAsync(L("library.mods"), string.Format(L("library.mods_rename_folder_failed"), newName));
                }
            }
            var saved = await service.UpdateFolderAsync(node.ModsFolder.Id, result.Icon, result.Color, node.ModsFolder.Description);
            if (saved == null) return;
            node.ModsFolder.Icon = saved.Icon;
            node.ModsFolder.Color = saved.Color;
            node.Icon = string.IsNullOrWhiteSpace(saved.Icon) ? "RegularFolder" : saved.Icon;
            node.Color = saved.Color;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not save the Mods folder's look", ex, "ContentManagerViewModel");
        }
    }

    private static string L(string key) => LocalizationManager.Instance.GetString(key);

    public async Task AddModsFolderAsync(SetNodeViewModel parent)
    {
        if (parent.ModsFolder == null) return;
        var name = await DialogHelper.ShowInputAsync(L("library.mods_new_folder"), L("library.mods_new_folder_prompt"), L("library.mods_new_folder_default"));
        if (string.IsNullOrWhiteSpace(name)) return;

        string? created;
        using (var scope = App.Services!.CreateScope())
        {
            created = await scope.ServiceProvider.GetRequiredService<ModsFolderService>().CreateFolderAsync(parent.ModsFolder.Id, name);
        }
        if (created == null)
        {
            await DialogHelper.ShowErrorAsync(L("library.mods"), string.Format(L("library.mods_create_folder_failed"), name.Trim()));
            return;
        }
        parent.IsExpanded = true;
        await LoadDataAsync();
    }

    public async Task DeleteModsFolderAsync(SetNodeViewModel node)
    {
        if (node.ModsFolder == null || !node.CanDeleteModsFolder) return;
        string folderPrefix = Path.Combine(ModsDirForUi(), node.ModsFolder.RelativePath.Replace('/', Path.DirectorySeparatorChar)) + Path.DirectorySeparatorChar;
        int count = Items.Count(i => i.Entity.IsModsItem && i.Entity.CompleteFileName.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
        var confirm = await DialogHelper.ShowDeleteConfirmAsync(string.Format(L("library.mods_delete_folder_confirm"), node.Name, count));
        if (!confirm.Confirmed) return;

        bool ok;
        using (var scope = App.Services!.CreateScope())
        {
            ok = await scope.ServiceProvider.GetRequiredService<ModsFolderService>().DeleteFolderAsync(node.ModsFolder.Id);
        }
        if (!ok) await DialogHelper.ShowErrorAsync(L("library.mods"), string.Format(L("library.mods_delete_folder_failed"), node.Name));
        await LoadDataAsync();
    }

    private static string ModsDirForUi()
    {
        using var scope = App.Services!.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ModsFolderService>().GetModsFolderPath();
    }

    // Items dropped on a Mods folder: every selected item goes into that folder.
    public async Task MoveSelectedToModsFolderAsync(SetNodeViewModel folderNode)
    {
        if (folderNode.ModsFolder == null) return;
        var movedItems = Items.Where(i => i.IsSelected).ToList();
        var ids = movedItems.Select(i => i.Entity.Id).ToList();
        if (ids.Count == 0) return;
        // Mods files moved between folders can go back; library items moved into Mods can't simply.
        var previousFolders = movedItems.All(i => i.Entity.IsModsItem && i.Entity.ModsFolderId.HasValue)
            ? movedItems.GroupBy(i => i.Entity.ModsFolderId!.Value).ToDictionary(g => g.Key, g => g.Select(i => i.Entity.Id).ToList())
            : null;

        ModsFolderService.ActionResult result;
        IsLoading = true;
        try
        {
            long folderId = folderNode.ModsFolder.Id;
            result = await Task.Run(async () =>
            {
                using var scope = App.Services!.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<ModsFolderService>().MoveToFolderAsync(ids, folderId);
            });
        }
        finally
        {
            IsLoading = false;
        }
        await LoadDataAsync();
        NotifyDirtyStateChanged();
        string folderName = folderNode.Name;
        Func<Task>? undo = previousFolders == null ? null : async () =>
        {
            foreach (var (folderId, back) in previousFolders)
            {
                var node = ModsFolderNodes().FirstOrDefault(n => n.ModsFolder!.Id == folderId);
                if (node != null) await RunOnItemsAsync(back, () => MoveSelectedToModsFolderAsync(node));
            }
        };
        ShowToast(string.Format(L("library.toast_moved"), ItemsPhrase(movedItems), folderName), undo);
        await ShowModsSkippedAsync(result);
    }

    // Mods files dropped on a set (or sent there from the warning badge) are imported into that set.
    private async Task<ModsFolderService.ActionResult> MoveModsItemsToSetAsync(List<long> ids, long setId)
    {
        if (ids.Count == 0) return ModsFolderService.ActionResult.Nothing;
        IsLoading = true;
        try
        {
            return await Task.Run(async () =>
            {
                using var scope = App.Services!.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<ModsFolderService>().MoveToSetAsync(ids, setId);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task MoveModsItemToSetAsync(ItemViewModel item, SetsEntity set)
    {
        var result = await MoveModsItemsToSetAsync(new List<long> { item.Entity.Id }, set.Id);
        await LoadDataAsync();
        NotifyDirtyStateChanged();
        await ShowModsSkippedAsync(result);
    }

    private static async Task ShowModsSkippedAsync(ModsFolderService.ActionResult result)
    {
        if (result.Skipped.Count == 0) return;
        var lines = result.Skipped.Take(8).Select(s => $"\u2022 {s.FileName}: {L("library.mods_skip_" + s.Reason.ToString().ToLowerInvariant())}").ToList();
        if (result.Skipped.Count > 8) lines.Add($"(+{result.Skipped.Count - 8})");
        await DialogHelper.ShowErrorAsync(L("library.mods"), L("library.mods_skipped_intro") + "\n\n" + string.Join("\n", lines));
    }

    private async Task RenameModsItemAsync(ItemViewModel item)
    {
        string plain = item.FileName;
        string ext = Path.GetExtension(plain);
        var newName = await DialogHelper.ShowInputAsync("Rename Item", $"Enter new filename for '{plain}':", Path.GetFileNameWithoutExtension(plain), ext);
        if (string.IsNullOrWhiteSpace(newName) || newName == plain) return;

        bool ok;
        using (var scope = App.Services!.CreateScope())
        {
            ok = await scope.ServiceProvider.GetRequiredService<ModsFolderService>().RenameItemAsync(item.Entity.Id, newName);
        }
        if (!ok)
        {
            await DialogHelper.ShowErrorAsync("Rename Error", "A file with this name already exists, or the file could not be renamed.");
            return;
        }
        await LoadDataAsync();
    }

    // Mods files go to the Recycle Bin. Returns how many were removed.
    private async Task<int> DeleteModsItemsAsync(List<ItemViewModel> items)
    {
        if (items.Count == 0) return 0;
        var ids = items.Select(i => i.Entity.Id).ToList();
        ModsFolderService.ActionResult result;
        using (var scope = App.Services!.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<ModsFolderService>().DeleteItemsAsync(ids);
        }
        await ShowModsSkippedAsync(result);
        return result.Done;
    }

    // Turns Mods items on or off; their files are renamed (".disabled") as far as the active configuration lets them.
    // Returns the names that could not be renamed.
    private async Task<List<string>> SetModsItemsEnabledAsync(IEnumerable<ItemViewModel> items, bool enabled)
    {
        var failed = new List<string>();
        var list = items.Where(i => i.Entity.IsModsItem).ToList();
        if (list.Count == 0) return failed;

        var results = await Task.Run(async () =>
        {
            using var scope = App.Services!.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ModsFolderService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var outcome = new List<(ItemViewModel Item, bool Ok, string Path)>();
            foreach (var item in list)
            {
                // A file the active configuration keeps off stays off on disk; the item is still on, like a library
                // item in a set the configuration leaves out.
                bool ok = await service.SetEnabledAsync(item.Entity.Id, enabled) != ModsFolderService.ToggleOutcome.Failed;
                string path = await db.AllMetaEntities.AsNoTracking().Where(m => m.Id == item.Entity.Id)
                    .Select(m => m.CompleteFileName).FirstOrDefaultAsync() ?? item.Entity.CompleteFileName;
                outcome.Add((item, ok, path));
            }
            return outcome;
        });

        foreach (var (item, ok, path) in results)
        {
            if (ok)
            {
                item.Entity.Enabled = enabled;
                item.Entity.CompleteFileName = path;
            }
            else
            {
                failed.Add(item.FileName);
            }
            item.NotifyEntityChanged();
        }
        return failed;
    }

    private static async Task ShowModsToggleFailedAsync(List<string> failed)
    {
        if (failed.Count == 0) return;
        string names = failed.Count <= 3 ? string.Join(", ", failed) : $"{string.Join(", ", failed.Take(3))} (+{failed.Count - 3})";
        await DialogHelper.ShowErrorAsync(LocalizationManager.Instance.GetString("library.mods"),
            string.Format(LocalizationManager.Instance.GetString("library.mods_toggle_failed"), names));
    }
}
