using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

// A set's load priority (Customize). Dynamic caches and vault shards are already one folder per set, so only
// Resource.cfg changes; the static cache merges everything, so it has to be rebuilt into its priority folders.
public partial class ContentManagerViewModel
{
    // Hook for tests: which way the last priority change was applied.
    public static string? LastPriorityApplyForTests { get; private set; }

    private async Task ApplySetPriorityChangeAsync()
    {
        var opts = _optionsMonitor?.CurrentValue;
        bool vault = opts?.EnableSingleSourceVault ?? false;
        bool isStatic = !vault && string.Equals(opts?.CacheMethod, "Static", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isStatic)
            {
                // Every set's items may sit in a different bundle now; the usual rebuild prompt takes it from here.
                foreach (var set in await _db.SetsEntities.ToListAsync()) set.Dirty = true;
                await _db.SaveChangesAsync();
                LastPriorityApplyForTests = "static";
                if (opts?.AutoRebuildStaticCache == true)
                {
                    await FreshScope.RunAsync<PKGManager>(pkg => pkg.SyncToSims3Async(forceRebuildStatic: true));
                }
            }
            else if (vault)
            {
                await FreshScope.RunAsync<CacheBuilderService>(cache => cache.RefreshVaultResourceCfgAsync());
                LastPriorityApplyForTests = "vault";
            }
            else
            {
                // Writes Resource.cfg and moves single included items into the matching ConfigItems folder.
                await FreshScope.RunAsync<PKGManager>(pkg => pkg.SyncToSims3Async());
                LastPriorityApplyForTests = "dynamic";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not apply a set's priority", ex, "ContentManagerViewModel");
            await DialogHelper.ShowErrorAsync(L("library.set_priority_title"), string.Format(L("library.set_priority_failed"), ex.Message));
        }
    }
}
