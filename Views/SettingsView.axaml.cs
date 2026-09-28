using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnSetAccentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string accentName } && DataContext is SettingsViewModel vm)
        {
            vm.SetAccentCommand.Execute(accentName);
        }
    }

    private void OnRemoveObservedFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folder } && DataContext is SettingsViewModel vm)
        {
            vm.RemoveObservedFolderCommand.Execute(folder);
        }
    }

    private void OnLanguageItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is LanguageOptionItemViewModel lang && DataContext is SettingsViewModel vm)
        {
            vm.SelectLanguage(lang);
            var dropdownBtn = this.FindControl<Button>("LanguageDropdownButton");
            dropdownBtn?.Flyout?.Hide();
        }
    }

    private async void OnBrowseDocumentBaseDirClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider != null && DataContext is SettingsViewModel vm)
        {
            await vm.BrowseDocumentBaseDirAsync(topLevel.StorageProvider);
        }
    }

    private async void OnBrowseGameFilesDirClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider != null && DataContext is SettingsViewModel vm)
        {
            await vm.BrowseGameFilesDirAsync(topLevel.StorageProvider);
        }
    }

    private async void OnAddObservedFolderClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider != null && DataContext is SettingsViewModel vm)
        {
            await vm.AddObservedFolderAsync(topLevel.StorageProvider);
        }
    }

    private void OnDocumentBaseDirLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && DataContext is SettingsViewModel vm)
        {
            vm.PromptOrApplyDocumentBaseDir(tb.Text ?? string.Empty);
        }
    }

    private void OnDocumentBaseDirKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && DataContext is SettingsViewModel vm)
        {
            vm.PromptOrApplyDocumentBaseDir(tb.Text ?? string.Empty);
            TopLevel.GetTopLevel(this)?.Focus();
        }
    }

    private void OnGameFilesDirLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && DataContext is SettingsViewModel vm)
        {
            vm.GameFilesDir = tb.Text ?? string.Empty;
        }
    }

    private void OnGameFilesDirKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && DataContext is SettingsViewModel vm)
        {
            vm.GameFilesDir = tb.Text ?? string.Empty;
            TopLevel.GetTopLevel(this)?.Focus();
        }
    }
}
