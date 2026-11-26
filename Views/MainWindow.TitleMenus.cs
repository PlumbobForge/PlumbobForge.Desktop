using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.ViewModels;
using PlumbobForge.Desktop.Views.Controls;

namespace PlumbobForge.Desktop.Views;

// The title bar's Help, Quick Actions and Play menus.
public partial class MainWindow
{
    private static string Lt(string key) => LocalizationManager.Instance.GetString(key);

    private void OnHelpClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || HelpButton.ContextMenu is not { } menu) return;
        _vm.DismissHelpMoveTip();
        // While the Guide is open, ? closes it.
        if (_vm.IsGuideOpen)
        {
            _vm.ToggleGuideCommand.Execute(null);
            return;
        }
        BuildHelpMenu(menu, _vm);
        menu.Open(HelpButton);
    }

    // "Show Me" on the moved-feedback callout opens the Help menu.
    private void OnHelpMoveTipShowClick(object? sender, RoutedEventArgs e) => OnHelpClick(sender, e);

    private async void OnPlayMenuClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || PlayMenuButton.ContextMenu is not { } menu) return;
        var configs = await _vm.PlayMenuConfigsAsync();
        BuildPlayMenu(menu, _vm, configs);
        menu.Open(PlayMenuButton);
    }

    private void OnQuickActionsClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || QuickActionsButton.ContextMenu is not { } menu) return;
        BuildQuickActionsMenu(menu, _vm);
        menu.Open(QuickActionsButton);
    }

    public static void BuildQuickActionsMenu(ItemsControl menu, MainViewModel vm)
    {
        MenuBuilder.Fill(menu,
            new Control?[]
            {
                MenuBuilder.Item(Lt("nav.clear_caches"), () => vm.ClearStandardCachesCommand.Execute(null), "RegularEraser"),
                MenuBuilder.Item(Lt("nav.refresh"), () => vm.RefreshApplicationCommand.Execute(null), "RegularRotateCw"),
            });
    }

    public static void BuildHelpMenu(ItemsControl menu, MainViewModel vm)
    {
        MenuBuilder.Fill(menu,
            new Control?[]
            {
                MenuBuilder.Item(Lt("nav.guide_menu"), () => vm.ToggleGuideCommand.Execute(null), "RegularBookOpen", "F1"),
                MenuBuilder.Item(Lt("nav.whats_new"), () => vm.ShowWhatsNewCommand.Execute(null), "RegularGift"),
            },
            new Control?[]
            {
                MenuBuilder.Item(Lt("nav.send_feedback"), () => vm.OpenFeedbackCommand.Execute(null), "RegularBug"),
            });
    }

    // Configurations to switch to, then rebuilding (Library mode) or the vault's status, then the Configurations page.
    public static void BuildPlayMenu(ItemsControl menu, MainViewModel vm, IReadOnlyList<ConfigItemViewModel> configs)
    {
        var choices = configs.Select(c => (Control?)MenuBuilder.Check(c.Name, c.Active, () =>
        {
            if (!c.Active) _ = vm.ActivateFromPlayMenuAsync(c);
        }));
        var cache = vm.IsVaultActive
            ? new Control?[] { new MenuItem { Header = vm.VaultStatusText, IsEnabled = false } }
            : new Control?[]
            {
                MenuBuilder.Item(Lt("nav.play_rebuild_only"), () => vm.RebuildCacheCommand.Execute(null), enabled: vm.HasDirtySets && !vm.IsGameRunning),
                MenuBuilder.Item(Lt("common.force_rebuild"), () => vm.ForceRebuildCacheCommand.Execute(null), enabled: !vm.IsGameRunning),
            };
        MenuBuilder.Fill(menu,
            choices.ToArray(),
            cache,
            new Control?[] { MenuBuilder.Item(Lt("nav.play_manage_configurations"), vm.OpenConfigurationsFromPlayMenu) });
    }
}
