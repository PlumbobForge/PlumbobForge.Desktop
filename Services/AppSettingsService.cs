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
        string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string text = Path.Combine(folderPath, "plumbobforge-app");
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
                string json = await File.ReadAllTextAsync(filePath);
                try
                {
                    jsonObject = JsonNode.Parse(json)?.AsObject() ?? new JsonObject((JsonNodeOptions?)null);
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

            JsonObject plumbobNode;
            if (jsonObject.TryGetPropertyValue("PlumbobForge", out var existingPfNode) && existingPfNode is JsonObject existingPfObj)
            {
                plumbobNode = existingPfObj;
            }
            else
            {
                plumbobNode = new JsonObject((JsonNodeOptions?)null);
                jsonObject["PlumbobForge"] = plumbobNode;
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

            plumbobNode["Language"] = options.Language ?? "auto";
            plumbobNode["DocumentBaseDir"] = options.DocumentBaseDir ?? "";
            plumbobNode["GameFilesDir"] = options.GameFilesDir ?? "";
            plumbobNode["DownloadFolderName"] = string.IsNullOrWhiteSpace(options.DownloadFolderName) ? "Downloads" : options.DownloadFolderName;
            plumbobNode["ArchiveFolderName"] = string.IsNullOrWhiteSpace(options.ArchiveFolderName) ? "Archive" : options.ArchiveFolderName;
            plumbobNode["TS3PackFolderName"] = string.IsNullOrWhiteSpace(options.TS3PackFolderName) ? "Downloads" : options.TS3PackFolderName;
            plumbobNode["ManagedPackageFolderName"] = string.IsNullOrWhiteSpace(options.ManagedPackageFolderName) ? "Library" : options.ManagedPackageFolderName;
            plumbobNode["SetCacheFolderName"] = string.IsNullOrWhiteSpace(options.SetCacheFolderName) ? "Builds" : options.SetCacheFolderName;
            plumbobNode["LegacyPackageFolderName"] = string.IsNullOrWhiteSpace(options.LegacyPackageFolderName) ? "Legacy" : options.LegacyPackageFolderName;
            plumbobNode["TS3PackStoreFolderName"] = string.IsNullOrWhiteSpace(options.TS3PackStoreFolderName) ? "Store" : options.TS3PackStoreFolderName;
            plumbobNode["CompressionLevel"] = options.CompressionLevel;
            plumbobNode["CacheMethod"] = options.CacheMethod ?? "Dynamic";
            plumbobNode["AutoRebuildStaticCache"] = options.AutoRebuildStaticCache;
            plumbobNode["EnableAutoScan"] = options.EnableAutoScan;
            plumbobNode["Theme"] = options.Theme ?? "Dark";
            plumbobNode["AccentColor"] = options.AccentColor ?? "Emerald";
            plumbobNode["ObservedFolders"] = jsonArray;
            plumbobNode["HasSeenWalkthrough"] = options.HasSeenWalkthrough;
            plumbobNode["HasCompletedUpgradeWizard"] = options.HasCompletedUpgradeWizard;

            if (options.LastOptimizedCacheMilestone > 0 || !plumbobNode.ContainsKey("LastOptimizedCacheMilestone"))
            {
                plumbobNode["LastOptimizedCacheMilestone"] = options.LastOptimizedCacheMilestone;
            }
            if (!string.IsNullOrEmpty(options.LastOptimizedCacheVersion) || !plumbobNode.ContainsKey("LastOptimizedCacheVersion"))
            {
                plumbobNode["LastOptimizedCacheVersion"] = options.LastOptimizedCacheVersion ?? "";
            }

            var optionsJson = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            await File.WriteAllTextAsync(filePath, jsonObject.ToJsonString(optionsJson));
        }
        catch (Exception)
        {
        }
    }
}
