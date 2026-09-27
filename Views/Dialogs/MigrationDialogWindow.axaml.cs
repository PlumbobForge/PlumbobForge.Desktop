using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace PlumbobForge.Desktop.Views.Dialogs;

public enum MigrationSource
{
    CcMagic,
    S3mo
}

public record MigrationDialogResult(bool Confirmed, MigrationSource Source, bool IsFullMigration, string? CustomPath)
{
    public bool IsFullCcMagicMigration => IsFullMigration;
}

public partial class MigrationDialogWindow : Window
{
    private readonly IStorageProvider? _storageProvider;

    public MigrationDialogWindow()
    {
        InitializeComponent();

        if (CcMagicRadio != null)
        {
            CcMagicRadio.IsCheckedChanged += (_, _) => OnRadioStateChanged();
        }
        if (S3moRadio != null)
        {
            S3moRadio.IsCheckedChanged += (_, _) => OnRadioStateChanged();
        }

        if (CcMagicPathBox != null && string.IsNullOrWhiteSpace(CcMagicPathBox.Text))
        {
            var defaultCcMagic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Electronic Arts", "CC Magic");
            CcMagicPathBox.Text = defaultCcMagic;
        }

        ValidateInputs();
    }

    public MigrationDialogWindow(IStorageProvider? storageProvider) : this()
    {
        _storageProvider = storageProvider;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnRadioStateChanged()
    {
        bool isS3mo = S3moRadio?.IsChecked == true;
        if (S3moPathSection != null)
        {
            S3moPathSection.IsVisible = isS3mo;
        }
        if (CcMagicOptionsSection != null)
        {
            CcMagicOptionsSection.IsVisible = !isS3mo;
        }
        ValidateInputs();
    }

    private void OnS3moPathChanged(object? sender, TextChangedEventArgs e)
    {
        ValidateInputs();
    }

    private void OnCcMagicPathChanged(object? sender, TextChangedEventArgs e)
    {
        ValidateInputs();
    }

    private async void OnBrowseS3moClick(object? sender, RoutedEventArgs e)
    {
        var sp = _storageProvider ?? StorageProvider;
        if (sp == null) return;

        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select The Sims 3 Mod Organizer Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            if (S3moPathBox != null)
            {
                S3moPathBox.Text = path;
            }
        }
    }

    private async void OnBrowseCcMagicClick(object? sender, RoutedEventArgs e)
    {
        var sp = _storageProvider ?? StorageProvider;
        if (sp == null) return;

        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select CC Magic Installation Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            if (CcMagicPathBox != null)
            {
                CcMagicPathBox.Text = path;
            }
        }
    }

    private bool ValidateInputs()
    {
        if (StartButton == null) return false;

        if (ValidationErrorText != null) ValidationErrorText.IsVisible = false;
        if (CcMagicValidationErrorText != null) CcMagicValidationErrorText.IsVisible = false;

        if (S3moRadio?.IsChecked == true)
        {
            string path = S3moPathBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                StartButton.IsEnabled = false;
                return false;
            }

            if (!Directory.Exists(path))
            {
                if (ValidationErrorText != null)
                {
                    ValidationErrorText.Text = "Directory does not exist.";
                    ValidationErrorText.IsVisible = true;
                }
                StartButton.IsEnabled = false;
                return false;
            }

            string mods = Path.Combine(path, "Mods");
            string profiles = Path.Combine(path, "Profiles");
            if (!Directory.Exists(mods) && !Directory.Exists(profiles))
            {
                if (ValidationErrorText != null)
                {
                    ValidationErrorText.Text = "Selected folder does not contain 'Mods' or 'Profiles' subfolders.";
                    ValidationErrorText.IsVisible = true;
                }
                StartButton.IsEnabled = false;
                return false;
            }

            StartButton.IsEnabled = true;
            return true;
        }

        // CC Magic selected
        string ccPath = CcMagicPathBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ccPath))
        {
            StartButton.IsEnabled = false;
            return false;
        }

        if (!Directory.Exists(ccPath))
        {
            if (CcMagicValidationErrorText != null)
            {
                CcMagicValidationErrorText.Text = "CC Magic folder not found. Please browse to your CC Magic folder.";
                CcMagicValidationErrorText.IsVisible = true;
            }
            StartButton.IsEnabled = false;
            return false;
        }

        StartButton.IsEnabled = true;
        return true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new MigrationDialogResult(false, MigrationSource.CcMagic, true, null));
    }

    private void OnStartClick(object? sender, RoutedEventArgs e)
    {
        if (!ValidateInputs()) return;

        var source = S3moRadio?.IsChecked == true ? MigrationSource.S3mo : MigrationSource.CcMagic;
        bool isFull = source == MigrationSource.S3mo
            ? (S3moFullRadio?.IsChecked != false)
            : (CcMagicFullRadio?.IsChecked != false);
        string? customPath = source == MigrationSource.S3mo ? S3moPathBox?.Text?.Trim() : CcMagicPathBox?.Text?.Trim();

        Close(new MigrationDialogResult(true, source, isFull, customPath));
    }
}
