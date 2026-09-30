using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlumbobForge.Desktop.ViewModels;

public partial class ContentManagerViewModel
{
    private static readonly char[] CasFilterDelimiters = new[] { ',', ';', ' ' };

    // Main Type Filters
    [ObservableProperty] private bool _filterTypeCAS = true;
    [ObservableProperty] private bool _filterTypeBuildBuy = true;
    [ObservableProperty] private bool _filterTypeOther = true;

    // CAS Categories
    [ObservableProperty] private bool _filterCasHair = true;
    [ObservableProperty] private bool _filterCasFullBody = true;
    [ObservableProperty] private bool _filterCasTops = true;
    [ObservableProperty] private bool _filterCasBottoms = true;
    [ObservableProperty] private bool _filterCasShoes = true;
    [ObservableProperty] private bool _filterCasDetails = true;
    [ObservableProperty] private bool _filterCasSkins = true;
    [ObservableProperty] private bool _filterCasAccessories = true;
    [ObservableProperty] private bool _filterCasSliders = true;
    [ObservableProperty] private bool _filterCasPresets = true;
    [ObservableProperty] private bool _filterCasOther = true;

    // Ages
    [ObservableProperty] private bool _filterAgeBaby = true;
    [ObservableProperty] private bool _filterAgeToddler = true;
    [ObservableProperty] private bool _filterAgeChild = true;
    [ObservableProperty] private bool _filterAgeTeen = true;
    [ObservableProperty] private bool _filterAgeYoungAdult = true;
    [ObservableProperty] private bool _filterAgeAdult = true;
    [ObservableProperty] private bool _filterAgeElder = true;

    // Gender
    [ObservableProperty] private bool _filterGenderMale = true;
    [ObservableProperty] private bool _filterGenderFemale = true;

    // Outfit Categories
    [ObservableProperty] private bool _filterOutfitEveryday = true;
    [ObservableProperty] private bool _filterOutfitFormal = true;
    [ObservableProperty] private bool _filterOutfitSleepwear = true;
    [ObservableProperty] private bool _filterOutfitSwimwear = true;
    [ObservableProperty] private bool _filterOutfitAthletic = true;
    [ObservableProperty] private bool _filterOutfitCareer = true;
    [ObservableProperty] private bool _filterOutfitOuterwear = true;

    // Other Sub-Categories
    [ObservableProperty] private bool _filterOtherWorlds = true;
    [ObservableProperty] private bool _filterOtherSims = true;
    [ObservableProperty] private bool _filterOtherLots = true;
    [ObservableProperty] private bool _filterOtherMisc = true;

    // Mode
    [ObservableProperty] private bool _filterModeEnabled = true;
    [ObservableProperty] private bool _filterModeDisabled = true;

    // Collapsible Accordion Section States
    [ObservableProperty] private bool _isTypeExpanded = true;
    [ObservableProperty] private bool _isCasSubFiltersExpanded = true;
    [ObservableProperty] private bool _isCasCategoryExpanded = true;
    [ObservableProperty] private bool _isCasAgeExpanded = true;
    [ObservableProperty] private bool _isCasGenderExpanded = true;
    [ObservableProperty] private bool _isCasOutfitExpanded = true;
    [ObservableProperty] private bool _isOtherSubExpanded = true;
    [ObservableProperty] private bool _isModeExpanded = true;

    [RelayCommand] public void ToggleTypeExpanded() { IsTypeExpanded = !IsTypeExpanded; SaveCurrentUiState(); }
    [RelayCommand] public void ToggleCasSubFiltersExpanded() { IsCasSubFiltersExpanded = !IsCasSubFiltersExpanded; SaveCurrentUiState(); }
    [RelayCommand] public void ToggleCasCategoryExpanded() { IsCasCategoryExpanded = !IsCasCategoryExpanded; SaveCurrentUiState(); }
    [RelayCommand] public void ToggleCasAgeExpanded() { IsCasAgeExpanded = !IsCasAgeExpanded; SaveCurrentUiState(); }
    [RelayCommand] public void ToggleCasGenderExpanded() { IsCasGenderExpanded = !IsCasGenderExpanded; SaveCurrentUiState(); }
    [RelayCommand] public void ToggleCasOutfitExpanded() { IsCasOutfitExpanded = !IsCasOutfitExpanded; SaveCurrentUiState(); }
    [RelayCommand] public void ToggleOtherSubExpanded() { IsOtherSubExpanded = !IsOtherSubExpanded; SaveCurrentUiState(); }
    [RelayCommand] public void ToggleModeExpanded() { IsModeExpanded = !IsModeExpanded; SaveCurrentUiState(); }

