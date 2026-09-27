using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PlumbobForge.Backend.Configuration;

namespace PlumbobForge.Desktop.Services;

public static class AppSettingsService
{
    public static string GetAppSettingsPath()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, "appsettings.json");
    }

    public static async Task SaveOptionsAsync(PlumbobForgeOptions options)
    {
        try
        {
            var filePath = GetAppSettingsPath();
            JsonObject jObject;
            if (File.Exists(filePath))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(filePath);
                    jObject = (JsonNode.Parse(text) as JsonObject) ?? new JsonObject();
                }
                catch
                {
                    jObject = new JsonObject();
                }
            }
            else
            {
                jObject = new JsonObject();
            }

            var jsonObserved = new JsonArray();
            if (options.ObservedFolders != null)
            {
                foreach (var folder in options.ObservedFolders)
                {
                    if (!string.IsNullOrWhiteSpace(folder)) jsonObserved.Add(folder);
                }
            }

            var ccNode = new JsonObject
            {
                ["DocumentBaseDir"] = options.DocumentBaseDir,
                ["DownloadFolderName"] = options.DownloadFolderName ?? "Downloads",
                ["ArchiveFolderName"] = options.ArchiveFolderName ?? "",
                ["TS3PackFolderName"] = options.TS3PackFolderName ?? "",
                ["ManagedPackageFolderName"] = "Library",
                ["SetCacheFolderName"] = "Builds",
                ["LegacyPackageFolderName"] = options.LegacyPackageFolderName ?? "",
                ["TS3PackStoreFolderName"] = options.TS3PackStoreFolderName ?? "",
                ["GameFilesDir"] = options.GameFilesDir ?? "",
                ["CompressionLevel"] = options.CompressionLevel,
                ["HasSeenWalkthrough"] = options.HasSeenWalkthrough,
                ["HasCompletedUpgradeWizard"] = options.HasCompletedUpgradeWizard,
                ["Language"] = options.Language ?? "auto",
                ["Theme"] = options.Theme ?? "Dark",
                ["AccentColor"] = options.AccentColor ?? "Emerald",
                ["CacheMethod"] = options.CacheMethod ?? "Dynamic",
                ["EnableAutoScan"] = options.EnableAutoScan,
                ["LastActiveTool"] = options.LastActiveTool ?? "Cache",
                ["ObservedFolders"] = jsonObserved
            };

            jObject["PlumbobForge"] = ccNode;

            var jsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(filePath, jObject.ToJsonString(jsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppSettingsService] Failed to save appsettings.json: {ex.Message}");
        }
    }
}
