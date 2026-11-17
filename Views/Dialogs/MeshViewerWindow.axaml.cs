using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class MeshViewerWindow : Window
{
    // True when closed with "Open in CC Optimizer".
    public bool OpenInOptimizer { get; private set; }

    public MeshViewerWindow()
    {
        InitializeComponent();
    }

    public MeshViewerWindow(MeshViewerViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as MeshViewerViewModel)?.Dispose();
        base.OnClosed(e);
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnOpenInOptimizerClick(object? sender, RoutedEventArgs e)
    {
        OpenInOptimizer = true;
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

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
}
