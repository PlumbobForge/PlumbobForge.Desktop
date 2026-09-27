using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class DuplicateSetNameDialogWindow : Window
{
    private readonly string _suggestedName;

    public DuplicateSetNameDialogWindow() : this("A set with this name already exists.", "New Name (2)")
    {
    }

    public DuplicateSetNameDialogWindow(string message, string suggestedName)
    {
        InitializeComponent();
        _suggestedName = suggestedName;
        MessageTextBlock.Text = message;
        SuggestionTextBlock.Text = $"Clicking below will create the set using the next available name: '{suggestedName}'.";
        RenameButton.Content = $"Rename to {suggestedName}";
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        Close(_suggestedName);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