    [RelayCommand]
    public void SelectAllFilters()
    {
        FilterTypeCAS = true;
        FilterTypeBuildBuy = true;
        FilterTypeOther = true;

        FilterCasHair = true;
        FilterCasFullBody = true;
        FilterCasTops = true;
        FilterCasBottoms = true;
        FilterCasShoes = true;
        FilterCasDetails = true;
        FilterCasSkins = true;
        FilterCasAccessories = true;
        FilterCasSliders = true;
        FilterCasPresets = true;
        FilterCasOther = true;

        FilterAgeBaby = true;
        FilterAgeToddler = true;
        FilterAgeChild = true;
        FilterAgeTeen = true;
        FilterAgeYoungAdult = true;
        FilterAgeAdult = true;
        FilterAgeElder = true;

        FilterGenderMale = true;
        FilterGenderFemale = true;

        FilterOutfitEveryday = true;
        FilterOutfitFormal = true;
        FilterOutfitSleepwear = true;
        FilterOutfitSwimwear = true;
        FilterOutfitAthletic = true;
        FilterOutfitCareer = true;
        FilterOutfitOuterwear = true;

        FilterOtherWorlds = true;
        FilterOtherSims = true;
        FilterOtherLots = true;
        FilterOtherMisc = true;

        FilterModeEnabled = true;
        FilterModeDisabled = true;

        RequestFilterUpdate(0);
    }

    [RelayCommand]
    public void ClearAllFilters()
    {
        FilterTypeCAS = false;
        FilterTypeBuildBuy = false;
        FilterTypeOther = false;

        FilterCasHair = false;
        FilterCasFullBody = false;
        FilterCasTops = false;
        FilterCasBottoms = false;
        FilterCasShoes = false;
        FilterCasDetails = false;
        FilterCasSkins = false;
        FilterCasAccessories = false;
        FilterCasSliders = false;
        FilterCasPresets = false;
        FilterCasOther = false;

        FilterAgeBaby = false;
        FilterAgeToddler = false;
        FilterAgeChild = false;
        FilterAgeTeen = false;
        FilterAgeYoungAdult = false;
        FilterAgeAdult = false;
        FilterAgeElder = false;

        FilterGenderMale = false;
        FilterGenderFemale = false;

        FilterOutfitEveryday = false;
        FilterOutfitFormal = false;
        FilterOutfitSleepwear = false;
        FilterOutfitSwimwear = false;
        FilterOutfitAthletic = false;
        FilterOutfitCareer = false;
        FilterOutfitOuterwear = false;

        FilterOtherWorlds = false;
        FilterOtherSims = false;
        FilterOtherLots = false;
        FilterOtherMisc = false;

        FilterModeEnabled = false;
        FilterModeDisabled = false;

        RequestFilterUpdate(0);
    }

    // Section All-Selected States & Commands
    public bool IsAllTypesSelected => FilterTypeCAS && FilterTypeBuildBuy && FilterTypeOther;
    public bool IsAllCasCategorySelected =>
        FilterCasHair && FilterCasFullBody && FilterCasTops && FilterCasBottoms &&
        FilterCasShoes && FilterCasDetails && FilterCasSkins && FilterCasAccessories &&
        FilterCasSliders && FilterCasPresets && FilterCasOther;
    public bool IsAllCasAgeSelected =>
        FilterAgeBaby && FilterAgeToddler && FilterAgeChild && FilterAgeTeen &&
        FilterAgeYoungAdult && FilterAgeAdult && FilterAgeElder;
    public bool IsAllCasGenderSelected => FilterGenderMale && FilterGenderFemale;
    public bool IsAllCasOutfitSelected =>
        FilterOutfitEveryday && FilterOutfitFormal && FilterOutfitSleepwear && FilterOutfitSwimwear &&
        FilterOutfitAthletic && FilterOutfitCareer && FilterOutfitOuterwear;
    public bool IsAllOtherCategorySelected =>
        FilterOtherWorlds && FilterOtherSims && FilterOtherLots && FilterOtherMisc;
    public bool IsAllModeSelected => FilterModeEnabled && FilterModeDisabled;

