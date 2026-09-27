using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Dialogs;

public static class DialogHelper
{
    private static Window? GetMainWindow()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    public static async Task<string?> ShowInputAsync(string title, string prompt, string initialValue, string? extension = null, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new InputDialogWindow(title, prompt, initialValue, extension);
        return parent != null ? await dialog.ShowDialog<string?>(parent) : null;
    }

    public static async Task<RetagDialogResult?> ShowRetagAsync(string currentPackageType, string? currentUserTags, string? currentCasCategories, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new RetagDialogWindow(currentPackageType, currentUserTags, currentCasCategories);
        return parent != null ? await dialog.ShowDialog<RetagDialogResult?>(parent) : null;
    }

    public static async Task<DeleteDialogResult> ShowDeleteConfirmAsync(string message, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new DeleteConfirmDialogWindow(message);
        return parent != null ? await dialog.ShowDialog<DeleteDialogResult>(parent) : new DeleteDialogResult(false, false);
    }

    public static async Task<DeleteSetDialogResult> ShowDeleteSetConfirmAsync(string setName, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new DeleteSetDialogWindow(setName);
        return parent != null ? await dialog.ShowDialog<DeleteSetDialogResult>(parent) : new DeleteSetDialogResult(false, false, false);
    }

    public static async Task<string> ShowDuplicateAlertAsync(string title, string message, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new AlertDialogWindow(title, message);
        return parent != null ? await dialog.ShowDialog<string>(parent) : "cancel";
    }

    public static async Task<string?> ShowDuplicateSetNameDialogAsync(string message, string suggestedName, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new DuplicateSetNameDialogWindow(message, suggestedName);
        return parent != null ? await dialog.ShowDialog<string?>(parent) : null;
    }

    public static async Task<MoveSetCollisionResult> ShowMoveSetCollisionDialogAsync(string message, string suggestedName, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new MoveSetCollisionDialogWindow(message, suggestedName);
        return parent != null ? await dialog.ShowDialog<MoveSetCollisionResult>(parent) : new MoveSetCollisionResult(MoveSetCollisionAction.Cancel, null);
    }

    public static async Task<CustomizeSetResult?> ShowCustomizeSetAsync(string currentName, string? currentIcon, string? currentColor, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new CustomizeSetDialogWindow(currentName, currentIcon, currentColor);
        return parent != null ? await dialog.ShowDialog<CustomizeSetResult?>(parent) : null;
    }

    public static async Task<CustomizeConfigResult?> ShowCustomizeConfigAsync(string currentName, string? currentDesc, string? currentIcon, string? currentColor, bool isDefault, bool isActive, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new CustomizeConfigDialogWindow(currentName, currentDesc, currentIcon, currentColor, isDefault, isActive);
        return parent != null ? await dialog.ShowDialog<CustomizeConfigResult?>(parent) : null;
    }

    public static async Task<bool> ShowPendingCacheWarningAsync(Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new PendingCacheWarningDialogWindow();
        return parent != null && await dialog.ShowDialog<bool>(parent);
    }

    public static async Task<RecheckPackageTypesDialogResult?> ShowRecheckPackageTypesAsync(Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new RecheckPackageTypesDialogWindow();
        return parent != null ? await dialog.ShowDialog<RecheckPackageTypesDialogResult?>(parent) : null;
    }

    public static async Task<MigrationDialogResult?> ShowMigrationAsync(Avalonia.Platform.Storage.IStorageProvider? storageProvider = null, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var sp = storageProvider ?? parent?.StorageProvider;
        var dialog = new MigrationDialogWindow(sp);
        return parent != null ? await dialog.ShowDialog<MigrationDialogResult?>(parent) : null;
    }

    public static async Task ShowUpgradeWizardAsync(UpgradeWizardViewModel viewModel, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new UpgradeWizardWindow(viewModel);
        if (parent != null)
        {
            await dialog.ShowDialog(parent);
        }
    }

    public static async Task ShowNewUserWalkthroughAsync(NewUserWalkthroughViewModel viewModel, Window? owner = null)
    {
        var parent = owner ?? GetMainWindow();
        var dialog = new NewUserWalkthroughWindow(viewModel);
        if (parent != null)
        {
            await dialog.ShowDialog(parent);
        }
    }
}
