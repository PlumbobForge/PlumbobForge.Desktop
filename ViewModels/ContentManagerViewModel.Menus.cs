using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Views.Dialogs;
using S3ForgeTools.GameFiles.Package;

namespace PlumbobForge.Desktop.ViewModels;

// What the item and sidebar context menus need when they open, and the actions only they use.
public partial class ContentManagerViewModel
{
    public sealed record MenuTarget(string Label, long Id);

    // State is true when everything in question is in the collection, false when none is, null for some.
    public sealed record CollectionMenuEntry(ulong Id, string Name, bool? State);

    public sealed class ItemMenuState
    {
        public List<ItemViewModel> Selected { get; init; } = new();
        public int Count => Selected.Count;
        public bool AllEnabled { get; init; }
        public bool AnyNotFavorite { get; init; }
        public bool AnyMods { get; init; }
        public bool AnyLibrary { get; init; }
        public string? RevealPath { get; init; }
        public List<MenuTarget> Sets { get; init; } = new();
        public List<SetNodeViewModel> Folders { get; init; } = new();
        public List<CollectionMenuEntry> Collections { get; init; } = new();
    }

    public ItemMenuState? GetItemMenuState()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return null;
        var libraryIds = selected.Where(i => !i.Entity.IsModsItem).Select(i => i.Entity.Id).ToList();
        string? reveal = selected.Count == 1 && File.Exists(selected[0].Entity.CompleteFileName) ? selected[0].Entity.CompleteFileName : null;

        // Move targets leave out where everything already is.
        var currentSets = selected.Where(i => !i.Entity.IsModsItem).Select(i => i.Entity.SetsEntityId).Distinct().ToList();
        var currentFolders = selected.Where(i => i.Entity.IsModsItem).Select(i => i.Entity.ModsFolderId).Distinct().ToList();
        bool allLibrary = libraryIds.Count == selected.Count;
        bool allMods = libraryIds.Count == 0;

