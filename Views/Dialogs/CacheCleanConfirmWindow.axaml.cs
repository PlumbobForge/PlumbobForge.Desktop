using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.Views.Dialogs;

// One row per core cache file: ticked files are deleted, unticked ones are kept.
public class CacheFileRow
{
    public CacheFileRow(string name, string state, bool isSelected)
    {
        Name = name;
        State = state;
        IsSelected = isSelected;
    }

    public string Name { get; }
    public string State { get; }
    public bool IsSelected { get; set; }
}

// Clears a chosen part of the five core game cache files. The unticked files are remembered and the automatic
// clear after mod changes keeps them too (Sims3GameProcess.LoadKeptCaches).
public partial class CacheCleanConfirmWindow : Window
{
    private readonly Func<IReadOnlyCollection<string>, Task<CacheCleanResult>>? _clean;
    private readonly string _sims3Folder = string.Empty;
    private readonly string _appDataDir = string.Empty;
    private List<CacheFileRow> _rows = new();
    private bool _done;

    public CacheCleanConfirmWindow()
    {
        InitializeComponent();
    }

    public CacheCleanConfirmWindow(string sims3Folder, string appDataDir, Func<IReadOnlyCollection<string>, Task<CacheCleanResult>> clean) : this()
    {
        _sims3Folder = sims3Folder;
        _appDataDir = appDataDir;
        _clean = clean;
        var kept = Sims3GameProcess.LoadKeptCaches(appDataDir);
        RefreshFiles(name => !kept.Contains(name));
    }

    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    private void RefreshFiles(Func<string, bool> isSelected)
    {
        _rows = Sims3GameProcess.CoreCacheFiles.Select(name =>
        {
            var path = Path.Combine(_sims3Folder, name);
            var state = File.Exists(path) ? CacheGroupInfo.FormatBytes(new FileInfo(path).Length) : L("dialogs.cache_files.not_present");
            return new CacheFileRow(name, state, isSelected(name));
        }).ToList();
        FileList.ItemsSource = _rows;
    }

    private void ShowStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = this.FindResource(isError ? "StatusRed" : "StatusGreen") as IBrush;
        StatusText.IsVisible = true;
    }

    private async void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (_done || _clean == null)
        {
            Close(_done);
            return;
        }

        if (Sims3GameProcess.IsGameRunning())
        {
            ShowStatus(L("dialogs.cache_files.game_running"), isError: true);
            return;
        }

        var selected = _rows.Where(r => r.IsSelected).Select(r => r.Name).ToList();
        if (selected.Count == 0)
        {
            ShowStatus(L("dialogs.cache_files.none_ticked"), isError: true);
            return;
        }

        ClearButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        try
        {
            Sims3GameProcess.SaveKeptCaches(_appDataDir, _rows.Where(r => !r.IsSelected).Select(r => r.Name));
            var result = await _clean(selected);
            RefreshFiles(name => selected.Contains(name));
            var summary = result.FilesDeleted switch
            {
                0 => L("dialogs.cache_files.nothing_present"),
                1 => L("dialogs.cache_files.cleared_one", result.FormattedBytesReclaimed),
                _ => L("dialogs.cache_files.cleared_many", result.FilesDeleted, result.FormattedBytesReclaimed)
            };
            ShowStatus(result.Errors.Count == 0 ? summary : summary + "\n" + string.Join("\n", result.Errors), result.Errors.Count > 0);
            _done = true;
            ClearButton.Content = L("dialogs.cache_files.done");
            CancelButton.IsVisible = false;
        }
        catch (Exception ex)
        {
            ShowStatus(L("dialogs.cache_files.failed", ex.Message), isError: true);
            CancelButton.IsEnabled = true;
        }
        finally
        {
            ClearButton.IsEnabled = true;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(_done);
}
