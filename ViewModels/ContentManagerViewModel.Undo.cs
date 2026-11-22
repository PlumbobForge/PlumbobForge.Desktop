using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

// "What just happened" toasts for library actions, and how each one is taken back. Undo re-runs the same
// action on just the items it changed, so a mixed selection comes back exactly as it was.
public partial class ContentManagerViewModel
{
    private ActionToastService? Toast => App.Services?.GetService<ActionToastService>();

    // "MenuA.package" for one item, "3 items" for more.
    private string ItemsPhrase(IReadOnlyCollection<ItemViewModel> items) =>
        items.Count == 1 ? items.First().FileName : string.Format(L("library.toast_items"), items.Count);

    private void ShowToast(string message, Func<Task>? undo = null) => Toast?.Show(message, undo);

    // Runs a selection-based action on these items only, then puts the user's selection back.
    private async Task RunOnItemsAsync(IReadOnlyCollection<long> ids, Func<Task> action)
    {
        var keep = Items.Where(i => i.IsSelected).Select(i => i.Entity.Id).ToHashSet();
        var only = ids.ToHashSet();
        foreach (var i in Items) i.IsSelected = only.Contains(i.Entity.Id);
        if (Items.Any(i => i.IsSelected)) await action();
        foreach (var i in Items) i.IsSelected = keep.Contains(i.Entity.Id);
    }
}
