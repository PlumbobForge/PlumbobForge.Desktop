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

    public DeleteSetDialogWindow(string setName) : this()
    {
        MessageTextBlock.Text = $"Are you sure you want to delete set '{setName}'?";
    }

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
