using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class PendingCacheWarningDialogWindow : Window
{
    public PendingCacheWarningDialogWindow()
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

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnCloseAnywayClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
