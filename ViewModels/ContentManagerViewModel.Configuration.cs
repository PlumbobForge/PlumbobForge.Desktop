using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Dialogs;

namespace PlumbobForge.Desktop.ViewModels;

// The sidebar shows what the active configuration loads: sets and Mods folders it leaves out are crossed out,
// and their context menu turns them on or off in it.
public partial class ContentManagerViewModel
{
    private int _configurationStateVersion;

    public async Task RefreshConfigurationStateAsync()
    {
        var services = App.Services;
        if (services == null) return;
        int version = ++_configurationStateVersion;

        string? name = null;
        var setIds = new HashSet<long>();
        var offFolders = new HashSet<long>();
        try
        {
            await Task.Run(async () =>
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var config = await db.ConfigEntities.AsNoTracking()
                    .Where(c => c.Active)
                    .Select(c => new { c.Id, c.Name, Sets = c.ConfigSetsEntities.Select(cs => cs.SetsEntityId).ToList() })
                    .FirstOrDefaultAsync();
                if (config == null) return;
                name = config.Name;
                setIds = config.Sets.ToHashSet();
                offFolders = (await db.ConfigModsFoldersOff.AsNoTracking()
                    .Where(o => o.ConfigEntityId == config.Id)
                    .Select(o => o.ModsFolderId)
                    .ToListAsync()).ToHashSet();
            });
        }
        catch
        {
            return;
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // A newer refresh started meanwhile; it will apply fresher state.
            if (version != _configurationStateVersion) return;
            foreach (var node in AllSetNodes(RootSets))
            {
                if (!node.CanToggleInConfiguration || name == null)
                {
                    node.IsOffInConfiguration = false;
                    node.ConfigurationToggleHeader = string.Empty;
                    node.ConfigurationOffToolTip = null;
                    continue;
                }
                bool off = node.IsModsFolder ? offFolders.Contains(node.ModsFolder!.Id) : !setIds.Contains(node.Id!.Value);
                node.IsOffInConfiguration = off;
                node.ConfigurationToggleHeader = LocalizationManager.Instance.GetString(off ? "library.config_turn_on" : "library.config_turn_off", name);
                node.ConfigurationOffToolTip = off ? LocalizationManager.Instance.GetString("library.config_off_tooltip", name) : null;
            }
        });
    }

    private static IEnumerable<SetNodeViewModel> AllSetNodes(IEnumerable<SetNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in AllSetNodes(node.Children)) yield return child;
        }
    }

    // Turns the node on or off in the active configuration. When it is one of several selected nodes, all of them
    // follow it (the ones already in that state are left alone). Sub-sets and sub-folders keep their own state.
    public async Task ToggleInActiveConfigurationAsync(SetNodeViewModel node)
    {
        if (!node.ShowConfigurationToggle) return;
        bool turnOn = node.IsOffInConfiguration;
        var selected = GetSelectedSetNodes();
        var targets = (selected.Count > 1 && selected.Contains(node) ? selected : new List<SetNodeViewModel> { node })
            .Where(n => n.CanToggleInConfiguration && n.IsOffInConfiguration == turnOn)
            .ToList();
        var setIds = targets.Where(n => !n.IsModsFolder).Select(n => n.Id!.Value).ToList();
        var folderIds = targets.Where(n => n.IsModsFolder).Select(n => n.ModsFolder!.Id).ToList();

        var configs = App.Services?.GetService<ConfigurationsViewModel>();
        if (configs == null) return;
        var failed = await configs.SetInActiveConfigurationAsync(setIds, folderIds, turnOn);
        await RefreshConfigurationStateAsync();
        if (failed.Count > 0)
        {
            await DialogHelper.ShowErrorAsync(LocalizationManager.Instance.GetString("library.mods"),
                LocalizationManager.Instance.GetString("library.config_mods_failed", failed.Count));
        }
    }
}
