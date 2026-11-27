using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

// Open In > Set in Sidebar: the item's set (or Mods folder) is selected, with the way down to it opened, so an item
// seen in All Items can be found without going through every set. An item that is in no set the sidebar shows (no
// set, a deleted one) is put in Default, which says so.
public partial class ContentManagerViewModel
{
    public async Task ShowInSidebarAsync(ItemViewModel item)
    {
        long id = item.Entity.Id;
        if (item.Entity.IsModsItem)
        {
            TrySelectModsItem(id);
            return;
        }

        if (item.Entity.SetsEntityId is long setId && FindNodeById(RootSets, setId) != null)
        {
            SelectSetById(setId, id);
            return;
        }

        var defaultSet = FlatSets.FirstOrDefault(s => s.IsDefault) ?? FlatSets.FirstOrDefault(s => s.Name == "Default");
        if (defaultSet == null || FindNodeById(RootSets, defaultSet.Id) == null)
        {
            AppLogger.LogWarning($"{item.FileName} is in set {item.Entity.SetsEntityId?.ToString() ?? "none"}, which the sidebar doesn't show, and there is no Default set", null, "ContentManagerViewModel");
            Toast?.Show(LocalizationManager.Instance.GetString("library.show_in_sidebar_none", item.FileName), isError: true);
            return;
        }

        AppLogger.LogWarning($"{item.FileName} was in set {item.Entity.SetsEntityId?.ToString() ?? "none"}, which the sidebar doesn't show; moved to Default", null, "ContentManagerViewModel");
        var meta = await _db.MetaEntities.FirstOrDefaultAsync(m => m.Id == id);
        if (meta != null) meta.SetsEntityId = defaultSet.Id;
        var tracked = await _db.SetsEntities.FirstOrDefaultAsync(s => s.Id == defaultSet.Id);
        if (tracked != null) tracked.Dirty = true;
        await _db.SaveChangesAsync();
        item.Entity.SetsEntityId = defaultSet.Id;
        RecalculateSetCounts();
        NotifyDirtyStateChanged();
        SelectSetById(defaultSet.Id, id);
        Toast?.Show(LocalizationManager.Instance.GetString("library.show_in_sidebar_moved", item.FileName, defaultSet.Name));
    }
}
