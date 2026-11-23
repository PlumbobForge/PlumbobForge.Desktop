using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

// Deleting with Undo: the items (or sets) leave the library at once, and the real delete waits behind the
// undo toast. Undo just shows them again, because nothing was touched yet. Reloads keep them hidden meanwhile.
public partial class ContentManagerViewModel
{
    private readonly HashSet<long> _pendingDeleteItemIds = new();
    private readonly HashSet<long> _pendingDeleteSetIds = new();
    // Sets deleted together with their files: their items are hidden too.
    private readonly HashSet<long> _pendingDeleteSetItems = new();

    public bool IsDeletePending(ItemViewModel item) =>
        _pendingDeleteItemIds.Contains(item.Entity.Id) ||
        (item.Entity.SetsEntityId is long setId && _pendingDeleteSetItems.Contains(setId));

    public bool IsSetDeletePending(long setId) => _pendingDeleteSetIds.Contains(setId);

    // Without a toast to wait behind (it should always be there), the delete happens straight away.
    private void HoldDelete(string message, Func<Task> undo, Func<Task> commit)
    {
        if (Toast is { } toast) toast.Show(message, undo, commit: commit);
        else _ = commit();
    }

    private void HideItems(IEnumerable<ItemViewModel> items)
    {
        foreach (var item in items.ToList())
        {
            item.IsSelected = false;
            Items.Remove(item);
            FilteredItems.Remove(item);
        }
        RecalculateSetCounts();
        UpdateSelectionState();
        ApplyFilter();
    }

    private void ShowItemsAgain(IEnumerable<ItemViewModel> items)
    {
        var present = Items.Select(i => i.Entity.Id).ToHashSet();
        foreach (var item in items)
        {
            if (present.Add(item.Entity.Id)) Items.Add(item);
        }
        RecalculateSetCounts();
        ApplyFilter();
    }

    // A held-back delete runs later, maybe while the user has picked other items; it must not undo that pick.
    private async Task KeepSelectionAsync(Func<Task> action)
    {
        var keep = Items.Where(i => i.IsSelected).Select(i => i.Entity.Id).ToHashSet();
        try
        {
            await action();
        }
        finally
        {
            foreach (var i in Items) i.IsSelected = keep.Contains(i.Entity.Id);
            UpdateSelectionState();
        }
    }

    // Library, vault and Mods items: to the Recycle Bin once the undo toast goes.
    public void DeleteItemsWithUndo(IReadOnlyCollection<ItemViewModel> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        var ids = list.Select(i => i.Entity.Id).ToList();
        foreach (var id in ids) _pendingDeleteItemIds.Add(id);
        string message = string.Format(L("library.toast_deleted"), ItemsPhrase(list));
        HideItems(list);

        HoldDelete(message,
            undo: () =>
            {
                foreach (var id in ids) _pendingDeleteItemIds.Remove(id);
                ShowItemsAgain(list);
                return Task.CompletedTask;
            },
            commit: async () =>
            {
                try
                {
                    // One item takes the single-item path, which also marks its set's merged cache for rebuilding.
                    await KeepSelectionAsync(() => DeleteWithoutCrashingAsync(() => list.Count == 1
                        ? DeleteItemConfirmedAsync(list[0], permanent: false)
                        : DeleteItemsAsync(list, permanent: false)));
                }
                finally
                {
                    foreach (var id in ids) _pendingDeleteItemIds.Remove(id);
                }
            });
    }

    // Sets, after their own dialog (keep or delete the files). A permanent delete never comes here.
    public async Task DeleteSetsWithUndoAsync(List<SetNodeViewModel> targets, DeleteSetDialogResult result)
    {
        var setIds = targets.Select(t => t.Id!.Value).ToList();
        foreach (var id in setIds)
        {
            _pendingDeleteSetIds.Add(id);
            if (result.DeleteFiles) _pendingDeleteSetItems.Add(id);
        }
        var hiddenItems = result.DeleteFiles
            ? Items.Where(i => i.Entity.SetsEntityId is long s && setIds.Contains(s)).ToList()
            : new List<ItemViewModel>();
        string message = targets.Count == 1
            ? string.Format(L("library.toast_set_deleted"), targets[0].Name)
            : string.Format(L("library.toast_sets_deleted"), targets.Count);

        bool selectedGoes = SelectedSetNode?.Id is long selected && setIds.Contains(selected);
        long? keepSelected = selectedGoes ? null : SelectedSetNode?.Id;
        if (selectedGoes) SelectedSetNode = RootSets.FirstOrDefault(n => n.IsAllItems);
        HideItems(hiddenItems);
        await ReloadSetsTreeAsync(keepSelected);

        HoldDelete(message,
            undo: async () =>
            {
                foreach (var id in setIds)
                {
                    _pendingDeleteSetIds.Remove(id);
                    _pendingDeleteSetItems.Remove(id);
                }
                ShowItemsAgain(hiddenItems);
                await ReloadSetsTreeAsync(SelectedSetNode?.Id);
            },
            commit: async () =>
            {
                try
                {
                    await KeepSelectionAsync(async () =>
                    {
                        foreach (var id in setIds) await DeleteSetCoreAsync(id, result);
                    });
                }
                finally
                {
                    foreach (var id in setIds)
                    {
                        _pendingDeleteSetIds.Remove(id);
                        _pendingDeleteSetItems.Remove(id);
                    }
                }
            });
    }
}
