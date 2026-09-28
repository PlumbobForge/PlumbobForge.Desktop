using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ContentManagerViewModel
{
    [RelayCommand]
    public void SetSortMode(string sortMode)
    {
        CurrentSetSort = sortMode;
        SortSetsTree();
    }

    public void SortSetsTree()
    {
        if (RootSets.Count <= 1) return;

        var allItemsNode = RootSets.FirstOrDefault(n => n.IsAllItems);
        var otherNodes = RootSets.Where(n => !n.IsAllItems).ToList();

        otherNodes = SortNodeList(otherNodes);

        RootSets.Clear();
        if (allItemsNode != null)
        {
            RootSets.Add(allItemsNode);
        }
        foreach (var node in otherNodes)
        {
            SortChildNodesRecursive(node);
            RootSets.Add(node);
        }
    }

    private List<SetNodeViewModel> SortNodeList(List<SetNodeViewModel> nodes)
    {
        return CurrentSetSort switch
        {
            "NameAsc" => nodes.OrderBy(n => n.Name).ToList(),
            "NameDesc" => nodes.OrderByDescending(n => n.Name).ToList(),
            _ => nodes.OrderByDescending(n => n.Id ?? 0).ToList()
        };
    }

    private void SortChildNodesRecursive(SetNodeViewModel parentNode)
    {
        if (parentNode.Children.Count <= 1) return;

        var sortedChildren = SortNodeList(parentNode.Children.ToList());
        parentNode.Children.Clear();
        foreach (var child in sortedChildren)
        {
            SortChildNodesRecursive(child);
            parentNode.Children.Add(child);
        }
    }

    public async Task ReloadSetsTreeAsync(long? selectedSetIdToRestore = null)
    {
        var setsList = await _db.SetsEntities.AsNoTracking().ToListAsync();

        FlatSets.Clear();
        foreach (var set in setsList)
        {
            FlatSets.Add(set);
        }

        var allItemsNode = RootSets.FirstOrDefault(n => n.IsAllItems) ?? new SetNodeViewModel("All Items", null);

        RootSets.Clear();
        RootSets.Add(allItemsNode);

        var nodeMap = setsList.ToDictionary(s => s.Id, s => new SetNodeViewModel(s));
        foreach (var set in setsList)
        {
            var node = nodeMap[set.Id];
            if (set.ParentSetsEntityId.HasValue && nodeMap.TryGetValue(set.ParentSetsEntityId.Value, out var parentNode))
            {
                parentNode.Children.Add(node);
            }
            else
            {
                RootSets.Add(node);
            }
        }

        SortSetsTree();
        RecalculateSetCounts();

        foreach (var root in RootSets)
        {
            RegisterNodeSelectionHandler(root);
        }
        UpdateSelectedSetsCount();

        long? targetId = selectedSetIdToRestore ?? SelectedSetNode?.Id;
        if (targetId.HasValue)
        {
            var targetNode = FindNodeById(RootSets, targetId.Value);
            SelectedSetNode = targetNode ?? allItemsNode;
        }
        else
        {
            SelectedSetNode = allItemsNode;
        }

        var configVm = App.Services?.GetService<ConfigurationsViewModel>();
        if (configVm != null)
        {
            _ = configVm.LoadDataAsync();
        }
    }

    public void SelectSetById(long setId, long? selectItemId = null)
    {
        var targetNode = FindNodeById(RootSets, setId);
        if (targetNode != null)
        {
            ExpandAncestorsRecursive(RootSets, setId);
            DeselectAllSets();
            targetNode.IsSelected = true;
            SelectedSetNode = targetNode;
        }

        if (selectItemId.HasValue)
        {
            DeselectAll();
            var targetItem = Items.FirstOrDefault(i => i.Entity.Id == selectItemId.Value);
            if (targetItem != null)
            {
                targetItem.IsSelected = true;
                UpdateSelectionState();
            }
        }
    }

    private bool ExpandAncestorsRecursive(IEnumerable<SetNodeViewModel> nodes, long targetId)
    {
        foreach (var node in nodes)
        {
            if (node.Id == targetId) return true;

            if (node.Children.Count > 0 && ExpandAncestorsRecursive(node.Children, targetId))
            {
                node.IsExpanded = true;
                return true;
            }
        }
        return false;
    }

    public void UpdateSelectedSetsCount()
    {
        SelectedSetsCount = GetSelectedSetNodes().Count;
        RequestFilterUpdate(0);
    }

    private void RegisterNodeSelectionHandler(SetNodeViewModel node)
    {
        node.SelectionChanged -= UpdateSelectedSetsCount;
        node.SelectionChanged += UpdateSelectedSetsCount;
        foreach (var child in node.Children)
        {
            RegisterNodeSelectionHandler(child);
        }
    }

    public List<SetNodeViewModel> GetSelectedSetNodes()
    {
        var result = new List<SetNodeViewModel>();
        void Collect(IEnumerable<SetNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsAllItems && node.IsSelected)
                {
                    result.Add(node);
                }
                Collect(node.Children);
            }
        }
        Collect(RootSets);
        return result;
    }

    private SetNodeViewModel? _lastSelectedSetNode;

    public List<SetNodeViewModel> GetVisibleSetNodes()
    {
        var result = new List<SetNodeViewModel>();
        foreach (var root in RootSets)
        {
            CollectVisible(root, result);
        }
        return result;

        static void CollectVisible(SetNodeViewModel node, List<SetNodeViewModel> list)
        {
            if (!node.IsAllItems)
            {
                list.Add(node);
            }
            if (node.IsExpanded)
            {
                foreach (var child in node.Children)
                {
                    CollectVisible(child, list);
                }
            }
        }
    }

    public void SelectSetNode(SetNodeViewModel setNode, bool isCtrlPressed, bool isShiftPressed)
    {
        if (setNode.IsAllItems)
        {
            SelectedSetNode = setNode;
            DeselectAllSets();
            _lastSelectedSetNode = null;
            return;
        }

        var visibleSets = GetVisibleSetNodes();

        if (isCtrlPressed)
        {
            setNode.IsSelected = !setNode.IsSelected;
            if (setNode.IsSelected)
            {
                _lastSelectedSetNode = setNode;
                SelectedSetNode = setNode;
            }
        }
        else if (isShiftPressed)
        {
            int anchorIndex = -1;
            if (_lastSelectedSetNode != null)
            {
                anchorIndex = visibleSets.IndexOf(_lastSelectedSetNode);
            }

            if (anchorIndex == -1)
            {
                for (int i = 0; i < visibleSets.Count; i++)
                {
                    if (visibleSets[i].IsSelected)
                    {
                        anchorIndex = i;
                        break;
                    }
                }
            }

            int currentIndex = visibleSets.IndexOf(setNode);

            if (anchorIndex != -1 && currentIndex != -1)
            {
                int start = Math.Min(anchorIndex, currentIndex);
                int end = Math.Max(anchorIndex, currentIndex);
                for (int i = 0; i < visibleSets.Count; i++)
                {
                    visibleSets[i].IsSelected = (i >= start && i <= end);
                }
                SelectedSetNode = setNode;
            }
            else
            {
                DeselectAllSets();
                setNode.IsSelected = true;
                _lastSelectedSetNode = setNode;
                SelectedSetNode = setNode;
            }
        }
        else
        {
            DeselectAllSets();
            setNode.IsSelected = true;
            _lastSelectedSetNode = setNode;
            SelectedSetNode = setNode;
        }

        UpdateSelectedSetsCount();
    }

    public void DeselectAllSets()
    {
        _lastSelectedSetNode = null;
        void ClearSelection(IEnumerable<SetNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                node.IsSelected = false;
                ClearSelection(node.Children);
            }
        }
        ClearSelection(RootSets);
        UpdateSelectedSetsCount();
    }

    public async Task MoveSetsAsync(List<long> setIdsToMove, long? targetParentSetId)
    {
        if (setIdsToMove == null || setIdsToMove.Count == 0) return;

        var idsList = setIdsToMove.Distinct().ToList();

        // If target parent is specified:
        if (targetParentSetId.HasValue)
        {
            // Target cannot be one of the sets being moved
            if (idsList.Contains(targetParentSetId.Value)) return;

            // Target cannot be a descendant of any of the sets being moved (prevent circular reference loops)
            foreach (var setId in idsList)
            {
                var movingNode = FindNodeById(RootSets, setId);
                if (movingNode != null && movingNode.ContainsDescendant(targetParentSetId.Value))
                {
                    return;
                }
            }
        }

        // Check if moving any set would cause a name collision under targetParentSetId
        var existingNamesUnderTarget = await _db.SetsEntities
            .Where(s => s.ParentSetsEntityId == targetParentSetId && !idsList.Contains(s.Id))
            .Select(s => s.Name.ToLower())
            .ToListAsync();

        bool anyModified = false;
        foreach (var setId in idsList)
        {
            var entity = await _db.SetsEntities.FindAsync(setId);
            if (entity != null)
            {
                if (entity.ParentSetsEntityId != targetParentSetId)
                {
                    var existingCollisionSet = await _db.SetsEntities.FirstOrDefaultAsync(s => s.Id != entity.Id && s.ParentSetsEntityId == targetParentSetId && s.Name.ToLower() == entity.Name.ToLower());
                    if (existingCollisionSet != null)
                    {
                        string suggestedName = GetNextAvailableSetName(entity.Name, targetParentSetId, _db, entity.Id);
                        var actionResult = await DialogHelper.ShowMoveSetCollisionDialogAsync(
                            $"A set named '{entity.Name}' already exists in the destination.",
                            suggestedName);

                        if (actionResult.Action == MoveSetCollisionAction.Cancel)
                        {
                            continue;
                        }
                        else if (actionResult.Action == MoveSetCollisionAction.Rename)
                        {
                            string finalName = actionResult.RenamedName ?? suggestedName;
                            entity.Name = finalName;
                            entity.FolderName = finalName;
                            entity.LongName = finalName;
                            entity.ParentSetsEntityId = targetParentSetId;
                            entity.Dirty = true;
                            anyModified = true;
                        }
                        else if (actionResult.Action == MoveSetCollisionAction.Merge)
                        {
                            // Merge all items from entity into existingCollisionSet
                            var itemMetas = await _db.MetaEntities.Where(m => m.SetsEntityId == entity.Id).ToListAsync();
                            foreach (var meta in itemMetas)
                            {
                                meta.SetsEntityId = existingCollisionSet.Id;
                                var inMemoryItem = Items.FirstOrDefault(i => i.Entity.Id == meta.Id);
                                if (inMemoryItem != null)
                                {
                                    inMemoryItem.Entity.SetsEntityId = existingCollisionSet.Id;
                                }
                            }

                            // Merge all child sub-sets from entity into existingCollisionSet
                            var childSubSets = await _db.SetsEntities.Where(s => s.ParentSetsEntityId == entity.Id).ToListAsync();
                            foreach (var child in childSubSets)
                            {
                                bool childDuplicate = await _db.SetsEntities.AnyAsync(s => s.Id != child.Id && s.ParentSetsEntityId == existingCollisionSet.Id && s.Name.ToLower() == child.Name.ToLower());
                                if (childDuplicate)
                                {
                                    string childSuggested = GetNextAvailableSetName(child.Name, existingCollisionSet.Id, _db, child.Id);
                                    child.Name = childSuggested;
                                    child.FolderName = childSuggested;
                                    child.LongName = childSuggested;
                                }
                                child.ParentSetsEntityId = existingCollisionSet.Id;
                                child.Dirty = true;
                            }

                            // Remove old config associations and delete source entity
                            var oldConfigSets = await _db.ConfigSetsEntities.Where(cs => cs.SetsEntityId == entity.Id).ToListAsync();
                            _db.ConfigSetsEntities.RemoveRange(oldConfigSets);
                            _db.SetsEntities.Remove(entity);

                            existingCollisionSet.Dirty = true;
                            anyModified = true;
                        }
                    }
                    else
                    {
                        entity.ParentSetsEntityId = targetParentSetId;
                        entity.Dirty = true;
                        anyModified = true;
                    }
                }
            }
        }

        if (anyModified)
        {
            await _db.SaveChangesAsync();
            RecalculateSetCounts();
            await ReloadSetsTreeAsync(SelectedSetNode?.Id);
            ApplyFilter();

            // Expand the target parent if moving into a parent
            if (targetParentSetId.HasValue)
            {
                var targetNode = FindNodeById(RootSets, targetParentSetId.Value);
                if (targetNode != null)
                {
                    targetNode.IsExpanded = true;
                }
            }
            NotifyDirtyStateChanged();
        }

        DeselectAllSets();
    }

    public static string GetNextAvailableSetName(string baseName, long? parentSetId, AppDbContext db, long? excludeSetId = null)
    {
        var match = System.Text.RegularExpressions.Regex.Match(baseName.Trim(), @"^(.*?)( \(\d+\))?$");
        string rootName = match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value) ? match.Groups[1].Value.Trim() : baseName.Trim();

        var existingNames = db.SetsEntities
            .Where(s => s.ParentSetsEntityId == parentSetId && (excludeSetId == null || s.Id != excludeSetId.Value))
            .Select(s => s.Name.ToLower())
            .ToHashSet();

        int counter = 2;
        while (true)
        {
            string candidate = $"{rootName} ({counter})";
            if (!existingNames.Contains(candidate.ToLower()))
            {
                return candidate;
            }
            counter++;
        }
    }

    [RelayCommand]
    public async Task CreateTopLevelSetAsync()
    {
        var name = await DialogHelper.ShowInputAsync("Create New Set", "Enter set folder name:", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        if (!PlumbobForge.Desktop.Utils.NameValidator.IsValidName(name, out _)) return;

        var trimmed = name.Trim();
        bool duplicate = await _db.SetsEntities.AnyAsync(s => s.ParentSetsEntityId == null && s.Name.ToLower() == trimmed.ToLower());
        if (duplicate)
        {
            string suggested = GetNextAvailableSetName(trimmed, null, _db);
            var chosen = await DialogHelper.ShowDuplicateSetNameDialogAsync($"A top-level set named '{trimmed}' already exists.", suggested);
            if (string.IsNullOrWhiteSpace(chosen)) return;
            trimmed = chosen;
        }

        var newSet = new SetsEntity
        {
            Name = trimmed,
            FolderName = trimmed,
            LongName = trimmed,
            Dirty = true
        };
        _db.SetsEntities.Add(newSet);
        await _db.SaveChangesAsync();
        await ReloadSetsTreeAsync(newSet.Id);
        NotifyDirtyStateChanged();
    }

    [RelayCommand]
    public async Task AddSubSetAsync(SetNodeViewModel? parentNode)
    {
        if (parentNode == null || !parentNode.Id.HasValue) return;

        string parentName = parentNode.Name;
        var name = await DialogHelper.ShowInputAsync("Create Sub-Set", $"Enter sub-folder name under '{parentName}':", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        if (!PlumbobForge.Desktop.Utils.NameValidator.IsValidName(name, out _)) return;

        var trimmed = name.Trim();
        long? parentId = parentNode.Id;
        bool duplicate = await _db.SetsEntities.AnyAsync(s => s.ParentSetsEntityId == parentId && s.Name.ToLower() == trimmed.ToLower());
        if (duplicate)
        {
            string suggested = GetNextAvailableSetName(trimmed, parentId, _db);
            var chosen = await DialogHelper.ShowDuplicateSetNameDialogAsync($"A sub-set named '{trimmed}' already exists under '{parentName}'.", suggested);
            if (string.IsNullOrWhiteSpace(chosen)) return;
            trimmed = chosen;
        }

        var newSet = new SetsEntity
        {
            Name = trimmed,
            FolderName = trimmed,
            LongName = trimmed,
            ParentSetsEntityId = parentId,
            Dirty = true
        };
        _db.SetsEntities.Add(newSet);
        await _db.SaveChangesAsync();
        await ReloadSetsTreeAsync(newSet.Id);
        NotifyDirtyStateChanged();
    }

    [RelayCommand]
    public async Task CustomizeSetAsync(SetNodeViewModel? setNode)
    {
        if (setNode == null || !setNode.Id.HasValue || setNode.IsDefault) return;

        var setEntity = await _db.SetsEntities.FindAsync(setNode.Id.Value);
        if (setEntity == null) return;

        var result = await DialogHelper.ShowCustomizeSetAsync(setEntity.Name, setEntity.Icon, setEntity.Color);
        if (result == null || !result.Confirmed) return;

        bool hasChanged = false;

        if (!string.IsNullOrWhiteSpace(result.Name) && !result.Name.Trim().Equals(setEntity.Name, StringComparison.OrdinalIgnoreCase))
        {
            string newName = result.Name.Trim();
            bool duplicate = await _db.SetsEntities.AnyAsync(s => s.Id != setEntity.Id && s.ParentSetsEntityId == setEntity.ParentSetsEntityId && s.Name.ToLower() == newName.ToLower());
            if (duplicate)
            {
                string levelDesc = setEntity.ParentSetsEntityId.HasValue ? "under this parent set" : "at the top level";
                string suggested = GetNextAvailableSetName(newName, setEntity.ParentSetsEntityId, _db, setEntity.Id);
                var chosen = await DialogHelper.ShowDuplicateSetNameDialogAsync($"A set named '{newName}' already exists {levelDesc}.", suggested);
                if (string.IsNullOrWhiteSpace(chosen)) return;
                newName = chosen;
            }

            setEntity.Name = newName;
            setEntity.FolderName = newName;
            setEntity.LongName = newName;
            hasChanged = true;
        }

        if (result.Icon != setEntity.Icon)
        {
            setEntity.Icon = result.Icon;
            hasChanged = true;
        }

        if (result.Color != setEntity.Color)
        {
            setEntity.Color = result.Color;
            hasChanged = true;
        }

        if (hasChanged)
        {
            PlumbobForge.Backend.Services.SetDirtyTracker.UpdateDirtyState(setEntity);
            await _db.SaveChangesAsync();
            await ReloadSetsTreeAsync(setEntity.Id);
            ApplyFilter();
            NotifyDirtyStateChanged();
        }
    }

    [RelayCommand]
    public Task RenameSetAsync(SetNodeViewModel? setNode) => CustomizeSetAsync(setNode);

    [RelayCommand]
    public async Task DeleteSetAsync(SetNodeViewModel? setNode)
    {
        if (setNode == null || !setNode.Id.HasValue || setNode.IsDefault) return;

        var result = await DialogHelper.ShowDeleteSetConfirmAsync(setNode.Name);
        if (!result.Confirmed) return;

        try
        {
            var setEntity = await _db.SetsEntities.FindAsync(setNode.Id.Value);
            if (setEntity != null)
            {
                // Un-subset direct child sets so they move to the top level
                var childSets = await _db.SetsEntities.Where(s => s.ParentSetsEntityId == setEntity.Id).ToListAsync();
                foreach (var child in childSets)
                {
                    child.ParentSetsEntityId = null;
                }

                // Remove any configuration links for this set
                var configSets = await _db.ConfigSetsEntities.Where(cs => cs.SetsEntityId == setEntity.Id).ToListAsync();
                _db.ConfigSetsEntities.RemoveRange(configSets);

                // Remove any collection links for this set
                var collectionSets = await _db.CollectionSets.Where(cs => cs.SetsEntityId == setEntity.Id).ToListAsync();
                _db.CollectionSets.RemoveRange(collectionSets);

                var setItemMetas = await _db.MetaEntities.Where(m => m.SetsEntityId == setEntity.Id).ToListAsync();

                if (result.DeleteFiles)
                {
                    // Delete files inside this set
                    foreach (var meta in setItemMetas)
                    {
                        try
                        {
                            var existingTomb = await _db.Tombstones.FirstOrDefaultAsync(t => t.FileName == meta.FileName);
                            if (existingTomb != null)
                            {
                                existingTomb.PackageType = meta.PackageType;
                                existingTomb.UserTags = meta.UserTags;
                                existingTomb.IsUserTagged = meta.IsUserTagged;
                                existingTomb.Description = meta.Description;
                                existingTomb.SetsEntityId = meta.SetsEntityId;
                                existingTomb.DeletedAt = DateTime.UtcNow;
                            }
                            else
                            {
                                _db.Tombstones.Add(new TombstoneEntity
                                {
                                    FileName = meta.FileName,
                                    PackageType = meta.PackageType,
                                    UserTags = meta.UserTags,
                                    IsUserTagged = meta.IsUserTagged,
                                    Description = meta.Description,
                                    SetsEntityId = meta.SetsEntityId,
                                    DeletedAt = DateTime.UtcNow
                                });
                            }
                        }
                        catch { }

                        if (File.Exists(meta.CompleteFileName))
                        {
                            if (result.Permanent)
                            {
                                try { File.Delete(meta.CompleteFileName); } catch { }
                            }
                            else
                            {
                                RecycleBinHelper.SendToRecycleBin(meta.CompleteFileName);
                            }
                        }

                        _db.MetaEntities.Remove(meta);

                        var inMemoryItem = Items.FirstOrDefault(i => i.Entity.Id == meta.Id);
                        if (inMemoryItem != null)
                        {
                            Items.Remove(inMemoryItem);
                            FilteredItems.Remove(inMemoryItem);
                        }
                    }
                }
                else
                {
                    // Move items to Default set
                    var defaultSet = await _db.SetsEntities.FirstOrDefaultAsync(s => s.Name == "Default" || s.IsDefault)
                                     ?? await _db.SetsEntities.FirstOrDefaultAsync();

                    foreach (var meta in setItemMetas)
                    {
                        meta.SetsEntityId = defaultSet?.Id;
                        var inMemoryItem = Items.FirstOrDefault(i => i.Entity.Id == meta.Id);
                        if (inMemoryItem != null)
                        {
                            inMemoryItem.Entity.SetsEntityId = defaultSet?.Id;
                        }
                    }

                    if (defaultSet != null)
                    {
                        defaultSet.Dirty = true;
                    }
                }

                _db.SetsEntities.Remove(setEntity);
                await _db.SaveChangesAsync();

                RecalculateSetCounts();
                await ReloadSetsTreeAsync();
                ApplyFilter();
                NotifyDirtyStateChanged();
            }
        }
        catch (Exception ex)
        {
            var message = DialogHelper.BuildDetailedExceptionMessage("An error occurred while deleting the set:", ex);
            await DialogHelper.ShowErrorAsync("Delete Error", message);
        }
    }

    private HashSet<long> GetAllSubSetIds(SetNodeViewModel node)
    {
        var ids = new HashSet<long>();
        if (node.Id.HasValue) ids.Add(node.Id.Value);
        foreach (var child in node.Children)
        {
            ids.UnionWith(GetAllSubSetIds(child));
        }
        return ids;
    }
}
