using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public sealed record PlayOptions(bool Rebuild);

// Asked before Play starts the game while sets have changes that aren't built yet.
public partial class PlayOptionsWindow : Window
{
    public PlayOptionsWindow()
    {
        InitializeComponent();
    }

    public PlayOptionsWindow(string configurationName) : this()
    {
        ConfigurationName.Text = configurationName;
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
        Close(null);
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        Close(new PlayOptions(RebuildCheck.IsChecked == true));
    }
}
