using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.Services;

// Remembers the main window's size, position and maximized state between launches.
// Kept in its own file so it never races the settings saves.
public static class WindowStateService
{
    public sealed record SavedWindowState(int X, int Y, double Width, double Height, bool Maximized);

    public static string FilePath => Path.Combine(AppPaths.AppDataDir, "window-state.json");

    public static SavedWindowState? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var state = JsonSerializer.Deserialize<SavedWindowState>(File.ReadAllText(FilePath));
            if (state == null || state.Width < 200 || state.Height < 150) return null;
            return state;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Save(SavedWindowState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Could not save the window state", ex, "WindowStateService");
        }
    }

    // Call before the window is shown. Restores the saved bounds, then follows the window
    // so the normal (not maximized) bounds are known even when it closes maximized.
    public static void Attach(Window window)
    {
        var saved = Load();
        if (saved != null)
        {
            window.Width = Math.Max(saved.Width, window.MinWidth);
            window.Height = Math.Max(saved.Height, window.MinHeight);
            if (IsOnScreen(window, saved))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(saved.X, saved.Y);
            }
            if (saved.Maximized) window.WindowState = WindowState.Maximized;
        }

        var normal = saved ?? new SavedWindowState(window.Position.X, window.Position.Y, window.Width, window.Height, false);
        void Remember()
        {
            if (window.WindowState != WindowState.Normal || !window.IsVisible) return;
            normal = new SavedWindowState(window.Position.X, window.Position.Y, window.Width, window.Height, false);
        }
        window.PositionChanged += (_, _) => Remember();
        window.Resized += (_, _) => Remember();
        window.Opened += (_, _) => Remember();
        window.Closed += (_, _) => Save(normal with { Maximized = window.WindowState == WindowState.Maximized });
    }

    // A monitor may have been unplugged since: keep the window only if its title bar is still reachable.
    private static bool IsOnScreen(Window window, SavedWindowState saved)
    {
        var screens = window.Screens?.All;
        if (screens == null || screens.Count == 0) return true;
        return IsOnScreen(screens.Select(s => (s.WorkingArea, s.Scaling)), saved);
    }

    public static bool IsOnScreen(IEnumerable<(PixelRect WorkingArea, double Scaling)> screens, SavedWindowState saved) =>
        screens.Any(s =>
        {
            int width = (int)(saved.Width * s.Scaling);
            var titleBar = new PixelRect(saved.X, saved.Y, Math.Max(width, 1), (int)(44 * s.Scaling));
            var visible = s.WorkingArea.Intersect(titleBar);
            return visible.Width >= 120 && visible.Height >= 20;
        });
}
