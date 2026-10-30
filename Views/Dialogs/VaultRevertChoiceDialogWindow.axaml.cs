using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class VaultRevertChoiceDialogWindow : Window
{
    private readonly string _libraryPath;
    private readonly bool _isFolderBackupAvailable;

    public VaultRevertChoiceDialogWindow() : this(string.Empty) { }

    public VaultRevertChoiceDialogWindow(string libraryPath)
    {
        InitializeComponent();
        _libraryPath = libraryPath;

        string backupDir = _libraryPath + "_Backup";
        _isFolderBackupAvailable = Directory.Exists(backupDir) && Directory.EnumerateFileSystemEntries(backupDir).Any();

        string parentDir = Path.GetDirectoryName(_libraryPath) ?? _libraryPath;
        string defaultZip = Path.Combine(parentDir, "Library_Backup.zip");
        if (File.Exists(defaultZip))
        {
            ZipPathTextBox.Text = defaultZip;
        }

        if (!_isFolderBackupAvailable)
        {
            FolderCard.Classes.Add("disabled");
            FolderOptionRadio.IsEnabled = false;
            FolderNotFoundBadge.IsVisible = true;

            if (File.Exists(defaultZip))
            {
                ZipOptionRadio.IsChecked = true;
            }
            else
            {
                VaultOptionRadio.IsChecked = true;
            }
        }
        else
        {
            FolderOptionRadio.IsChecked = true;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnFolderCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isFolderBackupAvailable) FolderOptionRadio.IsChecked = true;
    }

    private void OnZipCardPressed(object? sender, PointerPressedEventArgs e)
    {
        ZipOptionRadio.IsChecked = true;
    }

    private void OnVaultCardPressed(object? sender, PointerPressedEventArgs e)
    {
        VaultOptionRadio.IsChecked = true;
    }

    private async void OnBrowseZipClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Library Backup Archive",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Zip Archive (*.zip)") { Patterns = new[] { "*.zip" } }
            }
        });

        if (files.Count > 0)
        {
            ZipPathTextBox.Text = files[0].Path.LocalPath;
            ZipOptionRadio.IsChecked = true;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        VaultRevertMode mode;
        if (FolderOptionRadio.IsChecked == true && _isFolderBackupAvailable)
        {
            mode = VaultRevertMode.FolderBackup;
        }
        else if (ZipOptionRadio.IsChecked == true)
        {
            mode = VaultRevertMode.ZipArchive;
        }
        else
        {
            mode = VaultRevertMode.ReconstructFromVault;
        }

        string? customZip = !string.IsNullOrWhiteSpace(ZipPathTextBox.Text) ? ZipPathTextBox.Text.Trim() : null;
        bool preserveDelta = PreserveDeltaCheckbox.IsChecked == true;

        Close(new VaultRevertChoice(mode, customZip, preserveDelta));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
