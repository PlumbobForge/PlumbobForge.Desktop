using System;
using System.IO;
using System.Text.Json;

namespace PlumbobForge.Desktop.Services;

// Title bar choices from Settings > Appearance, kept in their own small file so the library's
// debounced ui-state saves never overwrite them.
public static class TitleBarPreferences
{
    private sealed class State
    {
        public bool ShowQuickActions { get; set; }
    }

    private static State? _state;

    private static string FilePath => Path.Combine(PlumbobForge.Backend.Configuration.AppPaths.AppDataDir, "titlebar-ui-state.json");

    public static event Action? Changed;

    // Off by default: the title bar stays as it is unless the user switches the button on.
    public static bool ShowQuickActions
    {
        get => Current.ShowQuickActions;
        set
        {
            if (Current.ShowQuickActions == value) return;
            Current.ShowQuickActions = value;
            Save();
            Changed?.Invoke();
        }
    }

    private static State Current => _state ??= Load();

    private static State Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TitleBarPreferences] Error loading state: {ex.Message}");
        }
        return new State();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(PlumbobForge.Backend.Configuration.AppPaths.AppDataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TitleBarPreferences] Error saving state: {ex.Message}");
        }
    }
}
