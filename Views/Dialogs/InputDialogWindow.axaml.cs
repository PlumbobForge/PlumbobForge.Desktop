using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.Utils;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class InputDialogWindow : Window
{
    private readonly string? _extension;
    private readonly bool _skipNameValidation;

    public InputDialogWindow()
    {
        InitializeComponent();
    }

    public InputDialogWindow(string title, string prompt, string initialValue, string? extension = null) : this()
    {
        Title = title;
        TitleTextBlock.Text = title;
        PromptTextBlock.Text = prompt;
        ValueTextBox.Text = initialValue;
        _extension = extension;
        _skipNameValidation = title.Contains("Description", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(extension))
        {
            ExtensionBadge.IsVisible = true;
            ExtensionTextBlock.Text = extension.StartsWith('.') ? extension : "." + extension;
        }

        ValidateInput();
    }

    private void OnValueChanged(object? sender, TextChangedEventArgs e)
    {
        ValidateInput();
    }

    private bool ValidateInput()
    {
        var text = ValueTextBox.Text?.Trim();

        if (_skipNameValidation)
        {
            ErrorTextBlock.IsVisible = false;
            SaveButton.IsEnabled = true;
            return true;
        }

        if (string.IsNullOrEmpty(text))
        {
            ErrorTextBlock.Text = "Name cannot be empty.";
            ErrorTextBlock.IsVisible = true;
            SaveButton.IsEnabled = false;
            return false;
        }

        if (!NameValidator.IsValidName(text, out var error))
        {
            ErrorTextBlock.Text = error;
            ErrorTextBlock.IsVisible = true;
            SaveButton.IsEnabled = false;
            return false;
        }

        ErrorTextBlock.IsVisible = false;
        SaveButton.IsEnabled = true;
        return true;
    }

    private void OnHeaderPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (!ValidateInput()) return;

        var text = ValueTextBox.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(_extension))
        {
            var ext = _extension.StartsWith('.') ? _extension : "." + _extension;
            if (!text.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                text += ext;
            }
        }
        Close(text);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
