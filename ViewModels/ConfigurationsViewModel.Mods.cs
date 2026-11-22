using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

// Mods folders in a configuration's columns. A folder is on unless the configuration turns it off; turning one off
// renames the files in it to ".disabled" while that configuration is active. Sub-folders are separate, like sub-sets.
public partial class ConfigurationsViewModel
{
    private List<ModsFolderEntity> _modsFolders = new();
    private Dictionary<long, ModsFolderEntity> _modsFoldersById = new();
    private Dictionary<long, HashSet<long>> _offModsFoldersByConfig = new();

    private static string LM(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    private async Task LoadModsFoldersAsync(AppDbContext db)
    {
        _modsFolders = PlumbobForge.Backend.Configuration.ModsFolderSupport.IsEnabled ? await db.ModsFolders.AsNoTracking().ToListAsync() : new();
        _modsFoldersById = _modsFolders.ToDictionary(f => f.Id);
        _offModsFoldersByConfig = (await db.ConfigModsFoldersOff.AsNoTracking()
                .Select(o => new { o.ConfigEntityId, o.ModsFolderId })
                .ToListAsync())
            .GroupBy(o => o.ConfigEntityId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.ModsFolderId).ToHashSet());
    }

    private HashSet<long> GetOffModsFolders(long configId)
    {
        if (!_offModsFoldersByConfig.TryGetValue(configId, out var set))
        {
            set = new HashSet<long>();
            _offModsFoldersByConfig[configId] = set;
        }
        return set;
    }

    private (List<ConfigModsFolderViewModel> On, List<ConfigModsFolderViewModel> Off) BuildModsFolderEntries(long configId)
    {
        var off = GetOffModsFolders(configId);
        var on = new List<ConfigModsFolderViewModel>();
        var offList = new List<ConfigModsFolderViewModel>();
        foreach (var folder in _modsFolders)
        {
            var vm = new ConfigModsFolderViewModel(folder, GetModsBreadcrumb(folder), GetModsDescendantIds(folder.Id).Count, !off.Contains(folder.Id));
            (vm.IsEnabledInConfig ? on : offList).Add(vm);
        }
        return (SortModsFolders(on), SortModsFolders(offList));
    }

    private List<ConfigModsFolderViewModel> SortModsFolders(List<ConfigModsFolderViewModel> list) => SetSortMode switch
    {
        "NameAsc" => list.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        "NameDesc" => list.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        "SubsetsDesc" => list.OrderByDescending(f => f.DescendantCount).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        _ => list.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList()
    };

    // "Mods Folder  ›  Packages" for Mods/Packages/Hair.
    private string GetModsBreadcrumb(ModsFolderEntity folder)
    {
        var names = new List<string>();
        var visited = new HashSet<long>();
        long? current = folder.ParentModsFolderId;
        while (current.HasValue && visited.Add(current.Value) && _modsFoldersById.TryGetValue(current.Value, out var parent))
        {
            names.Insert(0, parent.Name);
            current = parent.ParentModsFolderId;
        }
        names.Insert(0, LM("library.mods"));
        return string.Join("  ›  ", names);
    }

    // Where a Mods item sits, for its rule card.
    private string GetModsLocation(long? folderId)
    {
        if (!folderId.HasValue || !_modsFoldersById.TryGetValue(folderId.Value, out var folder)) return LM("library.mods");
        return $"{GetModsBreadcrumb(folder)}  ›  {folder.Name}";
    }

    private List<long> GetModsDescendantIds(long folderId)
    {
        var result = new List<long>();
        var queue = new Queue<long>();
        var visited = new HashSet<long> { folderId };
        queue.Enqueue(folderId);
        while (queue.Count > 0)
        {
            long parentId = queue.Dequeue();
            foreach (var child in _modsFolders.Where(f => f.ParentModsFolderId == parentId))
            {
                if (!visited.Add(child.Id)) continue;
                result.Add(child.Id);
                queue.Enqueue(child.Id);
            }
        }
        return result;
    }

    public async Task ToggleModsFolderAsync(ConfigModsFolderViewModel folder)
    {
        if (SelectedConfiguration == null) return;
        var selected = (folder.IsEnabledInConfig ? EnabledSets : DisabledSets)
            .OfType<ConfigModsFolderViewModel>()
            .Where(f => f.IsSelected)
            .Select(f => f.Id)
            .ToList();
        var ids = selected.Contains(folder.Id) && selected.Count > 1 ? selected : new List<long> { folder.Id };
        await RequestMoveModsFoldersAsync(ids, !folder.IsEnabledInConfig);
    }

