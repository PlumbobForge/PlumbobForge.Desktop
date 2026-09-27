using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views.Dialogs;

public record RetagDialogResult(string PackageType, string UserTags, string CasCategories);

public partial class RetagDialogWindow : Window
{
    public ObservableCollection<string> Tags { get; } = new();
    private string _selectedType = "CAS";
    private string? _selectedCategory;

    public RetagDialogWindow()
    {
        InitializeComponent();
        UserTagsItemsControl.ItemsSource = Tags;
        
        // Attach tunneling KeyDown event to intercept Backspace before TextBox swallows it
        TagInputTextBox.AddHandler(InputElement.KeyDownEvent, OnTagInputKeyDown, RoutingStrategies.Tunnel);
    }

    public RetagDialogWindow(string currentPackageType, string? currentUserTags, string? currentCasCategories) : this()
    {
        // Populate Tags
        if (!string.IsNullOrWhiteSpace(currentUserTags))
        {
            var rawTags = currentUserTags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var tag in rawTags)
            {
                var trimmed = tag.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !Tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    Tags.Add(trimmed);
                }
            }
        }

        // Set Type (CAS, BuildBuy, Other)
        if (string.Equals(currentPackageType, "BuildBuy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentPackageType, "Build-Buy", StringComparison.OrdinalIgnoreCase))
        {
            SetPackageType("BuildBuy");
        }
        else if (string.Equals(currentPackageType, "Other", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(currentPackageType, "World", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(currentPackageType, "Sim", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(currentPackageType, "Lot", StringComparison.OrdinalIgnoreCase))
        {
            SetPackageType("Other");
        }
        else
        {
            SetPackageType("CAS");
        }

        // Set Category
        _selectedCategory = currentCasCategories?.Trim();
        UpdateCategorySelection();
    }

    private void SetPackageType(string type)
    {
        _selectedType = type;
        TypeCasButton.IsChecked = (type == "CAS");
        TypeBuildBuyButton.IsChecked = (type == "BuildBuy");
        TypeOtherButton.IsChecked = (type == "Other");

        // Visibility
        CategoriesSection.IsVisible = (type != "BuildBuy");
        CasCategoriesPanel.IsVisible = (type == "CAS");
        OtherCategoriesPanel.IsVisible = (type == "Other");
    }

    private void OnTypeCasClick(object? sender, RoutedEventArgs e) => SetPackageType("CAS");
    private void OnTypeBuildBuyClick(object? sender, RoutedEventArgs e) => SetPackageType("BuildBuy");
    private void OnTypeOtherClick(object? sender, RoutedEventArgs e) => SetPackageType("Other");

    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.Tag is string catName)
        {
            _selectedCategory = catName;
            UpdateCategorySelection();
        }
    }

    private void UpdateCategorySelection()
    {
        // Uncheck all in CAS panel
        foreach (var child in CasCategoriesPanel.Children)
        {
            if (child is ToggleButton tb)
            {
                tb.IsChecked = string.Equals(tb.Tag?.ToString(), _selectedCategory, StringComparison.OrdinalIgnoreCase);
            }
        }
        // Uncheck all in Other panel
        foreach (var child in OtherCategoriesPanel.Children)
        {
            if (child is ToggleButton tb)
            {
                tb.IsChecked = string.Equals(tb.Tag?.ToString(), _selectedCategory, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    #region Tag Input Logic
    private void OnTagInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitCurrentTagInput();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            if (string.IsNullOrEmpty(TagInputTextBox.Text) && Tags.Count > 0)
            {
                Tags.RemoveAt(Tags.Count - 1);
                e.Handled = true;
            }
        }
    }

    private void OnTagInputTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text == ",")
        {
            CommitCurrentTagInput();
            e.Handled = true;
        }
    }

    private void CommitCurrentTagInput()
    {
        var text = TagInputTextBox.Text?.Trim(',', ' ');
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!Tags.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                Tags.Add(text);
            }
            TagInputTextBox.Text = string.Empty;
        }
    }

    private void OnRemoveTagClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string tag)
        {
            Tags.Remove(tag);
        }
    }
    #endregion

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        CommitCurrentTagInput();

        string userTags = string.Join(", ", Tags);
        string casCategory = _selectedCategory ?? string.Empty;

        Close(new RetagDialogResult(_selectedType, userTags, casCategory));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
