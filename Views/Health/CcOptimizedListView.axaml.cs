using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.ViewModels;
using PlumbobForge.Desktop.Views.Controls;

namespace PlumbobForge.Desktop.Views.Health;

public partial class CcOptimizedListView : UserControl
{
    private static string L(string key) => LocalizationManager.Instance.GetString(key);

    public CcOptimizedListView()
    {
        InitializeComponent();
    }

    // Hide after the command runs, or it never runs.
    private void OnSortClick(object? sender, RoutedEventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SortButton.Flyout?.Hide());

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CcOptimizedEntry entry, ContextMenu: { } menu } button) return;
        BuildMenu(menu, entry);
        menu.Open(button);
    }

    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: CcOptimizedEntry entry, ContextMenu: { } menu })
        {
            e.Handled = true;
            return;
        }
        BuildMenu(menu, entry);
    }

    public void BuildMenu(ItemsControl menu, CcOptimizedEntry entry)
    {
        if (DataContext is not CcOptimizerViewModel vm) return;
        string? reveal = CcOptimizerViewModel.RevealPath(entry);
        MenuBuilder.Fill(menu,
            new Control?[]
            {
                MenuBuilder.Item(L("look_inside.title"), () => _ = LookInsideAsync(vm, entry), "RegularSearch"),
                MenuBuilder.Item(L("tools.conflicts.view_in_library"), () => entry.Row.ShowInLibraryCommand.Execute(null), "RegularFolderOpen"),
                reveal != null ? MenuBuilder.Item(L("library.menu_file_explorer"), () => ContentManagerViewModel.RevealInExplorer(reveal, true), "RegularWindow") : null,
            },
            new Control?[]
            {
                MenuBuilder.Item(L("optimizer.history_retune"), () => vm.RetuneEntryCommand.Execute(entry), enabled: vm.CanEditList),
                MenuBuilder.Item(L("optimizer.history_restore"), () => vm.AskRestoreEntryCommand.Execute(entry), enabled: entry.ShowActions),
            });
    }

    private static async Task LookInsideAsync(CcOptimizerViewModel vm, CcOptimizedEntry entry)
    {
        try
        {
            await entry.Row.LookInsideCommand.ExecuteAsync(null);
            await vm.RefreshEntryAsync(entry.ItemId);
        }
        catch (Exception ex)
        {
            PlumbobForge.Backend.Services.AppLogger.LogWarning("Look Inside from the optimized list failed", ex, "CcOptimizedListView");
        }
    }
}
