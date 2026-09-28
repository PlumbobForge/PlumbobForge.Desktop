using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class ErrorDialogWindow : Window
{
    public ErrorDialogWindow()
    {
        InitializeComponent();
    }

    public ErrorDialogWindow(string title, string message) : this()
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

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