    // Asks about sub-folders that are on the other side, like sets ask about sub-sets.
    public async Task RequestMoveModsFoldersAsync(List<long> folderIds, bool toEnabled)
    {
        if (SelectedConfiguration == null || folderIds.Count == 0) return;

        var off = GetOffModsFolders(SelectedConfiguration.Id);
        var descendants = folderIds
            .SelectMany(GetModsDescendantIds)
            .Where(d => !folderIds.Contains(d) && off.Contains(d) == toEnabled)
            .ToHashSet();

        if (descendants.Count > 0)
        {
            _pendingTargetSetIds = folderIds;
            _pendingDescendantSetIds = descendants.ToList();
            _pendingTargetToEnabled = toEnabled;
            _pendingIsModsFolders = true;

            string first = _modsFoldersById.TryGetValue(folderIds[0], out var f) ? f.Name : LM("library.mods");
            string names = folderIds.Count == 1 ? $"\"{first}\"" : LM("configurations.mods_cascade_several", folderIds.Count);
            CascadeModalTitle = LM("configurations.mods_cascade_title");
            CascadeModalMessage = LM(toEnabled ? "configurations.mods_cascade_message_on" : "configurations.mods_cascade_message_off", names, descendants.Count);
            CascadeAllText = LM("configurations.mods_cascade_all");
            CascadeOnlyText = LM("configurations.mods_cascade_only");
            CascadeSubtitle = LM("configurations.mods_cascade_subtitle");
            IsCascadeModalVisible = true;
            return;
        }

        await ApplyModsFoldersAsync(folderIds, toEnabled);
    }

    // Returns true when it showed a problem, so the caller doesn't cover it with its own message.
    private Task<bool> ApplyModsFoldersAsync(List<long> folderIds, bool on) =>
        ApplyModsFolderChangesAsync(on ? folderIds : new List<long>(), on ? new List<long>() : folderIds);

    // From the library sidebar: turns sets and Mods folders on or off in the active configuration and applies it.
    // Returns the Mods files that could not be renamed (the game may have them open).
    public async Task<IReadOnlyList<string>> SetInActiveConfigurationAsync(List<long> setIds, List<long> modsFolderIds, bool on)
    {
        // Fresh state: the page may have loaded before sets or folders changed elsewhere.
        await LoadDataAsync();
        var active = Configurations.FirstOrDefault(c => c.Active);
        if (active == null) return Array.Empty<string>();
        if (setIds.Count > 0) await ApplyMoveSetsAsync(setIds, on, active);
        if (modsFolderIds.Count > 0)
        {
            await ApplyModsFolderChangesAsync(on ? modsFolderIds : new List<long>(), on ? new List<long>() : modsFolderIds, active);
            return ModsFolderService.LastApplyFailures;
        }
        return Array.Empty<string>();
    }

    private Task<bool> SwapModsFoldersAsync()
    {
        if (SelectedConfiguration == null) return Task.FromResult(false);
        var off = GetOffModsFolders(SelectedConfiguration.Id);
        var all = _modsFolders.Select(f => f.Id).ToList();
        return ApplyModsFolderChangesAsync(all.Where(off.Contains).ToList(), all.Where(id => !off.Contains(id)).ToList());
    }

    private async Task<bool> ApplyModsFolderChangesAsync(List<long> turnOn, List<long> turnOff, ConfigItemViewModel? target = null)
    {
        var config = target ?? SelectedConfiguration;
        if (config == null || (turnOn.Count == 0 && turnOff.Count == 0)) return false;
        var services = App.Services;
        if (services == null) return false;

        try
        {
            bool changed = await Task.Run(async () =>
            {
                using var scope = services.CreateScope();
                var mods = scope.ServiceProvider.GetRequiredService<ModsFolderService>();
                bool a = turnOn.Count > 0 && await mods.SetFoldersOnAsync(config.Id, turnOn, on: true);
                bool b = turnOff.Count > 0 && await mods.SetFoldersOnAsync(config.Id, turnOff, on: false);
                return a || b;
            });

            var off = GetOffModsFolders(config.Id);
            foreach (long id in turnOn) off.Remove(id);
            foreach (long id in turnOff) off.Add(id);

            string? failure = changed && config.Active ? await ApplyModsConfigurationAsync() : null;
            RefreshSetsColumns();
            if (failure == null) return false;
            ShowStatusToast(failure, isError: true);
            return true;
        }
        catch (Exception ex)
        {
            RefreshSetsColumns();
            ShowStatusToast(LM("configurations.mods_update_failed", ex.Message), isError: true);
            return true;
        }
    }

    // Renames Mods files to match the active configuration. Returns a message when some files couldn't be renamed.
    internal static async Task<string?> ApplyModsConfigurationAsync()
    {
        var services = App.Services;
        if (services == null) return null;
        try
        {
            var result = await Task.Run(async () =>
            {
                using var scope = services.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<ModsFolderService>().ApplyConfigurationAsync();
            });
            return ModsFailureMessage(result.Failed);
        }
        catch (Exception ex)
        {
            return LM("configurations.mods_update_failed", ex.Message);
        }
    }

    // After a game sync, which applies the configuration to Mods files on its own.
    internal static string? ModsApplyFailureMessage() => ModsFailureMessage(ModsFolderService.LastApplyFailures);

    private static string? ModsFailureMessage(IReadOnlyList<string> failed) =>
        failed.Count == 0 ? null : LM("configurations.mods_apply_failed", failed.Count);
}
