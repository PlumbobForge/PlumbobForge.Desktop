using Avalonia.Controls;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Health;

public partial class ConflictScannerView : UserControl
{
    public ConflictScannerView()
    {
        InitializeComponent();
    }

    private void OnNavigateToPrimaryPackageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SimpleConflictCardViewModel card && DataContext is HealthViewModel vm)
        {
            if (card.PrimaryPackage != null)
            {
                vm.NavigateToPackageItemCommand.Execute(card.PrimaryPackage);
            }
        }
    }

    private void OnNavigateToSecondaryPackageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SimpleConflictCardViewModel card && DataContext is HealthViewModel vm)
        {
            if (card.SecondaryPackage != null)
            {
                vm.NavigateToPackageItemCommand.Execute(card.SecondaryPackage);
            }
        }
    }
}