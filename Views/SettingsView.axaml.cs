using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PlumbobForge.Desktop.ViewModels;

using Avalonia.Threading;

namespace PlumbobForge.Desktop.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _subscribedVm;
    private Border? _currentHighlightedCard;
    private DateTime _highlightAppliedTime = DateTime.MinValue;

    public SettingsView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        AddHandler(InputElement.PointerPressedEvent, (s, e) => DismissHighlight(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(InputElement.KeyDownEvent, (s, e) => DismissHighlight(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.RequestScrollAndHighlight -= OnRequestScrollAndHighlight;
            _subscribedVm = null;
        }

        if (DataContext is SettingsViewModel vm)
        {
            _subscribedVm = vm;
            _subscribedVm.RequestScrollAndHighlight += OnRequestScrollAndHighlight;
        }
    }

    private void OnRequestScrollAndHighlight(string sectionName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = this.FindControl<Border>($"{sectionName}Card") ?? this.FindControl<Border>(sectionName);
            if (card != null)
            {
                if (_currentHighlightedCard != null)
                {
                    _currentHighlightedCard.Classes.Remove("highlighted");
                }

                _currentHighlightedCard = card;
                _currentHighlightedCard.Classes.Add("highlighted");
                _highlightAppliedTime = DateTime.UtcNow;

                card.BringIntoView();
            }
        }, DispatcherPriority.Loaded);
    }

    private void DismissHighlight()
    {
        if (_currentHighlightedCard == null) return;
        if ((DateTime.UtcNow - _highlightAppliedTime).TotalMilliseconds < 250) return;

        _currentHighlightedCard.Classes.Remove("highlighted");
        _currentHighlightedCard = null;
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

    private async void OnBrowseSims3UserDirClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider != null && DataContext is SettingsViewModel vm)
        {
            await vm.BrowseSims3UserDirAsync(topLevel.StorageProvider);
        }
    }

    private void OnSims3UserDirLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && DataContext is SettingsViewModel vm)
        {
            vm.Sims3UserDir = tb.Text ?? string.Empty;
        }
    }

    private void OnSims3UserDirKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && DataContext is SettingsViewModel vm)
        {
            vm.Sims3UserDir = tb.Text ?? string.Empty;
            TopLevel.GetTopLevel(this)?.Focus();
        }
    }
}
