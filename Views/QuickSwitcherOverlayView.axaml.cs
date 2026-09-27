using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views;

public partial class QuickSwitcherOverlayView : UserControl
{
    public QuickSwitcherOverlayView()
    {
        InitializeComponent();
        PropertyChanged += OnControlPropertyChanged;
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && IsVisible)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is QuickSwitcherViewModel vm)
        {
            vm.Close();
            e.Handled = true;
        }
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Prevent clicking inside the card from closing the overlay
        e.Handled = true;
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is QuickSwitcherItemViewModel item && DataContext is QuickSwitcherViewModel vm)
        {
            _ = vm.ExecuteSelectionAsync(item);
            e.Handled = true;
        }
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not QuickSwitcherViewModel vm) return;

        if (e.Key == Key.Down)
        {
            vm.MoveSelectionDown();
            ScrollSelectedItemIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.MoveSelectionUp();
            ScrollSelectedItemIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _ = vm.ExecuteSelectionAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.Close();
            e.Handled = true;
        }
    }

    private void ScrollSelectedItemIntoView()
    {
        if (DataContext is not QuickSwitcherViewModel vm || vm.SelectedIndex < 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            var container = ResultsItemsControl.ContainerFromIndex(vm.SelectedIndex) as Control;
            container?.BringIntoView();
        });
    }
}
