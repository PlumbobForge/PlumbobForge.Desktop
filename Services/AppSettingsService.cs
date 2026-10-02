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
        string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
        Directory.CreateDirectory(text);
        return Path.Combine(text, "appsettings.json");
    }

    public static async Task SaveOptionsAsync(PlumbobForgeOptions options)
    {
        try
        {
            string filePath = GetAppSettingsPath();
            JsonObject jsonObject;
            if (File.Exists(filePath))
            {
                try
                {
                    jsonObject = (JsonNode.Parse(await File.ReadAllTextAsync(filePath), null) as JsonObject) ?? new JsonObject((JsonNodeOptions?)null);
                }
                catch
                {
                    jsonObject = new JsonObject((JsonNodeOptions?)null);
                }
            }
            else
            {
                jsonObject = new JsonObject((JsonNodeOptions?)null);
            }
            JsonArray jsonArray = new JsonArray((JsonNodeOptions?)null);
            if (options.ObservedFolders != null)
            {
                foreach (string observedFolder in options.ObservedFolders)
                {
                    if (!string.IsNullOrWhiteSpace(observedFolder))
                    {
                        jsonArray.Add(observedFolder);
                    }
                }
            }
            JsonObject value = new JsonObject((JsonNodeOptions?)null)
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
                ["ObservedFolders"] = jsonArray
            };
            jsonObject["PlumbobForge"] = value;
            JsonSerializerOptions options2 = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            await File.WriteAllTextAsync(filePath, jsonObject.ToJsonString(options2));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[AppSettingsService] Failed to save appsettings.json: " + ex.Message);
        }
    }
}
