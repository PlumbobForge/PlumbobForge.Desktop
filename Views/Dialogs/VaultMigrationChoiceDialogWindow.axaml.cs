using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class VaultMigrationChoiceDialogWindow : Window
{
    public VaultMigrationChoiceDialogWindow()
    {
        InitializeComponent();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnZipCardPressed(object? sender, PointerPressedEventArgs e)
    {
        ZipOptionRadio.IsChecked = true;
    }

    private void OnKeepCardPressed(object? sender, PointerPressedEventArgs e)
    {
        KeepOptionRadio.IsChecked = true;
    }

    private void OnDeleteCardPressed(object? sender, PointerPressedEventArgs e)
    {
        DeleteOptionRadio.IsChecked = true;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        var choice = VaultMigrationBackupMode.ZipArchive;
        if (KeepOptionRadio.IsChecked == true)
        {
            choice = VaultMigrationBackupMode.KeepFolder;
        }
        else if (DeleteOptionRadio.IsChecked == true)
        {
            choice = VaultMigrationBackupMode.DeleteImmediately;
        }

        Close(choice);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