    [RelayCommand]
    public void ToggleAllTypes()
    {
        bool targetState = !IsAllTypesSelected;
        FilterTypeCAS = targetState;
        FilterTypeBuildBuy = targetState;
        FilterTypeOther = targetState;
        OnPropertyChanged(nameof(IsAllTypesSelected));
        RequestFilterUpdate(0);
    }

    [RelayCommand]
    public void ToggleAllCasCategory()
    {
        bool targetState = !IsAllCasCategorySelected;
        FilterCasHair = targetState;
        FilterCasFullBody = targetState;
        FilterCasTops = targetState;
        FilterCasBottoms = targetState;
        FilterCasShoes = targetState;
        FilterCasDetails = targetState;
        FilterCasSkins = targetState;
        FilterCasAccessories = targetState;
        FilterCasSliders = targetState;
        FilterCasPresets = targetState;
        FilterCasOther = targetState;
        OnPropertyChanged(nameof(IsAllCasCategorySelected));
        RequestFilterUpdate(0);
    }

    [RelayCommand]
    public void ToggleAllCasAge()
    {
        bool targetState = !IsAllCasAgeSelected;
        FilterAgeBaby = targetState;
        FilterAgeToddler = targetState;
        FilterAgeChild = targetState;
        FilterAgeTeen = targetState;
        FilterAgeYoungAdult = targetState;
        FilterAgeAdult = targetState;
        FilterAgeElder = targetState;
        OnPropertyChanged(nameof(IsAllCasAgeSelected));
        RequestFilterUpdate(0);
    }

    [RelayCommand]
    public void ToggleAllCasGender()
    {
        bool targetState = !IsAllCasGenderSelected;
        FilterGenderMale = targetState;
        FilterGenderFemale = targetState;
        OnPropertyChanged(nameof(IsAllCasGenderSelected));
        RequestFilterUpdate(0);
    }

    [RelayCommand]
    public void ToggleAllCasOutfit()
    {
        bool targetState = !IsAllCasOutfitSelected;
        FilterOutfitEveryday = targetState;
        FilterOutfitFormal = targetState;
        FilterOutfitSleepwear = targetState;
        FilterOutfitSwimwear = targetState;
        FilterOutfitAthletic = targetState;
        FilterOutfitCareer = targetState;
        FilterOutfitOuterwear = targetState;
        OnPropertyChanged(nameof(IsAllCasOutfitSelected));
        RequestFilterUpdate(0);
    }

    [RelayCommand]
    public void ToggleAllOtherCategory()
    {
        bool targetState = !IsAllOtherCategorySelected;
        FilterOtherWorlds = targetState;
        FilterOtherSims = targetState;
        FilterOtherLots = targetState;
        FilterOtherMisc = targetState;
        OnPropertyChanged(nameof(IsAllOtherCategorySelected));
        RequestFilterUpdate(0);
    }

    [RelayCommand]
    public void ToggleAllMode()
    {
        bool targetState = !IsAllModeSelected;
        FilterModeEnabled = targetState;
        FilterModeDisabled = targetState;
        OnPropertyChanged(nameof(IsAllModeSelected));
        RequestFilterUpdate(0);
    }

    public bool ShowCasSubFilters => FilterTypeCAS && IsCasSubFiltersExpanded;
    public bool ShowOtherSubFilters => FilterTypeOther && IsOtherSubExpanded;

