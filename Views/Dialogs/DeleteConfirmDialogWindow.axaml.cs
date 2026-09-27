using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public record DeleteDialogResult(bool Confirmed, bool Permanent);

public partial class DeleteConfirmDialogWindow : Window
{
    public DeleteConfirmDialogWindow()
    {
        InitializeComponent();
    }

    public DeleteConfirmDialogWindow(string message) : this()
    {
        MessageTextBlock.Text = message;
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
        Close(new DeleteDialogResult(true, PermanentCheckBox.IsChecked == true));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new DeleteDialogResult(false, false));
    }
}
