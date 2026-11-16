using Avalonia.Controls;

namespace PlumbobForge.Desktop.Views.Health;

public partial class HealthHeaderBarView : UserControl
{
    public HealthHeaderBarView()
    {
        InitializeComponent();
    }

    // Hide after the command runs, or it never runs.
    private void OnOptimizerSendClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => OptimizerSendButton.Flyout?.Hide());
}
