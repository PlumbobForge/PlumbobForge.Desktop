using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Dialogs;

// Tidy by Type preview: each kind with its file count, a tick to include it and the Packages folder it goes to.
// Closes with what to move, or null.
public partial class TidyByTypeDialogWindow : Window
{
    private readonly List<(ContentManagerViewModel.TidyGroup Group, CheckBox Box, TextBox Folder)> _rows = new();

    public TidyByTypeDialogWindow()
    {
        InitializeComponent();
    }

    public TidyByTypeDialogWindow(IReadOnlyList<ContentManagerViewModel.TidyGroup> groups) : this()
    {
        foreach (var group in groups)
        {
            var box = new CheckBox
            {
                IsChecked = true,
                Content = string.Format(L(group.Items.Count == 1 ? "library.tidy_group_one" : "library.tidy_group"), group.Label, group.Items.Count),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = group.Kind,
            };
            ToolTip.SetTip(box, string.Join("\n", group.Items.Take(12).Select(i => i.FileName)) + (group.Items.Count > 12 ? "\n…" : string.Empty));
            var folder = new TextBox { Text = group.DefaultFolder, Width = 170, FontSize = 12, Tag = group.Kind };
            var prefix = new TextBlock { Text = "Packages /", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            prefix.Foreground = this.FindResource("TextMuted") as Avalonia.Media.IBrush;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto, Auto") };
            row.Children.Add(box);
            Grid.SetColumn(prefix, 1);
            row.Children.Add(prefix);
            Grid.SetColumn(folder, 2);
            row.Children.Add(folder);
            GroupsPanel.Children.Add(new Border
            {
                Background = this.FindResource("BgCard") as Avalonia.Media.IBrush,
                BorderBrush = this.FindResource("BorderSubtle") as Avalonia.Media.IBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8),
                Child = row,
            });

            box.IsCheckedChanged += (_, _) =>
            {
                folder.IsEnabled = box.IsChecked == true;
                Refresh();
            };
            folder.TextChanged += (_, _) => Refresh();
            _rows.Add((group, box, folder));
        }
        Refresh();
    }

    private static string L(string key) => LocalizationManager.Instance.GetString(key);

    private void Refresh()
    {
        int count = _rows.Where(r => r.Box.IsChecked == true).Sum(r => r.Group.Items.Count);
        bool valid = _rows.Where(r => r.Box.IsChecked == true).All(r => ContentManagerViewModel.IsValidTidyFolder(r.Folder.Text));
        foreach (var r in _rows) r.Folder.Classes.Set("error", r.Box.IsChecked == true && !ContentManagerViewModel.IsValidTidyFolder(r.Folder.Text));
        MoveButton.Content = string.Format(L(count == 1 ? "library.tidy_move_one" : "library.tidy_move"), count);
        MoveButton.IsEnabled = count > 0 && valid;
    }

    // What the dialog would close with now (the tests read it too).
    public List<ContentManagerViewModel.TidyChoice> Choices() =>
        _rows.Where(r => r.Box.IsChecked == true)
            .Select(r => new ContentManagerViewModel.TidyChoice(r.Group.Kind, r.Folder.Text!.Trim(), r.Group.Items.Select(i => i.Entity.Id).ToList()))
            .ToList();

    private void OnHeaderPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnMoveClick(object? sender, RoutedEventArgs e)
    {
        if (!MoveButton.IsEnabled) return;
        Close(Choices());
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
