using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

// Asked once after updating: turn on Mods folder support? Closes with true (turn on) or false (not now).
public partial class ModsSupportPromptWindow : Window
{
    public ModsSupportPromptWindow()
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

    private void OnTurnOnClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnNotNowClick(object? sender, RoutedEventArgs e) => Close(false);
}