    partial void OnFilterTypeCASChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCasSubFilters));
        OnPropertyChanged(nameof(IsAllTypesSelected));
        RequestFilterUpdate();
    }

    partial void OnIsCasSubFiltersExpandedChanged(bool value) => OnPropertyChanged(nameof(ShowCasSubFilters));

    partial void OnFilterTypeBuildBuyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAllTypesSelected));
        RequestFilterUpdate();
    }

    partial void OnFilterTypeOtherChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOtherSubFilters));
        OnPropertyChanged(nameof(IsAllTypesSelected));
        RequestFilterUpdate();
    }

    partial void OnIsOtherSubExpandedChanged(bool value) => OnPropertyChanged(nameof(ShowOtherSubFilters));

    partial void OnFilterCasHairChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasFullBodyChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasTopsChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasBottomsChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasShoesChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasDetailsChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasSkinsChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasAccessoriesChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasSlidersChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasPresetsChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterCasOtherChanged(bool value) { OnPropertyChanged(nameof(IsAllCasCategorySelected)); RequestFilterUpdate(); }

    partial void OnFilterAgeBabyChanged(bool value) { OnPropertyChanged(nameof(IsAllCasAgeSelected)); RequestFilterUpdate(); }
    partial void OnFilterAgeToddlerChanged(bool value) { OnPropertyChanged(nameof(IsAllCasAgeSelected)); RequestFilterUpdate(); }
    partial void OnFilterAgeChildChanged(bool value) { OnPropertyChanged(nameof(IsAllCasAgeSelected)); RequestFilterUpdate(); }
    partial void OnFilterAgeTeenChanged(bool value) { OnPropertyChanged(nameof(IsAllCasAgeSelected)); RequestFilterUpdate(); }
    partial void OnFilterAgeYoungAdultChanged(bool value) { OnPropertyChanged(nameof(IsAllCasAgeSelected)); RequestFilterUpdate(); }
    partial void OnFilterAgeAdultChanged(bool value) { OnPropertyChanged(nameof(IsAllCasAgeSelected)); RequestFilterUpdate(); }
    partial void OnFilterAgeElderChanged(bool value) { OnPropertyChanged(nameof(IsAllCasAgeSelected)); RequestFilterUpdate(); }

    partial void OnFilterGenderMaleChanged(bool value) { OnPropertyChanged(nameof(IsAllCasGenderSelected)); RequestFilterUpdate(); }
    partial void OnFilterGenderFemaleChanged(bool value) { OnPropertyChanged(nameof(IsAllCasGenderSelected)); RequestFilterUpdate(); }

    partial void OnFilterOutfitEverydayChanged(bool value) { OnPropertyChanged(nameof(IsAllCasOutfitSelected)); RequestFilterUpdate(); }
    partial void OnFilterOutfitFormalChanged(bool value) { OnPropertyChanged(nameof(IsAllCasOutfitSelected)); RequestFilterUpdate(); }
    partial void OnFilterOutfitSleepwearChanged(bool value) { OnPropertyChanged(nameof(IsAllCasOutfitSelected)); RequestFilterUpdate(); }
    partial void OnFilterOutfitSwimwearChanged(bool value) { OnPropertyChanged(nameof(IsAllCasOutfitSelected)); RequestFilterUpdate(); }
    partial void OnFilterOutfitAthleticChanged(bool value) { OnPropertyChanged(nameof(IsAllCasOutfitSelected)); RequestFilterUpdate(); }
    partial void OnFilterOutfitCareerChanged(bool value) { OnPropertyChanged(nameof(IsAllCasOutfitSelected)); RequestFilterUpdate(); }
    partial void OnFilterOutfitOuterwearChanged(bool value) { OnPropertyChanged(nameof(IsAllCasOutfitSelected)); RequestFilterUpdate(); }

    partial void OnFilterOtherWorldsChanged(bool value) { OnPropertyChanged(nameof(IsAllOtherCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterOtherSimsChanged(bool value) { OnPropertyChanged(nameof(IsAllOtherCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterOtherLotsChanged(bool value) { OnPropertyChanged(nameof(IsAllOtherCategorySelected)); RequestFilterUpdate(); }
    partial void OnFilterOtherMiscChanged(bool value) { OnPropertyChanged(nameof(IsAllOtherCategorySelected)); RequestFilterUpdate(); }

    partial void OnFilterModeEnabledChanged(bool value) { OnPropertyChanged(nameof(IsAllModeSelected)); RequestFilterUpdate(); }
    partial void OnFilterModeDisabledChanged(bool value) { OnPropertyChanged(nameof(IsAllModeSelected)); RequestFilterUpdate(); }

    partial void OnSelectedSetNodeChanged(SetNodeViewModel? value)
    {
        _savedSetId = value?.Id;
        OnPropertyChanged(nameof(CanShowSortFavoritesFirst));
        SaveCurrentUiState();
        RequestFilterUpdate(0);
    }

    private Avalonia.Threading.DispatcherTimer? _filterDebounceTimer;

    private void RequestFilterUpdate(int debounceMs = 30)
    {
        SaveCurrentUiState();

        if (debounceMs <= 0)
        {
            _filterDebounceTimer?.Stop();
            ApplyFilter();
            return;
        }

        if (_filterDebounceTimer == null)
        {
            _filterDebounceTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(debounceMs)
            };
            _filterDebounceTimer.Tick += (s, e) =>
            {
                _filterDebounceTimer.Stop();
                ApplyFilter();
            };
        }

        _filterDebounceTimer.Interval = TimeSpan.FromMilliseconds(debounceMs);
        _filterDebounceTimer.Stop();
        _filterDebounceTimer.Start();
    }

    partial void OnSearchQueryChanged(string value)
    {
        RequestFilterUpdate(200);
    }

    private List<ItemViewModel> _allMatchingItems = new();
    private const int BatchSize = 24;

    public void ApplyFilter()
    {
        var query = Items.AsEnumerable();

        if (SelectedSetNode != null && SelectedSetNode.IsFavorites)
        {
            query = query.Where(i => i.IsFavorite);
        }
        else
        {
            var selectedNodes = GetSelectedSetNodes();
            if (selectedNodes.Count > 0)
            {
                var targetSetIds = selectedNodes.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();
                query = query.Where(i => i.Entity.SetsEntityId.HasValue && targetSetIds.Contains(i.Entity.SetsEntityId.Value));
            }
            else if (SelectedSetNode != null && SelectedSetNode.Id.HasValue)
            {
                long targetSetId = SelectedSetNode.Id.Value;
                query = query.Where(i => i.Entity.SetsEntityId.HasValue && i.Entity.SetsEntityId.Value == targetSetId);
            }
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim().ToLowerInvariant();
            query = query.Where(i => i.FileName.ToLowerInvariant().Contains(q) ||
                                     (i.UserTags != null && i.UserTags.ToLowerInvariant().Contains(q)));
        }

        // Mode filter
        query = query.Where(i => (i.Enabled && FilterModeEnabled) || (!i.Enabled && FilterModeDisabled));

        // Type filter
        query = query.Where(i =>
        {
            var type = i.PackageType ?? "";
            var isCas = type.Equals("CAS", StringComparison.OrdinalIgnoreCase);
            var isBuildBuy = type.Equals("BuildBuy", StringComparison.OrdinalIgnoreCase) ||
                             type.Equals("Build/Buy", StringComparison.OrdinalIgnoreCase) ||
                             type.Equals("Object", StringComparison.OrdinalIgnoreCase);
            var isOther = !isCas && !isBuildBuy;

            if (isCas)
            {
                if (!FilterTypeCAS) return false;
                if (!MatchCasCategory(i)) return false;
                if (!MatchCasAge(i)) return false;
                if (!MatchCasGender(i)) return false;
                if (!MatchCasOutfit(i)) return false;
                return true;
            }

            if (isBuildBuy)
            {
                return FilterTypeBuildBuy;
            }

            if (isOther)
            {
                if (!FilterTypeOther) return false;
                return MatchOtherSubCategory(i);
            }

            return false;
        });

        query = (CurrentItemSort, SortFavoritesFirst) switch
        {
            ("NameAsc", true) => query.OrderByDescending(i => i.IsFavorite).ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase),
            ("NameAsc", false) => query.OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase),
            ("NameDesc", true) => query.OrderByDescending(i => i.IsFavorite).ThenByDescending(i => i.FileName, StringComparer.OrdinalIgnoreCase),
            ("NameDesc", false) => query.OrderByDescending(i => i.FileName, StringComparer.OrdinalIgnoreCase),
            (_, true) => query.OrderByDescending(i => i.IsFavorite).ThenByDescending(i => i.Id),
            (_, false) => query.OrderByDescending(i => i.Id)
        };

        var newMatchingList = query.ToList();
        var newMatchingSet = new HashSet<ItemViewModel>(newMatchingList);

        // Smart thumbnail cache preservation: only unload thumbnails for items NO LONGER matching
        foreach (var oldItem in FilteredItems)
        {
            if (!newMatchingSet.Contains(oldItem))
            {
                oldItem.UnloadThumbnail();
            }
        }

        _allMatchingItems = newMatchingList;
        FilteredItems.Clear();

        foreach (var item in _allMatchingItems)
        {
            FilteredItems.Add(item);
        }

        RebuildRows();
    }

    private bool MatchCasCategory(ItemViewModel item)
    {
        if (FilterCasHair && FilterCasFullBody && FilterCasTops && FilterCasBottoms &&
            FilterCasShoes && FilterCasDetails && FilterCasSkins && FilterCasAccessories &&
            FilterCasSliders && FilterCasPresets && FilterCasOther)
            return true;

        var cats = item.Entity.CASCategories ?? "Other";
        if (FilterCasHair && cats.Contains("Hair", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterCasFullBody && (cats.Contains("Full body", StringComparison.OrdinalIgnoreCase) || cats.Contains("FullBody", StringComparison.OrdinalIgnoreCase))) return true;
        if (FilterCasTops && cats.Contains("Tops", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterCasBottoms && cats.Contains("Bottoms", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterCasShoes && cats.Contains("Shoes", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterCasDetails && cats.Contains("Details", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterCasSkins && cats.Contains("Skins", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterCasAccessories && cats.Contains("Accessories", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterCasSliders && cats.Contains("Sliders", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterCasPresets && cats.Contains("Presets", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterCasOther && cats.Contains("Other", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private bool MatchCasAge(ItemViewModel item)
    {
        if (FilterAgeBaby && FilterAgeToddler && FilterAgeChild && FilterAgeTeen &&
            FilterAgeYoungAdult && FilterAgeAdult && FilterAgeElder)
            return true;

        var ages = item.Entity.CASAge ?? "";
        if (string.IsNullOrEmpty(ages)) return true;

        var parts = ages.Split(CasFilterDelimiters, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (FilterAgeBaby && parts.Any(p => string.Equals(p, "Baby", StringComparison.OrdinalIgnoreCase))) return true;
        if (FilterAgeToddler && parts.Any(p => string.Equals(p, "Toddler", StringComparison.OrdinalIgnoreCase))) return true;
        if (FilterAgeChild && parts.Any(p => string.Equals(p, "Child", StringComparison.OrdinalIgnoreCase))) return true;
        if (FilterAgeTeen && parts.Any(p => string.Equals(p, "Teen", StringComparison.OrdinalIgnoreCase))) return true;
        if (FilterAgeYoungAdult && parts.Any(p => string.Equals(p, "YoungAdult", StringComparison.OrdinalIgnoreCase))) return true;
        if (FilterAgeAdult && parts.Any(p => string.Equals(p, "Adult", StringComparison.OrdinalIgnoreCase))) return true;
        if (FilterAgeElder && parts.Any(p => string.Equals(p, "Elder", StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    private bool MatchCasGender(ItemViewModel item)
    {
        if (FilterGenderMale && FilterGenderFemale) return true;

        var gender = item.Entity.CASGender ?? "";
        if (string.IsNullOrEmpty(gender)) return true;

        var parts = gender.Split(CasFilterDelimiters, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (FilterGenderMale && parts.Any(p => string.Equals(p, "Male", StringComparison.OrdinalIgnoreCase))) return true;
        if (FilterGenderFemale && parts.Any(p => string.Equals(p, "Female", StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    private bool MatchCasOutfit(ItemViewModel item)
    {
        if (FilterOutfitEveryday && FilterOutfitFormal && FilterOutfitSleepwear && FilterOutfitSwimwear &&
            FilterOutfitAthletic && FilterOutfitCareer && FilterOutfitOuterwear)
            return true;

        var outfits = item.Entity.CASOutfitCategory ?? "";
        if (string.IsNullOrEmpty(outfits)) return true;

        if (FilterOutfitEveryday && outfits.Contains("Everyday", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterOutfitFormal && outfits.Contains("Formal", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterOutfitSleepwear && outfits.Contains("Sleepwear", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterOutfitSwimwear && outfits.Contains("Swimwear", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterOutfitAthletic && outfits.Contains("Athletic", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterOutfitCareer && outfits.Contains("Career", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterOutfitOuterwear && outfits.Contains("Outerwear", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private bool MatchOtherSubCategory(ItemViewModel item)
    {
        if (FilterOtherWorlds && FilterOtherSims && FilterOtherLots && FilterOtherMisc)
            return true;

        var type = item.PackageType ?? "";
        var name = item.FileName.ToLowerInvariant();

        if (FilterOtherWorlds && (type.Equals("World", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".world"))) return true;
        if (FilterOtherSims && (type.Equals("Sim", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sim"))) return true;
        if (FilterOtherLots && type.Equals("Lot", StringComparison.OrdinalIgnoreCase)) return true;
        if (FilterOtherMisc && !type.Equals("World", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".world") &&
            !type.Equals("Sim", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".sim") && !type.Equals("Lot", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
