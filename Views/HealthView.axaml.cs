using Avalonia.Controls;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views;

public partial class HealthView : UserControl
{
    public HealthView()
    {
        InitializeComponent();
        ToolsRoot.SizeChanged += (_, e) => (DataContext as HealthViewModel)?.UpdateLayoutWidth(e.NewSize.Width);
        DataContextChanged += (_, _) => (DataContext as HealthViewModel)?.UpdateLayoutWidth(ToolsRoot.Bounds.Width);
    }
}
