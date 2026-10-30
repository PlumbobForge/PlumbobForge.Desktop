using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class LookInsideWindow : Window
{
    public LookInsideWindow()
    {
        InitializeComponent();
    }

    private bool _closeConfirmed;

    public LookInsideWindow(LookInsideViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAsync();
        Closing += OnWindowClosing;
        Closed += (_, _) => viewModel.Dispose();
    }

    private LookInsideViewModel? ViewModel => DataContext as LookInsideViewModel;

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ViewModel is not { } vm || _closeConfirmed) return;
        if (vm.IsSaving)
        {
            e.Cancel = true;
        }
        else if (vm.HasPendingChanges)
        {
            e.Cancel = true;
            vm.ShowUnsavedPrompt = true;
        }
    }

    private void OnKeepEditingClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ShowUnsavedPrompt = false;
    }

    private void OnDiscardAndCloseClick(object? sender, RoutedEventArgs e)
    {
        _closeConfirmed = true;
        Close();
    }

    private async System.Threading.Tasks.Task<string?> PickImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Instance.GetString("look_inside.pick_image"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images (*.png, *.jpg, *.jpeg, *.bmp, *.webp, *.gif)") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif" } },
                new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
            }
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async void OnReplaceThumbnailClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CanEdit: true } vm || sender is not Button { DataContext: LookInsideThumbnailItem item }) return;
        if (await PickImageAsync() is { } path) await vm.StageReplaceAsync(item, path);
    }

    private async void OnReplaceAllClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CanEdit: true } vm || sender is not Button { DataContext: LookInsideObjectItem obj }) return;
        if (await PickImageAsync() is { } path) await vm.StageReplaceAllAsync(obj, path);
    }

    private async void OnAddThumbnailsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CanEdit: true } vm || sender is not Button { DataContext: LookInsideObjectItem obj }) return;
        if (await PickImageAsync() is { } path) await vm.StageAddAsync(obj, path);
    }

    private void OnRemoveThumbnailClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LookInsideThumbnailItem item }) ViewModel?.StageRemove(item);
    }

    private void OnUndoThumbnailClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LookInsideThumbnailItem item }) ViewModel?.Undo(item);
    }

    private void OnDiscardClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.DiscardChanges();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { CanSave: true } vm) await vm.SaveAsync();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void OnCopyKeyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key } && !string.IsNullOrEmpty(key) && Clipboard != null)
        {
            try
            {
                await Clipboard.SetTextAsync(key);
            }
            catch { }
        }
    }

    private async void OnExportThumbnailClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CanExport: true } vm || sender is not Button { DataContext: LookInsideThumbnailItem { IsNewSlot: false } item }) return;

        string suggested = vm.GetSuggestedFileName(item);
        string ext = Path.GetExtension(suggested).TrimStart('.');
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Instance.GetString("look_inside.export_one"),
            SuggestedFileName = suggested,
            DefaultExtension = ext,
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType($"{ext.ToUpperInvariant()} (*.{ext})") { Patterns = new[] { $"*.{ext}" } },
                new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        string? path = file?.TryGetLocalPath();
        if (path != null)
        {
            await vm.ExportThumbnailAsync(item, path);
        }
    }

    private async void OnExportObjectClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CanExport: true } vm || sender is not Button { DataContext: LookInsideObjectItem obj }) return;

        string? folder = await PickFolderAsync();
        if (folder != null)
        {
            await vm.ExportManyAsync(folder, obj);
        }
    }

    private async void OnExportAllClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CanExport: true } vm) return;

        string? folder = await PickFolderAsync();
        if (folder != null)
        {
            await vm.ExportManyAsync(folder);
        }
    }

    private async System.Threading.Tasks.Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Instance.GetString("look_inside.pick_folder"),
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void OnOpenExportFolderClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.OpenLastExportFolder();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
