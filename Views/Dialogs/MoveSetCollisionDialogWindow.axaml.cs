using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public enum MoveSetCollisionAction
{
    Cancel,
    Merge,
    Rename
}

public record MoveSetCollisionResult(MoveSetCollisionAction Action, string? RenamedName);

public partial class MoveSetCollisionDialogWindow : Window
{
    private readonly string _suggestedName;

    public MoveSetCollisionDialogWindow() : this("A set with this name already exists in the destination.", "New Name (2)")
    {
    }

    public MoveSetCollisionDialogWindow(string message, string suggestedName)
    {
        InitializeComponent();
        _suggestedName = suggestedName;
        MessageTextBlock.Text = message;
        DescriptionTextBlock.Text = $"Merge will combine all items into the existing set.\nRename will move this set as '{suggestedName}'.";
        RenameButton.Content = $"Rename to {suggestedName}";
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMergeClick(object? sender, RoutedEventArgs e)
    {
        Close(new MoveSetCollisionResult(MoveSetCollisionAction.Merge, null));
    }

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        Close(new MoveSetCollisionResult(MoveSetCollisionAction.Rename, _suggestedName));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new MoveSetCollisionResult(MoveSetCollisionAction.Cancel, null));
    }
}
