using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public record RecheckPackageTypesDialogResult(bool Confirmed, bool SkipUserTagged);

public partial class RecheckPackageTypesDialogWindow : Window
{
    public RecheckPackageTypesDialogWindow()
    {
        InitializeComponent();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new RecheckPackageTypesDialogResult(false, true));
    }

    private void OnStartClick(object? sender, RoutedEventArgs e)
    {
        bool skipUserTagged = SkipUserTaggedCheckBox?.IsChecked ?? true;
        Close(new RecheckPackageTypesDialogResult(true, skipUserTagged));
    }
}
