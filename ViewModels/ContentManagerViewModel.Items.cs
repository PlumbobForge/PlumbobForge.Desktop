using System;
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

    [RelayCommand]
    public async Task ToggleEnableItemAsync(ItemViewModel? item)
    {
        if (item == null) return;
        item.Entity.Enabled = !item.Entity.Enabled;
        
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

        item.NotifyEntityChanged();
        await _db.SaveChangesAsync();
        NotifyDirtyStateChanged();
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

        if (directory != null)
        {
            var newCompleteFileName = Path.Combine(directory, safeName);
            if (item.Entity.CompleteFileName != newCompleteFileName)
            {
                if (File.Exists(newCompleteFileName))
                {
                    await DialogHelper.ShowDuplicateAlertAsync("Rename Error", "A file with this name already exists.");
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
                    DeletedAt = DateTime.UtcNow
                });
            }
        }
        catch { }

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

        await _db.SaveChangesAsync();
        RecalculateSetCounts();
        UpdateSelectionState();
        ApplyFilter();
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

        foreach (var item in selected)
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
        DeselectAll();
    }

    [RelayCommand]
    public async Task MoveSelectedToSetAsync(SetsEntity? targetSet)
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        long? targetSetId = targetSet?.Id;
        var setIdsToDirty = new HashSet<long>();
        if (targetSetId.HasValue)
        {
            setIdsToDirty.Add(targetSetId.Value);
        }

        foreach (var item in selected)
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
        await PlumbobForge.Backend.Services.SetDirtyTracker.UpdateDirtyStatesForSetsAsync(_db, setIdsToDirty);
        await _db.SaveChangesAsync();
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
                        DeletedAt = DateTime.UtcNow
                    });
                }
            }
            catch { }

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

        await _db.SaveChangesAsync();
        await PlumbobForge.Backend.Services.SetDirtyTracker.UpdateDirtyStatesForSetsAsync(_db, setIdsToDirty);
        await _db.SaveChangesAsync();
        RecalculateSetCounts();
        DeselectAll();
        ApplyFilter();
        NotifyDirtyStateChanged();
    }
}
