using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class NewUserWalkthroughWindow : Window
{
    public NewUserWalkthroughWindow()
    {
        InitializeComponent();
    }

    public NewUserWalkthroughWindow(NewUserWalkthroughViewModel viewModel) : this()
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

    private void OnSetAccentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string accentName } && DataContext is NewUserWalkthroughViewModel vm)
        {
            vm.SetAccentCommand.Execute(accentName);
        }
    }
}