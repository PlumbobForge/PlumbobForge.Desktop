using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlumbobForge.Desktop.Services;

public class ContentManagerUiState
{
    public bool IsCompactListView { get; set; } = false;
    public int GridZoomLevel { get; set; } = 3;
    public long? SelectedSetId { get; set; } = null;
    public bool IsSetsSidebarCollapsed { get; set; } = false;
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
}

public class UiStateService
{
    private readonly string _stateFilePath;
    private CancellationTokenSource? _saveCts;
    private readonly object _lock = new();

    public UiStateService()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "plumbobforge-app");
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
