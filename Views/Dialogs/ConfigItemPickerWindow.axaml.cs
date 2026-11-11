using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class ConfigItemPickerWindow : Window
{
    public ConfigItemPickerWindow()
    {
        InitializeComponent();
    }

    public ConfigItemPickerWindow(ConfigItemPickerViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) =>
        {
            SearchBox.Focus();
            await viewModel.LoadAsync();
        };
    }

    private ConfigItemPickerViewModel? ViewModel => DataContext as ConfigItemPickerViewModel;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnItemRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PickerItemRow row } && row.CanPick)
        {
            row.IsChecked = !row.IsChecked;
            e.Handled = true;
        }
    }

    private void OnClearSearchClick(object? sender, RoutedEventArgs e) => ViewModel?.ClearSearch();

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.Confirm();
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
