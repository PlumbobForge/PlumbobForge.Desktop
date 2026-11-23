using Avalonia.Controls;
using Avalonia.Input;
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
            vm.DeleteCollectionWithUndo(item);
        }
    }

    // Wide layout: delete the collection that is open in the editor (Undo on the toast).
    private void OnDeleteOpenCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HealthViewModel vm && vm.EditingCollectionItem is { } item) vm.DeleteCollectionWithUndo(item);
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

    private void OnRemoveIncludedItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CollectionIncludedItemViewModel item && DataContext is HealthViewModel vm)
        {
            vm.RemoveIncludedItemCommand.Execute(item);
        }
    }

    private void OnAddSetToCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CollectionSetOptionViewModel item && DataContext is HealthViewModel vm)
        {
            vm.AddSetToCollectionCommand.Execute(item);
        }
    }

    private async void OnImportCustomIconClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider != null && DataContext is HealthViewModel vm)
        {
            await vm.ImportCustomIconAsync(topLevel.StorageProvider);
        }
    }

    private async void OnDeleteCustomIconClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is CollectionIconItemViewModel item && DataContext is HealthViewModel vm)
        {
            await vm.DeleteCustomIconAsync(item);
        }
    }

    private void OnIconContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control control && control.DataContext is CollectionIconItemViewModel icon)
        {
            if (!icon.IsCustom)
            {
                e.Handled = true;
            }
        }
    }
}