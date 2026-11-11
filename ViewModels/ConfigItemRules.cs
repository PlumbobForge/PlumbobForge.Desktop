using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public static class ConfigItemRules
{
    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    public static async Task<string> SetAsync(long configId, IReadOnlyCollection<long> itemIds, bool? include)
    {
        var services = App.Services ?? throw new InvalidOperationException("Services are not available");
        var result = await Task.Run(async () =>
        {
            using var scope = services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ConfigItemOverrideService>().SetRuleAsync(configId, itemIds, include);
        });
        return await ApplyToGameAsync(result.ActiveConfigChanged, result.LibrarySetsNeedingRebuild);
    }

    public static async Task<string> ApplyToGameAsync(bool activeConfigChanged, int librarySetsNeedingRebuild)
    {
        string message = L("configurations.items_saved");
        if (App.Services == null) return message;

        var opts = App.Services.GetRequiredService<IOptionsMonitor<PlumbobForgeOptions>>().CurrentValue;
        bool vault = opts.EnableSingleSourceVault;
        bool isStatic = !vault && string.Equals(opts.CacheMethod, "Static", StringComparison.OrdinalIgnoreCase);

        if (activeConfigChanged)
        {
            await Task.Run(async () =>
            {
                using var scope = App.Services.CreateScope();
                var pkgManager = scope.ServiceProvider.GetRequiredService<PKGManager>();
                if (isStatic)
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    foreach (var set in await db.SetsEntities.ToListAsync()) set.Dirty = true;
                    await db.SaveChangesAsync();
                    if (opts.AutoRebuildStaticCache) await pkgManager.SyncToSims3Async(forceRebuildStatic: true);
                }
                else
                {
                    await pkgManager.SyncToSims3Async();
                }
            });
            if (isStatic && !opts.AutoRebuildStaticCache) message = L("configurations.items_static_rebuild");
        }

        if (!vault && !isStatic && librarySetsNeedingRebuild > 0)
        {
            message = L("configurations.items_rebuild_needed", librarySetsNeedingRebuild);
        }

        _ = App.Services.GetService<MainViewModel>()?.RefreshDirtyStateAsync();
        return message;
    }
}
