using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlumbobForge.Desktop.Services;

public class SmartSetState
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // Filter-panel values that differ from an untouched panel, by property name.
    public Dictionary<string, string> Filters { get; set; } = new();
}

public class ContentManagerUiState
{
    public bool IsCompactListView { get; set; } = false;
    public int GridZoomLevel { get; set; } = 3;
    public long? SelectedSetId { get; set; } = null;
    public bool IsSetsSidebarCollapsed { get; set; } = false;
    // The sidebar's Sets and Mods Folder sections, when both are shown.
    public bool IsSetsSectionCollapsed { get; set; } = false;
    public bool IsModsSectionCollapsed { get; set; } = false;
    public double SetsSidebarWidth { get; set; } = 260;
    // Sets items were last moved to, newest first (the top of Move to Set).
    public List<long> RecentMoveSetIds { get; set; } = new();
    // Smart sets (saved filters), the one that was open, and the filters from before it was opened.
    public List<SmartSetState> SmartSets { get; set; } = new();
    public string? SelectedSmartSetId { get; set; }
    public Dictionary<string, string>? FiltersBeforeSmartSet { get; set; }
    public bool IsSmartSectionCollapsed { get; set; } = false;
    public bool IsFiltersSidebarCollapsed { get; set; } = false;
    public string CurrentItemSort { get; set; } = "DateAdded";
    public bool SortFavoritesFirst { get; set; } = true;
    public string CurrentSetSort { get; set; } = "DateCreated";

    // Main Type Filters
    public bool FilterTypeCAS { get; set; } = true;
    public bool FilterTypeBuildBuy { get; set; } = true;
    public bool FilterTypeOther { get; set; } = true;

    // CAS Categories
    public bool FilterCasHair { get; set; } = true;
    public bool FilterCasFullBody { get; set; } = true;
    public bool FilterCasTops { get; set; } = true;
    public bool FilterCasBottoms { get; set; } = true;
    public bool FilterCasShoes { get; set; } = true;
    public bool FilterCasDetails { get; set; } = true;
    public bool FilterCasSkins { get; set; } = true;
    public bool FilterCasAccessories { get; set; } = true;
    public bool FilterCasSliders { get; set; } = true;
    public bool FilterCasPresets { get; set; } = true;
    public bool FilterCasOther { get; set; } = true;

    // Ages
    public bool FilterAgeBaby { get; set; } = true;
    public bool FilterAgeToddler { get; set; } = true;
    public bool FilterAgeChild { get; set; } = true;
    public bool FilterAgeTeen { get; set; } = true;
    public bool FilterAgeYoungAdult { get; set; } = true;
    public bool FilterAgeAdult { get; set; } = true;
    public bool FilterAgeElder { get; set; } = true;

    // Gender
    public bool FilterGenderMale { get; set; } = true;
    public bool FilterGenderFemale { get; set; } = true;

    // Outfit Categories
    public bool FilterOutfitEveryday { get; set; } = true;
    public bool FilterOutfitFormal { get; set; } = true;
    public bool FilterOutfitSleepwear { get; set; } = true;
    public bool FilterOutfitSwimwear { get; set; } = true;
    public bool FilterOutfitAthletic { get; set; } = true;
    public bool FilterOutfitCareer { get; set; } = true;
    public bool FilterOutfitOuterwear { get; set; } = true;

    // Build/Buy Kinds
    public bool FilterBBObjects { get; set; } = true;
    public bool FilterBBDoorsWindows { get; set; } = true;
    public bool FilterBBLandscaping { get; set; } = true;
    public bool FilterBBWallsFloors { get; set; } = true;
    public bool FilterBBTerrain { get; set; } = true;
    public bool FilterBBFencesRailings { get; set; } = true;
    public bool FilterBBStairs { get; set; } = true;
    public bool FilterBBRoofs { get; set; } = true;
    public bool FilterBBOtherBuild { get; set; } = true;

    // Build/Buy Rooms
    public bool FilterBBRoomLiving { get; set; } = true;
    public bool FilterBBRoomDining { get; set; } = true;
    public bool FilterBBRoomKitchen { get; set; } = true;
    public bool FilterBBRoomNursery { get; set; } = true;
    public bool FilterBBRoomBathroom { get; set; } = true;
    public bool FilterBBRoomLaundry { get; set; } = true;
    public bool FilterBBRoomBedroom { get; set; } = true;
    public bool FilterBBRoomStudy { get; set; } = true;
    public bool FilterBBRoomOutdoor { get; set; } = true;
    public bool FilterBBRoomCommunity { get; set; } = true;
    public bool FilterBBRoomResort { get; set; } = true;

