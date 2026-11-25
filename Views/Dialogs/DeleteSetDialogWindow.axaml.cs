using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public record DeleteSetDialogResult(bool Confirmed, bool DeleteFiles, bool Permanent);

public partial class DeleteSetDialogWindow : Window
{
    public DeleteSetDialogWindow()
    {
        InitializeComponent();
    }

    public DeleteSetDialogWindow(string setName, string? message = null) : this()
    {
        MessageTextBlock.Text = message ?? $"Are you sure you want to delete set '{setName}'?";
    }

    // A Mods folder: the same choices as a set, with Packages in place of the Default set. canMoveToPackages false
    // (no Packages folder Resource.cfg loads) leaves only deleting the files, so no option points nowhere.
    public DeleteSetDialogWindow(string folderName, int fileCount, bool isRootFolder, bool canMoveToPackages) : this()
    {
        string title = L("dialogs.delete_set.title_folder");
        Title = title;
        TitleTextBlock.Text = title;
        DeleteButton.Content = title;
        MessageTextBlock.Text = string.Format(L("dialogs.delete_set.message_folder"), folderName, fileCount);
        InfoTextBlock.Text = L(isRootFolder ? "dialogs.delete_set.root_info_folder" : "dialogs.delete_set.child_info_folder");
        ItemsTitleTextBlock.Text = L("dialogs.delete_set.items_action_title_folder");
        MoveFilesRadio.Content = L("dialogs.delete_set.move_to_packages");
        DeleteFilesRadio.Content = L("dialogs.delete_set.delete_items_folder");
        MoveFilesRadio.IsVisible = canMoveToPackages;
        MoveFilesRadio.IsChecked = canMoveToPackages;
        DeleteFilesRadio.IsChecked = !canMoveToPackages;
    }

    private static string L(string key) => PlumbobForge.Desktop.Services.Localization.LocalizationManager.Instance.GetString(key);

    private void OnHeaderPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        bool deleteFiles = DeleteFilesRadio.IsChecked == true;
        bool permanent = deleteFiles && PermanentCheckBox.IsChecked == true;
        Close(new DeleteSetDialogResult(true, deleteFiles, permanent));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new DeleteSetDialogResult(false, false, false));
    }
}
