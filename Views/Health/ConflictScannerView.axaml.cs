using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace PlumbobForge.Desktop.Views.Health;

public partial class ConflictScannerView : UserControl
{
    public ConflictScannerView()
    {
        InitializeComponent();
    }

    private void OnFilterPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is ScrollViewer sv && e.Delta.Y != 0)
        {
            double newX = Math.Clamp(sv.Offset.X - (e.Delta.Y * 60), 0, Math.Max(0, sv.Extent.Width - sv.Viewport.Width));
            sv.Offset = new Vector(newX, sv.Offset.Y);
            e.Handled = true;
        }
    }
}
