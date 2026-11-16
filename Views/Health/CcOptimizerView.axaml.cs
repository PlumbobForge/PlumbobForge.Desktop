using Avalonia.Controls;

namespace PlumbobForge.Desktop.Views.Health;

public partial class CcOptimizerView : UserControl
{
    public CcOptimizerView()
    {
        InitializeComponent();
    }

    // Hide after the command runs, or it never runs.
    private void OnListMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ListMenuButton.Flyout?.Hide());
}
