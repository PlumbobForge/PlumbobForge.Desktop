using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views;

public partial class QuickSwitcherOverlayView : UserControl
{
    private QuickSwitcherViewModel? _subscribedVm;

    public QuickSwitcherOverlayView()
    {
        InitializeComponent();
        PropertyChanged += OnControlPropertyChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null) _subscribedVm.CompletionApplied -= OnCompletionApplied;
        _subscribedVm = DataContext as QuickSwitcherViewModel;
        if (_subscribedVm != null) _subscribedVm.CompletionApplied += OnCompletionApplied;
    }

    private void OnCompletionApplied()
    {
        // The binding rewrites the text first; move the caret after it so typing continues at the end.
        Dispatcher.UIThread.Post(() =>
        {
            SearchBox.Focus();
            SearchBox.ClearSelection();
            SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
        }, DispatcherPriority.Input);
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
        else if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None)
        {
            // Tab autocompletes instead of moving focus out of the search box.
            _ = CompleteAfterSearchAsync(vm);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _ = ExecuteAfterSearchAsync(vm);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.Close();
            e.Handled = true;
        }
    }

    private static async Task CompleteAfterSearchAsync(QuickSwitcherViewModel vm)
    {
        await vm.WaitForPendingSearchAsync();
        vm.CompleteSelection();
    }

    private static async Task ExecuteAfterSearchAsync(QuickSwitcherViewModel vm)
    {
        await vm.WaitForPendingSearchAsync();
        await vm.ExecuteSelectionAsync();
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
