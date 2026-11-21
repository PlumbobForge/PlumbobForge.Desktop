using System;
using System.IO;
using System.Text.Json;

namespace PlumbobForge.Desktop.Services;

// Where the main window was and how big, so the app opens the way it was left.
// Size is in DIPs (Avalonia's Width/Height), the position in screen pixels (Avalonia's Position).
public class WindowPlacement
{
    public double Width { get; set; }
    public double Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsMaximized { get; set; }
}

// Kept in its own file so the content manager's ui-state rewrites cannot drop it.
public static class WindowPlacementStore
{
    private static readonly string FilePath =
        Path.Combine(PlumbobForge.Backend.Configuration.AppPaths.AppDataDir, "window-placement.json");

    public static WindowPlacement? Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(FilePath));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WindowPlacementStore] Error loading window placement: {ex.Message}");
        }
        return null;
    }

    public static void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(placement, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WindowPlacementStore] Error saving window placement: {ex.Message}");
        }
    }
}