    // Build/Buy Functions
    public bool FilterBBFuncAppliances { get; set; } = true;
    public bool FilterBBFuncElectronics { get; set; } = true;
    public bool FilterBBFuncEntertainment { get; set; } = true;
    public bool FilterBBFuncLighting { get; set; } = true;
    public bool FilterBBFuncPlumbing { get; set; } = true;
    public bool FilterBBFuncDecor { get; set; } = true;
    public bool FilterBBFuncKids { get; set; } = true;
    public bool FilterBBFuncStorage { get; set; } = true;
    public bool FilterBBFuncComfort { get; set; } = true;
    public bool FilterBBFuncSurfaces { get; set; } = true;
    public bool FilterBBFuncVehicles { get; set; } = true;
    public bool FilterBBFuncPets { get; set; } = true;
    public bool FilterBBFuncMisc { get; set; } = true;

    // Build/Buy Price
    public decimal? FilterBBPriceMin { get; set; }
    public decimal? FilterBBPriceMax { get; set; }

    // Other Sub-Categories
    public bool FilterOtherWorlds { get; set; } = true;
    public bool FilterOtherSims { get; set; } = true;
    public bool FilterOtherLots { get; set; } = true;
    public bool FilterOtherMisc { get; set; } = true;

    // Mode
    public bool FilterModeEnabled { get; set; } = true;
    public bool FilterModeDisabled { get; set; } = true;
    public bool FilterModeOptimizedOnly { get; set; }

    // Script mods
    public bool FilterScriptMods { get; set; } = true;
    public bool FilterCoreMods { get; set; } = true;
    public bool FilterNoScript { get; set; } = true;

    // Accordion Expansion States
    public bool IsTypeExpanded { get; set; } = true;
    public bool IsCasSubFiltersExpanded { get; set; } = true;
    public bool IsCasCategoryExpanded { get; set; } = true;
    public bool IsCasAgeExpanded { get; set; } = true;
    public bool IsCasGenderExpanded { get; set; } = true;
    public bool IsCasOutfitExpanded { get; set; } = true;
    public bool IsBBSubFiltersExpanded { get; set; } = true;
    public bool IsBBKindExpanded { get; set; } = true;
    public bool IsBBRoomExpanded { get; set; } = true;
    public bool IsBBFunctionExpanded { get; set; } = true;
    public bool IsBBPriceExpanded { get; set; } = true;
    public bool IsOtherSubExpanded { get; set; } = true;
    public bool IsModeExpanded { get; set; } = true;
    public bool IsScriptExpanded { get; set; } = true;
}

// The Configurations tab's own UI state, kept apart so the library's state file never overwrites it.
public class ConfigurationsUiState
{
    public string SourceFilter { get; set; } = "All";

    private static string FilePath => Path.Combine(PlumbobForge.Backend.Configuration.AppPaths.AppDataDir, "configurations-ui-state.json");

    public static ConfigurationsUiState Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<ConfigurationsUiState>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigurationsUiState] Error loading state: {ex.Message}");
        }
        return new ConfigurationsUiState();
    }

    public static void Save(ConfigurationsUiState state)
    {
        try
        {
            Directory.CreateDirectory(PlumbobForge.Backend.Configuration.AppPaths.AppDataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigurationsUiState] Error saving state: {ex.Message}");
        }
    }
}

public class UiStateService
{
    private readonly string _stateFilePath;
    private CancellationTokenSource? _saveCts;
    private readonly object _lock = new();

    public UiStateService()
    {
        var appDataPath = PlumbobForge.Backend.Configuration.AppPaths.AppDataDir;
        Directory.CreateDirectory(appDataPath);
        _stateFilePath = Path.Combine(appDataPath, "ui-state.json");
    }

    public ContentManagerUiState LoadState()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<ContentManagerUiState>(json);
                if (state != null) return state;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UiStateService] Error loading state: {ex.Message}");
        }

        return new ContentManagerUiState();
    }

    public void SaveState(ContentManagerUiState state, bool immediate = false)
    {
        lock (_lock)
        {
            _saveCts?.Cancel();
            _saveCts?.Dispose();
            _saveCts = new CancellationTokenSource();
            var token = _saveCts.Token;

            if (immediate)
            {
                WriteStateToDisk(state);
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, token);
                    if (!token.IsCancellationRequested)
                    {
                        WriteStateToDisk(state);
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UiStateService] Error saving state: {ex.Message}");
                }
            }, token);
        }
    }

    public void SaveStateImmediate(ContentManagerUiState state)
    {
        lock (_lock)
        {
            _saveCts?.Cancel();
            WriteStateToDisk(state);
        }
    }

    private void WriteStateToDisk(ContentManagerUiState state)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(state, options);
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UiStateService] Error writing state file: {ex.Message}");
        }
    }
}
