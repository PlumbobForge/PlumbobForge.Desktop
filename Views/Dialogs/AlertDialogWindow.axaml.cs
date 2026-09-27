using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class AlertDialogWindow : Window
{
    public AlertDialogWindow()
    {
        InitializeComponent();
    }

    public AlertDialogWindow(string title, string message) : this()
    {
        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
    }

    private void OnHeaderPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnRenameAllClick(object? sender, RoutedEventArgs e)
    {
        Close("rename");
    }

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
    {
        Close("replace");
    }

    private void OnSkipAllClick(object? sender, RoutedEventArgs e)
    {
        Close("skip");
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close("cancel");
    }
}
