using Avalonia.Controls;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Health;

public partial class CollectionCreatorView : UserControl
{
    public CollectionCreatorView()
    {
        InitializeComponent();
    }

    private void OnEditCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CollectionItemViewModel item && DataContext is HealthViewModel vm)
        {
            vm.EditCollectionCommand.Execute(item);
        }
    }

    private void OnDeleteCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CollectionItemViewModel item && DataContext is HealthViewModel vm)
        {
            vm.DeleteCollectionCommand.Execute(item);
        }
    }

    private void OnSelectIconClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CollectionIconItemViewModel item && DataContext is HealthViewModel vm)
        {
            vm.SelectIconCommand.Execute(item);
        }
    }

    private void OnRemoveAssignedSetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CollectionSetOptionViewModel item && DataContext is HealthViewModel vm)
        {
            vm.RemoveAssignedSetCommand.Execute(item);
        }
    }

    private void OnAddSetToCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CollectionSetOptionViewModel item && DataContext is HealthViewModel vm)
        {
            vm.AddSetToCollectionCommand.Execute(item);
        }
    }
}