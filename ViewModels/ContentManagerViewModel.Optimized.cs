using System;
using System.Collections.Generic;
using System.Linq;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ContentManagerViewModel
{
    private static Dictionary<long, DateTime> OptimizedItemsSafe()
    {
        try
        {
            return MeshBackupStore.OptimizedItems();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("Could not read the CC Optimizer backups", ex, "ContentManagerViewModel");
            return new();
        }
    }

    // After the CC optimizer saved or restored something.
    public void RefreshOptimizedFlags()
    {
        var optimized = OptimizedItemsSafe();
        bool changed = false;
        foreach (var item in Items)
        {
            DateTime? at = optimized.TryGetValue(item.Entity.Id, out var when) ? when : null;
            if (item.OptimizedAt == at) continue;
            item.OptimizedAt = at;
            changed = true;
        }
        if (changed && FilterModeOptimizedOnly) RequestFilterUpdate(0);
        if (changed) RecountSmartSets();
    }
}
