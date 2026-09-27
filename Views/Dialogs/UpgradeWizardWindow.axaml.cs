using Avalonia.Controls;
using Avalonia.Input;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class UpgradeWizardWindow : Window
{
    public UpgradeWizardWindow()
    {
        InitializeComponent();
    }

    public UpgradeWizardWindow(UpgradeWizardViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += success => Close(success);
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
