using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using PlumbobForge.Desktop.Controls.Icons;

namespace PlumbobForge.Desktop.Views.Controls;

// Context menus that change with what was clicked are built in code: entries in groups, a divider only
// between groups that have something in them, and icons only where an entry asks for one.
public static class MenuBuilder
{
    public static MenuItem Item(string header, Action onClick, string? icon = null, string? gesture = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (icon != null) item.Icon = Icon(icon);
        if (gesture != null) item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return item;
    }

    // Destructive entries: red text, no icon.
    public static MenuItem Danger(string header, Action onClick, string? gesture = null, bool enabled = true)
    {
        var item = Item(header, onClick, gesture: gesture, enabled: enabled);
        item.Classes.Add("danger");
        return item;
    }

    public static MenuItem? Sub(string header, IEnumerable<Control?> children, string? icon = null)
    {
        var list = children.Where(c => c != null).Cast<Control>().ToList();
        if (list.Count == 0) return null;
        var item = new MenuItem { Header = header };
        if (icon != null) item.Icon = Icon(icon);
        foreach (var c in list) item.Items.Add(c);
        return item;
    }

    // A checkable entry; null is "some of the selection", shown with a dash.
    public static MenuItem Check(string header, bool? state, Action onClick)
    {
        var item = Item(header, onClick);
        if (state == null) item.Icon = new PackIconBoxIcons { Kind = PackIconBoxIconsKind.RegularMinus, Width = 14, Height = 14 };
        else
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = state.Value;
        }
        return item;
    }

    public static Separator Divider() => new();

    // A long list of places (Move to Set ▸ with dozens of sets): the latest few on top, then a box that narrows
    // the rest as you type. Short lists stay plain.
    public const int FilterFrom = 8;

    public static MenuItem? PlaceSub(string header, IReadOnlyList<(string Label, Action Go)> places, IReadOnlyList<(string Label, Action Go)> recent,
        string filterHint, string noMatch)
    {
        if (places.Count == 0) return null;
        if (places.Count < FilterFrom) return Sub(header, places.Select(p => (Control?)Item(p.Label, p.Go)));

        var item = new MenuItem { Header = header };
        var recentEntries = recent.Select(r => Item(r.Label, r.Go)).ToList();
        foreach (var r in recentEntries) item.Items.Add(r);
        var recentDivider = Divider();
        if (recentEntries.Count > 0) item.Items.Add(recentDivider);

        var box = new TextBox { Watermark = filterHint, MinWidth = 200, Margin = new Avalonia.Thickness(4, 2, 4, 4), Classes = { "menu-filter" } };
        item.Items.Add(box);
        var entries = places.Select(p => Item(p.Label, p.Go)).ToList();
        foreach (var e in entries) item.Items.Add(e);
        var none = new MenuItem { Header = noMatch, IsEnabled = false, IsVisible = false };
        item.Items.Add(none);

        box.TextChanged += (_, _) =>
        {
            string q = box.Text?.Trim() ?? string.Empty;
            foreach (var e in entries.Concat(recentEntries)) e.IsVisible = q.Length == 0 || (e.Header?.ToString() ?? string.Empty).Contains(q, StringComparison.CurrentCultureIgnoreCase);
            recentDivider.IsVisible = recentEntries.Any(e => e.IsVisible);
            none.IsVisible = entries.All(e => !e.IsVisible);
        };
        // Typing goes to the box, not to the menu's own keyboard navigation.
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Up or Key.Down or Key.Enter) return;
            e.Handled = true;
        };
        item.SubmenuOpened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => box.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
        return item;
    }

    // Puts the groups into the menu with a divider between each pair of non-empty groups.
    public static void Fill(ItemsControl menu, params IEnumerable<Control?>[] groups)
    {
        menu.Items.Clear();
        foreach (var group in groups)
        {
            var entries = group.Where(c => c != null).Cast<Control>().ToList();
            if (entries.Count == 0) continue;
            if (menu.Items.Count > 0) menu.Items.Add(Divider());
            foreach (var e in entries) menu.Items.Add(e);
        }
    }

    private static PackIconBoxIcons Icon(string kind) =>
        new() { Kind = Enum.Parse<PackIconBoxIconsKind>(kind), Width = 16, Height = 16 };
}
