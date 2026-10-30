using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ContentManagerViewModel
{
    public void UpdateSelectionState()
    {
        SelectedCount = Items.Count(i => i.IsSelected);
        HasSelectedItems = SelectedCount > 0;
    }

    public void LoadMoreItems()
    {
        if (FilteredItems.Count >= _allMatchingItems.Count) return;

        int nextCount = Math.Min(BatchSize, _allMatchingItems.Count - FilteredItems.Count);
        var nextBatch = _allMatchingItems.Skip(FilteredItems.Count).Take(nextCount).ToList();
        foreach (var item in nextBatch)
        {
            FilteredItems.Add(item);
            if (item.ThumbnailBitmap == null && !item.IsLoadingThumbnail)
            {
                _ = item.LoadThumbnailAsync(_thumbnailService);
            }
        }
    }


    private static async Task RecycleVaultItemsAsync(VaultService vaultService, IEnumerable<(long Id, string FileName)> items)
    {
        string dir = Path.Combine(Path.GetTempPath(), "PlumbobForge", "VaultRecycle", Guid.NewGuid().ToString("N"));
        foreach (var (id, fileName) in items)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, Path.ChangeExtension(fileName, ".package"));
                if (await vaultService.ExportModAsync(id, target))
                {
                    foreach (var exported in Directory.GetFiles(dir))
                    {
                        RecycleBinHelper.SendToRecycleBin(exported);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Could not send vault item {fileName} to the Recycle Bin", ex, "ContentManagerViewModel");
            }
        }
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }


    private async Task DeployLibraryItemsInVaultModeAsync()
    {
        if (!IsVaultActive) return;
        try
        {
            await _pkgManager.SyncToSims3Async();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to update lots, Sims and worlds in the game folders", ex, "ContentManagerViewModel");
        }
    }

    [RelayCommand]
    public async Task ToggleEnableItemAsync(ItemViewModel? item)
    {
        if (item == null) return;
        item.Entity.Enabled = !item.Entity.Enabled;

        if (item.Entity.IsVaultManaged)
        {
            var vaultService = App.Services?.GetService<VaultService>();
            if (vaultService != null)
            {
                try
                {
                    await vaultService.ToggleModAsync(item.Entity.Id, item.Entity.Enabled);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"Failed to toggle vault item {item.Entity.Id}", ex, "ContentManagerViewModel");
                    item.Entity.Enabled = !item.Entity.Enabled;
                }
            }
        }
        else
        {
            var meta = await _db.MetaEntities.FindAsync(item.Entity.Id);
            if (meta != null)
            {
                meta.Enabled = item.Entity.Enabled;
                if (meta.SetsEntityId.HasValue)
                {
                    var set = await _db.SetsEntities.Include(s => s.MetaEntities).FirstOrDefaultAsync(s => s.Id == meta.SetsEntityId.Value);
                    if (set != null)
                    {
                        PlumbobForge.Backend.Services.SetDirtyTracker.UpdateDirtyState(set);
                    }
                }
            }
            await _db.SaveChangesAsync();
            await DeployLibraryItemsInVaultModeAsync();
        }

        item.NotifyEntityChanged();
        NotifyDirtyStateChanged();
    }

    [RelayCommand]
    public async Task ToggleFavoriteItemAsync(ItemViewModel? item)
    {
        if (item == null) return;

        var meta = await _db.MetaEntities.FindAsync(item.Entity.Id);
        if (meta != null)
        {
            meta.IsFavorite = item.IsFavorite;
            await _db.SaveChangesAsync();
        }

        RecalculateSetCounts();
        ApplyFilter();
    }

    [RelayCommand]
    public async Task ToggleFavoriteSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        bool targetState = selected.Any(i => !i.IsFavorite);
        await FavoriteSelectedItemsAsync(targetState);
    }

    public async Task FavoriteSelectedItemsAsync(bool isFavorite)
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var ids = selected.Select(i => i.Entity.Id).ToList();
        var metas = await _db.MetaEntities.Where(m => ids.Contains(m.Id)).ToListAsync();
        foreach (var meta in metas)
        {
            meta.IsFavorite = isFavorite;
        }
        await _db.SaveChangesAsync();

        foreach (var item in selected)
        {
            item.Entity.IsFavorite = isFavorite;
            item.NotifyEntityChanged();
        }

        RecalculateSetCounts();
        ApplyFilter();
    }

    public bool IsTextureOptimizationActive => _optionsMonitor?.CurrentValue?.OptimizeTextures ?? false;

    public void NotifyTextureOptimizationChanged()
    {
        ItemViewModel.TextureOptimizationActive = IsTextureOptimizationActive;
        OnPropertyChanged(nameof(IsTextureOptimizationActive));
        foreach (var item in Items) item.NotifyEntityChanged();
    }


    [RelayCommand]
    public async Task ToggleOriginalTexturesSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        bool skip = selected.Any(i => !i.Entity.SkipTextureOptimization);
        var ids = selected.Select(i => i.Entity.Id).ToList();

        try
        {
            var vaultIds = selected.Where(i => i.Entity.IsVaultManaged).Select(i => i.Entity.Id).ToList();
            if (vaultIds.Count > 0)
            {
                await Task.Run(async () =>
                {
                    using var scope = App.Services!.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<VaultService>().SetSkipTextureOptimizationAsync(vaultIds, skip);
                });
            }

            var libraryIds = ids.Except(vaultIds).ToList();
            if (libraryIds.Count > 0)
            {
                var metas = await _db.MetaEntities.Where(m => libraryIds.Contains(m.Id)).ToListAsync();
                foreach (var meta in metas) meta.SkipTextureOptimization = skip;
                await SetDirtyTracker.UpdateDirtyStatesForSetsAsync(_db, metas.Where(m => m.SetsEntityId.HasValue).Select(m => m.SetsEntityId!.Value));
                await _db.SaveChangesAsync();
            }

            foreach (var item in selected)
            {
                item.Entity.SkipTextureOptimization = skip;
                item.NotifyEntityChanged();
            }
            NotifyDirtyStateChanged();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to change texture optimisation for selected items", ex, "ContentManagerViewModel");
        }
    }

    [RelayCommand]
    public async Task RenameItemAsync(ItemViewModel? item)
    {
        if (item == null) return;

        var extension = Path.GetExtension(item.FileName);
        if (string.IsNullOrEmpty(extension) && !string.IsNullOrEmpty(item.Entity.CompleteFileName))
        {
            extension = Path.GetExtension(item.Entity.CompleteFileName);
        }
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(item.FileName);

        var newName = await DialogHelper.ShowInputAsync("Rename Item", $"Enter new filename for '{item.FileName}':", fileNameWithoutExt, extension);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.FileName) return;

        var directory = Path.GetDirectoryName(item.Entity.CompleteFileName);

        var safeName = string.Join("_", newName.Split(Path.GetInvalidFileNameChars()));
        if (!string.IsNullOrEmpty(extension) && !safeName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            safeName += extension;
        }

        if (item.Entity.IsVaultManaged)
        {
            // A vault item's CompleteFileName is its shard, which other mods share: only the name in the database changes.
            if (!safeName.Equals(item.Entity.FileName, StringComparison.OrdinalIgnoreCase) &&
                await _db.MetaEntities.AnyAsync(m => m.Id != item.Entity.Id && m.FileName == safeName))
            {
                await DialogHelper.ShowErrorAsync("Rename Error", "An item with this name already exists.");
                return;
            }
            item.Entity.FileName = safeName;
        }
        else if (directory != null)
        {
            var newCompleteFileName = Path.Combine(directory, safeName);
            if (item.Entity.CompleteFileName != newCompleteFileName)
            {
                if (File.Exists(newCompleteFileName))
                {
                    await DialogHelper.ShowErrorAsync("Rename Error", "A file with this name already exists.");
                    return;
                }
                if (File.Exists(item.Entity.CompleteFileName))
                {
                    try { File.Move(item.Entity.CompleteFileName, newCompleteFileName); } catch { }
                }
                item.Entity.FileName = safeName;
                item.Entity.CompleteFileName = newCompleteFileName;
            }
        }

        var meta = await _db.MetaEntities.FindAsync(item.Entity.Id);
        if (meta != null)
        {
            meta.FileName = item.Entity.FileName;
            meta.CompleteFileName = item.Entity.CompleteFileName;
            if (meta.SetsEntityId.HasValue)
            {
                var set = await _db.SetsEntities.Include(s => s.MetaEntities).FirstOrDefaultAsync(s => s.Id == meta.SetsEntityId.Value);
                if (set != null)
                {
                    PlumbobForge.Backend.Services.SetDirtyTracker.UpdateDirtyState(set);
                }
            }
        }

        item.NotifyEntityChanged();
        await _db.SaveChangesAsync();
        NotifyDirtyStateChanged();
    }

    [RelayCommand]
    public async Task ChangeCategoryAsync(ItemViewModel? item) => await EditItemTagsAsync(item);

    [RelayCommand]
    public async Task EditItemTagsAsync(ItemViewModel? item)
    {
        if (item == null) return;

        var dialogResult = await DialogHelper.ShowRetagAsync(item.PackageType, item.UserTags, item.Entity.CASCategories);
        if (dialogResult == null) return;

        item.Entity.PackageType = dialogResult.PackageType;
        item.Entity.UserTags = dialogResult.UserTags;
        item.Entity.CASCategories = dialogResult.CasCategories;
        item.Entity.IsUserTagged = true;

        var meta = await _db.MetaEntities.FindAsync(item.Entity.Id);
        if (meta != null)
        {
            meta.PackageType = dialogResult.PackageType;
            meta.UserTags = dialogResult.UserTags;
            meta.CASCategories = dialogResult.CasCategories;
            meta.IsUserTagged = true;
        }

        item.NotifyEntityChanged();
        await _db.SaveChangesAsync();
        ApplyFilter();
    }

    [RelayCommand]
    public async Task DeleteItemAsync(ItemViewModel? item)
    {
        if (item == null) return;

        var result = await DialogHelper.ShowDeleteConfirmAsync($"Are you sure you want to delete '{item.FileName}'?");
        if (!result.Confirmed) return;

        try
        {
            var existingTomb = await _db.Tombstones.FirstOrDefaultAsync(t => t.FileName == item.FileName);
            if (existingTomb != null)
            {
                existingTomb.PackageType = item.Entity.PackageType;
                existingTomb.UserTags = item.Entity.UserTags;
                existingTomb.IsUserTagged = item.Entity.IsUserTagged;
                existingTomb.Description = item.Entity.Description;
                existingTomb.SetsEntityId = item.Entity.SetsEntityId;
                existingTomb.IsFavorite = item.Entity.IsFavorite;
                existingTomb.DeletedAt = DateTime.UtcNow;
            }
            else
            {
                _db.Tombstones.Add(new TombstoneEntity
                {
                    FileName = item.FileName,
                    PackageType = item.Entity.PackageType,
                    UserTags = item.Entity.UserTags,
                    IsUserTagged = item.Entity.IsUserTagged,
                    Description = item.Entity.Description,
                    SetsEntityId = item.Entity.SetsEntityId,
                    IsFavorite = item.Entity.IsFavorite,
                    DeletedAt = DateTime.UtcNow
                });
            }
        }
        catch { }

        if (item.Entity.IsVaultManaged)
        {
            var vaultService = App.Services?.GetService<VaultService>(); if (vaultService == null) return;
            if (!result.Permanent)
            {
                await RecycleVaultItemsAsync(vaultService, new[] { (item.Entity.Id, item.Entity.FileName) });
            }
            try
            {
                await vaultService.DeleteModsBatchAsync(new[] { item.Entity.Id },
                    result.Permanent ? null : path => RecycleBinHelper.SendToRecycleBin(path));
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to delete vault item {item.Entity.Id}", ex, "ContentManagerViewModel");
                await DialogHelper.ShowErrorAsync("Delete Error", $"'{item.FileName}' could not be deleted: {ex.Message}");
                return;
            }
        }
        else
        {
            if (File.Exists(item.Entity.CompleteFileName))
            {
                if (result.Permanent)
                {
                    try { File.Delete(item.Entity.CompleteFileName); } catch { }
                }
                else
                {
                    RecycleBinHelper.SendToRecycleBin(item.Entity.CompleteFileName);
                }
            }

            var meta = await _db.MetaEntities.FindAsync(item.Entity.Id);
            if (meta != null)
            {
                _db.MetaEntities.Remove(meta);
            }
        }

        // The merged cache still holds a deleted library item until its set is rebuilt.
        if (!item.Entity.IsVaultManaged && item.Entity.SetsEntityId.HasValue)
        {
            var set = await _db.SetsEntities.FindAsync(item.Entity.SetsEntityId.Value);
            if (set != null) set.Dirty = true;
        }

        Items.Remove(item);
        FilteredItems.Remove(item);

        await _db.SaveChangesAsync();
        if (!item.Entity.IsVaultManaged) await DeployLibraryItemsInVaultModeAsync();
        RecalculateSetCounts();
        UpdateSelectionState();
        ApplyFilter();
        NotifyDirtyStateChanged();
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in FilteredItems)
        {
            item.IsSelected = true;
        }
        UpdateSelectionState();
    }

    private ItemViewModel? _lastSelectedItem;

    public void SelectItem(ItemViewModel item, bool isCtrlPressed, bool isShiftPressed)
    {
        if (isCtrlPressed)
        {
            item.IsSelected = !item.IsSelected;
            if (item.IsSelected)
            {
                _lastSelectedItem = item;
            }
        }
        else if (isShiftPressed)
        {
            int anchorIndex = -1;
            if (_lastSelectedItem != null)
            {
                anchorIndex = FilteredItems.IndexOf(_lastSelectedItem);
            }

            if (anchorIndex == -1)
            {
                for (int i = 0; i < FilteredItems.Count; i++)
                {
                    if (FilteredItems[i].IsSelected)
                    {
                        anchorIndex = i;
                        break;
                    }
                }
            }

            int currentIndex = FilteredItems.IndexOf(item);

            if (anchorIndex != -1 && currentIndex != -1)
            {
                int start = Math.Min(anchorIndex, currentIndex);
                int end = Math.Max(anchorIndex, currentIndex);
                for (int i = 0; i < FilteredItems.Count; i++)
                {
                    FilteredItems[i].IsSelected = (i >= start && i <= end);
                }
            }
            else
            {
                DeselectAll();
                item.IsSelected = true;
                _lastSelectedItem = item;
            }
        }
        else
        {
            bool wasSelected = item.IsSelected && FilteredItems.Count(i => i.IsSelected) == 1;
            DeselectAll();
            item.IsSelected = !wasSelected;
            if (item.IsSelected)
            {
                _lastSelectedItem = item;
            }
        }

        UpdateSelectionState();
    }

    [RelayCommand]
    public void DeselectAll()
    {
        _lastSelectedItem = null;
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }
        UpdateSelectionState();
    }

    [RelayCommand]
    public async Task ToggleEnableSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        // If all selected are enabled, disable them; otherwise enable them
        bool targetState = !selected.All(i => i.Enabled);

        var vaultItems = selected.Where(i => i.Entity.IsVaultManaged).ToList();
        var defaultItems = selected.Where(i => !i.Entity.IsVaultManaged).ToList();

        if (vaultItems.Count > 0)
        {
            var vaultService = App.Services?.GetService<VaultService>();
            bool toggled = false;
            if (vaultService != null)
            {
                try
                {
                    toggled = await vaultService.ToggleModsBatchAsync(vaultItems.Select(i => i.Entity.Id), targetState);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to toggle vault items", ex, "ContentManagerViewModel");
                }
            }
            if (toggled)
            {
                foreach (var item in vaultItems)
                {
                    item.Entity.Enabled = targetState;
                    item.NotifyEntityChanged();
                }
            }
        }

        if (defaultItems.Count > 0)
        {
            foreach (var item in defaultItems)
            {
                item.Entity.Enabled = targetState;
                var meta = await _db.MetaEntities.FindAsync(item.Entity.Id);
                if (meta != null)
                {
                    meta.Enabled = targetState;
                    if (meta.SetsEntityId.HasValue)
                    {
                        var set = await _db.SetsEntities.FindAsync(meta.SetsEntityId.Value);
                        if (set != null) set.Dirty = true;
                    }
                }
                item.NotifyEntityChanged();
            }

            await _db.SaveChangesAsync();
            await DeployLibraryItemsInVaultModeAsync();
        }

        DeselectAll();
    }

    [RelayCommand]
    public async Task MoveSelectedToSetAsync(SetsEntity? targetSet)
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        // Items always live in a set; "no set" means the Default set (an item without a set is in no cache or shard).
        targetSet ??= await _db.SetsEntities.FirstOrDefaultAsync(s => s.Name == "Default" || s.IsDefault) ?? await _db.SetsEntities.FirstOrDefaultAsync();
        long? targetSetId = targetSet?.Id;
        var setIdsToDirty = new HashSet<long>();
        if (targetSetId.HasValue)
        {
            setIdsToDirty.Add(targetSetId.Value);
        }

        var vaultItems = selected.Where(i => i.Entity.IsVaultManaged).ToList();
        var defaultItems = selected.Where(i => !i.Entity.IsVaultManaged).ToList();

        if (vaultItems.Count > 0)
        {
            var vaultService = App.Services?.GetService<VaultService>();
            if (vaultService != null)
            {
                try
                {
                    if (targetSet == null || !await vaultService.MoveModsBatchAsync(vaultItems.Select(i => i.Entity.Id), targetSet))
                    {
                        AppLogger.LogError("Some vault items could not be moved (missing shard files)", null, "ContentManagerViewModel");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to move vault items", ex, "ContentManagerViewModel");
                }
            }
            foreach (var item in vaultItems)
            {
                if (item.Entity.SetsEntityId.HasValue && item.Entity.SetsEntityId.Value != targetSetId)
                {
                    setIdsToDirty.Add(item.Entity.SetsEntityId.Value);
                }
                var updatedMeta = await _db.MetaEntities.AsNoTracking().FirstOrDefaultAsync(m => m.Id == item.Entity.Id);
                if (updatedMeta != null)
                {
                    item.Entity.SetsEntityId = updatedMeta.SetsEntityId;
                    item.Entity.CompleteFileName = updatedMeta.CompleteFileName;
                }
                item.NotifyEntityChanged();
            }
        }

        if (defaultItems.Count > 0)
        {
            foreach (var item in defaultItems)
            {
                if (item.Entity.SetsEntityId.HasValue && item.Entity.SetsEntityId.Value != targetSetId)
                {
                    setIdsToDirty.Add(item.Entity.SetsEntityId.Value);
                }

                item.Entity.SetsEntityId = targetSetId;

                var meta = await _db.MetaEntities.FindAsync(item.Entity.Id);
                if (meta != null)
                {
                    meta.SetsEntityId = targetSetId;
                }

                item.NotifyEntityChanged();
            }

            await _db.SaveChangesAsync();
        }

        await PlumbobForge.Backend.Services.SetDirtyTracker.UpdateDirtyStatesForSetsAsync(_db, setIdsToDirty);
        await _db.SaveChangesAsync();
        if (defaultItems.Count > 0) await DeployLibraryItemsInVaultModeAsync();
        RecalculateSetCounts();
        DeselectAll();
        ApplyFilter();
        NotifyDirtyStateChanged();
    }

    [RelayCommand]
    public async Task EditTagsSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var firstItem = selected.FirstOrDefault();
        var dialogResult = await DialogHelper.ShowRetagAsync(
            firstItem?.PackageType ?? BatchPackageType,
            firstItem?.UserTags ?? BatchTagInput,
            firstItem?.Entity.CASCategories);

        if (dialogResult == null) return;

        foreach (var item in selected)
        {
            item.Entity.PackageType = dialogResult.PackageType;
            item.Entity.UserTags = dialogResult.UserTags;
            item.Entity.CASCategories = dialogResult.CasCategories;
            item.Entity.IsUserTagged = true;

            var meta = await _db.MetaEntities.FindAsync(item.Entity.Id);
            if (meta != null)
            {
                meta.PackageType = dialogResult.PackageType;
                meta.UserTags = dialogResult.UserTags;
                meta.CASCategories = dialogResult.CasCategories;
                meta.IsUserTagged = true;
            }

            item.NotifyEntityChanged();
        }

        await _db.SaveChangesAsync();
        DeselectAll();
        ApplyFilter();
    }

    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        string message = selected.Count == 1
            ? $"Are you sure you want to delete '{selected[0].FileName}'?"
            : $"Are you sure you want to delete {selected.Count} selected items?";

        var result = await DialogHelper.ShowDeleteConfirmAsync(message);
        if (!result.Confirmed) return;

        var setIdsToDirty = new HashSet<long>();
        foreach (var item in selected)
        {
            if (item.Entity.SetsEntityId.HasValue)
            {
                setIdsToDirty.Add(item.Entity.SetsEntityId.Value);
            }

            try
            {
                var existingTomb = await _db.Tombstones.FirstOrDefaultAsync(t => t.FileName == item.FileName);
                if (existingTomb != null)
                {
                    existingTomb.PackageType = item.Entity.PackageType;
                    existingTomb.UserTags = item.Entity.UserTags;
                    existingTomb.IsUserTagged = item.Entity.IsUserTagged;
                    existingTomb.Description = item.Entity.Description;
                    existingTomb.SetsEntityId = item.Entity.SetsEntityId;
                    existingTomb.IsFavorite = item.Entity.IsFavorite;
                    existingTomb.DeletedAt = DateTime.UtcNow;
                }
                else
                {
                    _db.Tombstones.Add(new TombstoneEntity
                    {
                        FileName = item.FileName,
                        PackageType = item.Entity.PackageType,
                        UserTags = item.Entity.UserTags,
                        IsUserTagged = item.Entity.IsUserTagged,
                        Description = item.Entity.Description,
                        SetsEntityId = item.Entity.SetsEntityId,
                        IsFavorite = item.Entity.IsFavorite,
                        DeletedAt = DateTime.UtcNow
                    });
                }
            }
            catch { }
        }

        var vaultItems = selected.Where(i => i.Entity.IsVaultManaged).ToList();
        var defaultItems = selected.Where(i => !i.Entity.IsVaultManaged).ToList();

        if (vaultItems.Count > 0)
        {
            var vaultService = App.Services?.GetService<VaultService>();
            if (vaultService != null)
            {
                if (!result.Permanent)
                {
                    await RecycleVaultItemsAsync(vaultService, vaultItems.Select(i => (i.Entity.Id, i.Entity.FileName)));
                }
                try
                {
                    await vaultService.DeleteModsBatchAsync(vaultItems.Select(i => i.Entity.Id),
                        result.Permanent ? null : path => RecycleBinHelper.SendToRecycleBin(path));
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to delete vault items", ex, "ContentManagerViewModel");
                    await DialogHelper.ShowErrorAsync("Delete Error", $"The selected vault items could not be deleted: {ex.Message}");
                    return;
                }
            }
            foreach (var item in vaultItems)
            {
                Items.Remove(item);
                FilteredItems.Remove(item);
            }
        }

        if (defaultItems.Count > 0)
        {
            foreach (var item in defaultItems)
            {
                if (File.Exists(item.Entity.CompleteFileName))
                {
                    if (result.Permanent)
                    {
                        try { File.Delete(item.Entity.CompleteFileName); } catch { }
                    }
                    else
                    {
                        RecycleBinHelper.SendToRecycleBin(item.Entity.CompleteFileName);
                    }
                }

                var meta = await _db.MetaEntities.FindAsync(item.Entity.Id);
                if (meta != null)
                {
                    _db.MetaEntities.Remove(meta);
                }

                Items.Remove(item);
                FilteredItems.Remove(item);
            }
        }

        await _db.SaveChangesAsync();
        await PlumbobForge.Backend.Services.SetDirtyTracker.UpdateDirtyStatesForSetsAsync(_db, setIdsToDirty);
        await _db.SaveChangesAsync();
        if (defaultItems.Count > 0) await DeployLibraryItemsInVaultModeAsync();
        RecalculateSetCounts();
        DeselectAll();
        ApplyFilter();
        NotifyDirtyStateChanged();
    }

    [RelayCommand]
    public async Task RefreshSingleItemAsync(ItemViewModel? item)
    {
        if (item == null) return;
        try
        {
            await Task.Run(async () =>
            {
                using var scope = App.Services?.CreateScope();
                var pkgMgr = scope?.ServiceProvider.GetRequiredService<PKGManager>() ?? _pkgManager;
                if (pkgMgr != null)
                {
                    await pkgMgr.RefreshItemAsync(item.Id);
                }
            });

            var updatedMeta = await _db.MetaEntities.AsNoTracking().FirstOrDefaultAsync(m => m.Id == item.Id);
            if (updatedMeta != null)
            {
                item.UpdateFromEntity(updatedMeta);
            }
            await item.ReloadThumbnailAsync(forceDiskExtraction: false);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Error refreshing item {item.Id}", ex, "ContentManagerViewModel");
        }
    }

    [RelayCommand]
    public async Task RefreshSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        IsLoading = true;
        try
        {
            var selectedIds = selected.Select(i => i.Id).ToList();
            await Task.Run(async () =>
            {
                using var scope = App.Services?.CreateScope();
                var pkgMgr = scope?.ServiceProvider.GetRequiredService<PKGManager>() ?? _pkgManager;
                if (pkgMgr != null)
                {
                    foreach (var id in selectedIds)
                    {
                        await pkgMgr.RefreshItemAsync(id);
                    }
                }
            });

            var updatedMetas = await _db.MetaEntities.AsNoTracking().Where(m => selectedIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
            foreach (var item in selected)
            {
                if (updatedMetas.TryGetValue(item.Id, out var meta))
                {
                    item.UpdateFromEntity(meta);
                }
                await item.ReloadThumbnailAsync(forceDiskExtraction: false);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Error refreshing selected items", ex, "ContentManagerViewModel");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ExportSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var topLevel = TopLevel.GetTopLevel(App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (topLevel?.StorageProvider == null) return;

        using var scope = App.Services?.CreateScope();
        var vaultService = scope?.ServiceProvider.GetRequiredService<VaultService>();
        if (vaultService == null) return;

        if (selected.Count == 1)
        {
            var single = selected[0];
            string defaultName = Path.ChangeExtension(single.FileName, ".package");
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationManager.Instance.GetString("library.export_item") ?? "Export Item",
                DefaultExtension = "package",
                SuggestedFileName = defaultName,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Package Files (*.package)") { Patterns = new[] { "*.package" } },
                    new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
                }
            });

            if (file != null)
            {
                string targetPath = file.Path.LocalPath;
                bool ok = await vaultService.ExportModAsync(single.Id, targetPath);
                if (ok)
                {
                    _ = _notificationService.BroadcastAsync("mod_exported", single.FileName);
                }
            }
        }
        else
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LocalizationManager.Instance.GetString("library.export_selected") ?? "Select destination folder for exported packages",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                string targetDir = folders[0].Path.LocalPath;
                int exported = 0;
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in selected)
                {
                    // Two selected mods can share a file name (different sets); never overwrite one with the other.
                    string baseName = Path.GetFileNameWithoutExtension(item.FileName);
                    string fileName = $"{baseName}.package";
                    for (int n = 2; usedNames.Contains(fileName) || File.Exists(Path.Combine(targetDir, fileName)); n++)
                    {
                        fileName = $"{baseName} ({n}).package";
                    }
                    usedNames.Add(fileName);

                    try
                    {
                        if (await vaultService.ExportModAsync(item.Id, Path.Combine(targetDir, fileName)))
                        {
                            exported++;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError($"Failed to export item {item.Id}", ex, "ContentManagerViewModel");
                    }
                }
                if (exported > 0)
                {
                    _ = _notificationService.BroadcastAsync("mods_exported", targetDir);
                }
            }
        }
    }

    [RelayCommand]
    public async Task LookInsideSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count != 1) return;

        var item = selected[0];
        if (await DialogHelper.ShowLookInsideAsync(item.Id, item.FileName))
        {
            await RefreshEditedItemsAsync(new[] { item.Id });
        }
    }


    public async Task RefreshEditedItemsAsync(IEnumerable<long> itemIds)
    {
        foreach (var id in itemIds)
        {
            var item = Items.FirstOrDefault(i => i.Id == id);
            if (item == null) continue;
            var updatedMeta = await _db.MetaEntities.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
            if (updatedMeta != null) item.UpdateFromEntity(updatedMeta);
            await item.ReloadThumbnailAsync(forceDiskExtraction: false);
        }
        ApplyFilter();
        NotifyDirtyStateChanged();
    }

    [RelayCommand]
    public async Task EditCasFlagsSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;
        var ids = selected.Select(i => i.Id).ToList();
        if (await DialogHelper.ShowCasFlagsAsync(ids, IsVaultActive))
        {
            await RefreshEditedItemsAsync(ids);
        }
    }

    [RelayCommand]
    public async Task ReplaceSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count != 1) return;

        var targetItem = selected[0];
        var topLevel = TopLevel.GetTopLevel(App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Instance.GetString("library.replace_item") ?? "Select Replacement File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Sims 3 Content (*.package, *.sims3pack)") { Patterns = new[] { "*.package", "*.sims3pack" } },
                new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count == 0) return;

        string replacementPath = files[0].Path.LocalPath;
        if (!File.Exists(replacementPath)) return;

        using var scope = App.Services?.CreateScope();
        var vaultService = scope?.ServiceProvider.GetRequiredService<VaultService>();
        if (vaultService == null) return;

        IsLoading = true;
        try
        {
            bool success = await vaultService.ReplaceModAsync(targetItem.Id, replacementPath);
            if (success)
            {
                var updatedMeta = await _db.MetaEntities.AsNoTracking().FirstOrDefaultAsync(m => m.Id == targetItem.Id);
                if (updatedMeta != null)
                {
                    targetItem.UpdateFromEntity(updatedMeta);
                }
                await targetItem.ReloadThumbnailAsync(forceDiskExtraction: true);
                ApplyFilter();
                _ = _notificationService.BroadcastAsync("mod_replaced", targetItem.FileName);
            }
            else
            {
                AppLogger.LogError($"Could not replace item {targetItem.Id} with {replacementPath} (not a vault item, or the file could not be read)", null, "ContentManagerViewModel");
                _ = _notificationService.BroadcastAsync("mod_replace_failed", targetItem.FileName);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to replace item {targetItem.Id}", ex, "ContentManagerViewModel");
            _ = _notificationService.BroadcastAsync("mod_replace_failed", targetItem.FileName);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
