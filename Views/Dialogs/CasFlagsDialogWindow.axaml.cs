using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class CasFlagsDialogWindow : Window
{
    public CasFlagsDialogWindow()
    {
        InitializeComponent();
    }

    public CasFlagsDialogWindow(CasFlagsBatchViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAsync();
    }

    private CasFlagsBatchViewModel? ViewModel => DataContext as CasFlagsBatchViewModel;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // A save in progress has to finish: items are rewritten one by one.
        if (ViewModel?.IsSaving == true) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel?.Dispose();
        base.OnClosed(e);
    }

    private void OnHeaderPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.ApplyAsync();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