        return new ItemMenuState
        {
            Selected = selected,
            AllEnabled = selected.All(i => i.Enabled),
            AnyNotFavorite = selected.Any(i => !i.IsFavorite),
            AnyMods = !allLibrary,
            AnyLibrary = !allMods,
            RevealPath = reveal,
            Sets = IsModsOnly ? new() : SetMoveTargets().Where(t => !(allLibrary && currentSets.Count == 1 && currentSets[0] == t.Id)).ToList(),
            Folders = !ModsFolderSupport.IsEnabled ? new() : ModsFolderNodes().Where(n => !(allMods && currentFolders.Count == 1 && currentFolders[0] == n.ModsFolder!.Id)).ToList(),
            Collections = libraryIds.Count == 0 ? new() : GetCollectionEntries(c => c.CollectionItems.Select(ci => ci.MetaEntityId), libraryIds),
        };
    }

    public List<CollectionMenuEntry> GetSetCollectionEntries(IReadOnlyCollection<long> setIds) =>
        GetCollectionEntries(c => c.CollectionSets.Select(cs => cs.SetsEntityId), setIds.ToList());

    // Only collections PlumbobForge made; the game's own and other tools' are edited in Tools > Collections.
    private List<CollectionMenuEntry> GetCollectionEntries(Func<CollectionEntity, IEnumerable<long>> members, List<long> ids)
    {
        try
        {
            using var scope = App.Services!.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var collections = db.Collections.AsNoTracking()
                .Include(c => c.CollectionSets)
                .Include(c => c.CollectionItems)
                .Where(c => c.IsPlumbobForge)
                .ToList();
            return collections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Select(c =>
            {
                var inside = members(c).ToHashSet();
                int hits = ids.Count(inside.Contains);
                bool? state = hits == 0 ? false : hits == ids.Count ? true : null;
                return new CollectionMenuEntry(c.Id, c.Name, state);
            }).ToList();
        }
        catch (Exception ex)
        {
            // An older database without the collections tables has nothing to offer here.
            AppLogger.LogWarning("Could not read collections for the menu", ex, "ContentManagerViewModel");
            return new();
        }
    }

    // Sets in sidebar order; subsets read "Parent / Child".
    public List<MenuTarget> SetMoveTargets()
    {
        var list = new List<MenuTarget>();
        void Walk(IEnumerable<SetNodeViewModel> nodes, string prefix)
        {
            foreach (var n in nodes.Where(n => n.Id.HasValue && !n.IsSpecialNode && !n.IsModsNode))
            {
                string label = prefix.Length == 0 ? n.Name : prefix + " / " + n.Name;
                list.Add(new MenuTarget(label, n.Id!.Value));
                Walk(n.Children, label);
            }
        }
        Walk(RootSets, string.Empty);
        return list;
    }

    private List<long> _recentMoveSetIds = new();

    // Up to three sets moved to lately that still exist, newest first.
    public List<MenuTarget> RecentMoveTargets(IReadOnlyCollection<MenuTarget> targets) =>
        _recentMoveSetIds.Select(id => targets.FirstOrDefault(t => t.Id == id)).Where(t => t != null).Cast<MenuTarget>().Take(3).ToList();

    private void NoteMoveTarget(long setId)
    {
        _recentMoveSetIds.Remove(setId);
        _recentMoveSetIds.Insert(0, setId);
        if (_recentMoveSetIds.Count > 10) _recentMoveSetIds.RemoveRange(10, _recentMoveSetIds.Count - 10);
        SaveCurrentUiState();
    }

    public List<SetNodeViewModel> ModsFolderNodes() => AllSidebarNodes().Where(n => n.IsModsFolder).ToList();

    public string ModsFolderLabel(SetNodeViewModel node) => node.ModsFolder?.RelativePath.Replace("/", " / ") ?? node.Name;

    public async Task MoveSelectedToSetIdAsync(long setId)
    {
        var target = await _db.SetsEntities.FindAsync(setId);
        if (target != null) await MoveSelectedToSetAsync(target);
    }

    // Items a sidebar entry holds directly, for Look Inside and the optimizer.
    public List<ItemViewModel> ItemsOfNode(SetNodeViewModel node)
    {
        if (node.IsModsFolder) return Items.Where(i => i.Entity.IsModsItem && i.Entity.ModsFolderId == node.ModsFolder!.Id).ToList();
        if (node.Id.HasValue) return Items.Where(i => !i.Entity.IsModsItem && i.Entity.SetsEntityId == node.Id.Value).ToList();
        return new();
    }

    public async Task LookInsideItemsAsync(IReadOnlyCollection<ItemViewModel> items)
    {
        if (items.Count == 0) return;
        var saved = await DialogHelper.ShowLookInsideAsync(items.Select(i => (i.Id, i.FileName)).ToList());
        if (saved.Count > 0) await RefreshEditedItemsAsync(saved);
    }

    public async Task OpenInOptimizerAsync(IReadOnlyCollection<long> itemIds)
    {
        if (itemIds.Count == 0) return;
        var main = App.Services?.GetService<MainViewModel>();
        main?.NavigateToTools();
        var health = App.Services?.GetService<HealthViewModel>();
        if (health != null) await health.OpenInOptimizerAsync(itemIds);
    }

    // Tools > Mesh Report for just these items, under a chip with the label.
    public async Task OpenInMeshReportAsync(IReadOnlyCollection<long> itemIds, string label)
    {
        if (itemIds.Count == 0) return;
        App.Services?.GetService<MainViewModel>()?.NavigateToTools();
        var health = App.Services?.GetService<HealthViewModel>();
        if (health != null) await health.ShowMeshesForAsync(itemIds, label);
    }

    public static void RevealInExplorer(string path, bool select)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = select ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("Could not open File Explorer", ex, "ContentManagerViewModel");
        }
    }

    public string ModsFolderFullPath(SetNodeViewModel node) =>
        Path.Combine(ModsDirForUi(), (node.ModsFolder?.RelativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));

    // Adds the items or sets to a collection, or takes them out when all of them are in it already.
    public async Task ToggleCollectionMembershipAsync(CollectionMenuEntry entry, IReadOnlyCollection<long> itemIds, IReadOnlyCollection<long> setIds)
    {
        var before = await ReadCollectionMembersAsync(entry.Id);
        if (before == null) return;
        bool remove = entry.State == true;
        var sets = before.Value.Sets.ToList();
        var items = before.Value.Items.ToList();
        if (remove)
        {
            sets.RemoveAll(setIds.Contains);
            items.RemoveAll(itemIds.Contains);
        }
        else
        {
            sets.AddRange(setIds.Where(id => !sets.Contains(id)));
            items.AddRange(itemIds.Where(id => !items.Contains(id)));
        }
        if (!await SaveCollectionMembersAsync(entry.Id, sets, items)) return;

        string what = setIds.Count > 0
            ? (setIds.Count == 1 ? AllSidebarNodes().FirstOrDefault(n => n.Id == setIds.First())?.Name ?? string.Empty : string.Format(L("library.toast_items"), setIds.Count))
            : ItemsPhrase(Items.Where(i => itemIds.Contains(i.Entity.Id)).ToList());
        ShowToast(string.Format(L(remove ? "library.toast_collection_removed" : "library.toast_collection_added"), what, entry.Name),
            async () => { await SaveCollectionMembersAsync(entry.Id, before.Value.Sets, before.Value.Items); });
    }

    private static async Task<(List<long> Sets, List<long> Items)?> ReadCollectionMembersAsync(ulong id)
    {
        using var scope = App.Services!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var collection = await db.Collections.AsNoTracking()
            .Include(c => c.CollectionSets)
            .Include(c => c.CollectionItems)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (collection == null) return null;
        return (collection.CollectionSets.Select(cs => cs.SetsEntityId).ToList(), collection.CollectionItems.Select(ci => ci.MetaEntityId).ToList());
    }

    // Rewrites the collection (database and the game's file) with these members; name, icon and catalog hiding stay.
    private async Task<bool> SaveCollectionMembersAsync(ulong id, List<long> sets, List<long> items)
    {
        try
        {
            using var scope = App.Services!.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var collection = await db.Collections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (collection == null) return false;
            var service = scope.ServiceProvider.GetRequiredService<Sims3CollectionService>();
            var key = new TGI_Key(collection.IconType, collection.IconGroup, collection.IconInstance);
            var result = await service.SaveCollectionAsync(collection.Id, collection.Name, key, sets, collection.HideFromCatalog, includedItemIds: items);
            if (result.Success) return true;
            await DialogHelper.ShowErrorAsync(L("library.menu_collections"), result.ErrorMessage ?? string.Empty);
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not change a collection from the menu", ex, "ContentManagerViewModel");
            await DialogHelper.ShowErrorAsync(L("library.menu_collections"), ex.Message);
            return false;
        }
    }

    // Opens Tools > Collections with a new collection holding these sets and items.
    public async Task NewCollectionWithAsync(IReadOnlyCollection<long> setIds, IReadOnlyCollection<long> itemIds)
    {
        App.Services?.GetService<MainViewModel>()?.NavigateToTools();
        var health = App.Services?.GetService<HealthViewModel>();
        if (health == null) return;
        health.SelectSection("Collections");
        await health.CreateNewCollectionWithAsync(setIds, itemIds);
    }
}
