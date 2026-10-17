using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconPacks.Avalonia.BoxIcons;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlumbobForge.Backend.Database;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class QuickSwitcherViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllSelected))]
    [NotifyPropertyChangedFor(nameof(IsConfigsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSetsSelected))]
    [NotifyPropertyChangedFor(nameof(IsItemsSelected))]
    [NotifyPropertyChangedFor(nameof(IsPlacesSelected))]
    private string _selectedCategory = "All"; // "All", "Sets", "Configurations", "Items", "Places"

    public bool IsAllSelected => SelectedCategory == "All";
    public bool IsConfigsSelected => SelectedCategory == "Configurations";
    public bool IsSetsSelected => SelectedCategory == "Sets";
    public bool IsItemsSelected => SelectedCategory == "Items";
    public bool IsPlacesSelected => SelectedCategory == "Places";

    [ObservableProperty]
    private ObservableCollection<QuickSwitcherItemViewModel> _results = new();

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _emptyStateMessage = "Type to search...";

    public QuickSwitcherViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task OpenAsync()
    {
        IsOpen = true;
        SearchQuery = string.Empty;
        SelectedCategory = "All";
        await ExecuteSearchAsync(string.Empty);
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        Results.Clear();
        SelectedIndex = -1;
    }

    partial void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _ = Task.Run(async () =>
        {
            await Task.Delay(80, token); // Debounce
            if (!token.IsCancellationRequested)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ExecuteSearchAsync(value, token);
                });
            }
        }, token);
    }

    [RelayCommand]
    public async Task SetCategoryAsync(string category)
    {
        SelectedCategory = category;
        await ExecuteSearchAsync(SearchQuery);
    }

    public async Task ExecuteSearchAsync(string query, CancellationToken ct = default)
    {
        IsLoading = true;
        var trimmed = query?.Trim() ?? string.Empty;
        var list = new List<QuickSwitcherItemViewModel>();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            bool includePlaces = SelectedCategory == "All" || SelectedCategory == "Places";
            bool includeConfigs = SelectedCategory == "All" || SelectedCategory == "Configurations";
            bool includeSets = SelectedCategory == "All" || SelectedCategory == "Sets";
            bool includeItems = SelectedCategory == "All" || SelectedCategory == "Items";

            // 0. PLACES
            if (includePlaces)
            {
                var places = GetPlaces();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    places = places.Where(p =>
                        p.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        (p.Subtitle != null && p.Subtitle.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                        (p.Keywords != null && p.Keywords.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                    ).ToList();
                }
                list.AddRange(places);
            }

            // 1. CONFIGURATIONS
            if (includeConfigs)
            {
                var configQuery = db.ConfigEntities.AsNoTracking().AsQueryable();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    configQuery = configQuery.Where(c => EF.Functions.Like(c.Name, $"%{trimmed}%") || (c.Description != null && EF.Functions.Like(c.Description, $"%{trimmed}%")));
                }
                var configs = await configQuery.Take(10).ToListAsync(ct);
                foreach (var c in configs)
                {
                    var iconKind = Enum.TryParse<PackIconBoxIconsKind>(c.Icon, out var k) ? k : PackIconBoxIconsKind.RegularSlider;
                    var brush = !string.IsNullOrEmpty(c.Color)
                        ? Brush.Parse(c.Color)
                        : (c.Active ? ThemeService.GetCurrentAccentBrush() : Brush.Parse("#60a5fa"));

                    list.Add(new QuickSwitcherItemViewModel
                    {
                        Type = QuickSwitcherItemType.Configuration,
                        TargetId = c.Id,
                        Title = c.Name,
                        Subtitle = !string.IsNullOrWhiteSpace(c.Description) ? c.Description : (c.Default ? "Default configuration profile" : "Configuration profile"),
                        DetailBadge = c.Active ? "ACTIVE" : (c.Default ? "DEFAULT" : null),
                        IconKind = iconKind,
                        IconBrush = brush
                    });
                }
            }

            // 2. SETS
            if (includeSets)
            {
                var setsList = await db.SetsEntities.AsNoTracking().ToListAsync(ct);
                var setMap = setsList.ToDictionary(s => s.Id);

                var matchedSets = string.IsNullOrEmpty(trimmed)
                    ? setsList.Take(15).ToList()
                    : setsList.Where(s => s.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();

                foreach (var s in matchedSets)
                {
                    // Build path / breadcrumb
                    var pathParts = new List<string> { s.Name };
                    var curr = s;
                    while (curr.ParentSetsEntityId.HasValue && setMap.TryGetValue(curr.ParentSetsEntityId.Value, out var parent))
                    {
                        pathParts.Insert(0, parent.Name);
                        curr = parent;
                    }

                    var iconKind = Enum.TryParse<PackIconBoxIconsKind>(s.Icon, out var k) ? k : PackIconBoxIconsKind.RegularFolder;
                    var brush = !string.IsNullOrEmpty(s.Color)
                        ? Brush.Parse(s.Color)
                        : ThemeService.GetCurrentAccentBrush();

                    list.Add(new QuickSwitcherItemViewModel
                    {
                        Type = QuickSwitcherItemType.Set,
                        TargetId = s.Id,
                        Title = s.Name,
                        Subtitle = pathParts.Count > 1 ? string.Join("  /  ", pathParts) : "Top-level set folder",
                        IconKind = iconKind,
                        IconBrush = brush
                    });
                }
            }

            // 3. ITEMS
            if (includeItems && !string.IsNullOrEmpty(trimmed))
            {
                var items = await db.MetaEntities
                    .AsNoTracking()
                    .Include(i => i.SetsEntity)
                    .Where(i => EF.Functions.Like(i.FileName, $"%{trimmed}%")
                             || (i.Description != null && EF.Functions.Like(i.Description, $"%{trimmed}%"))
                             || (i.UserTags != null && EF.Functions.Like(i.UserTags, $"%{trimmed}%")))
                    .Take(25)
                    .ToListAsync(ct);

                foreach (var item in items)
                {
                    var setName = item.SetsEntity?.Name ?? "Unassigned";
                    var subtitle = !string.IsNullOrWhiteSpace(item.UserTags)
                        ? $"In set: {setName}  •  Tags: {item.UserTags}"
                        : $"In set: {setName}";

                    list.Add(new QuickSwitcherItemViewModel
                    {
                        Type = QuickSwitcherItemType.Item,
                        TargetId = item.Id,
                        SecondaryId = item.SetsEntityId,
                        Title = item.FileName,
                        Subtitle = subtitle,
                        IconKind = PackIconBoxIconsKind.RegularPackage,
                        IconBrush = Brush.Parse("#c084fc")
                    });
                }
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            EmptyStateMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        Results = new ObservableCollection<QuickSwitcherItemViewModel>(list);
        if (Results.Count > 0)
        {
            SelectedIndex = 0;
            Results[0].IsSelected = true;
        }
        else
        {
            SelectedIndex = -1;
            EmptyStateMessage = string.IsNullOrEmpty(trimmed) ? "Type to search..." : $"No results matching \"{trimmed}\"";
        }
    }

    public void MoveSelectionUp()
    {
        if (Results.Count == 0) return;
        if (SelectedIndex > 0)
        {
            Results[SelectedIndex].IsSelected = false;
            SelectedIndex--;
            Results[SelectedIndex].IsSelected = true;
        }
    }

    public void MoveSelectionDown()
    {
        if (Results.Count == 0) return;
        if (SelectedIndex < Results.Count - 1)
        {
            Results[SelectedIndex].IsSelected = false;
            SelectedIndex++;
            Results[SelectedIndex].IsSelected = true;
        }
    }

    [RelayCommand]
    public async Task ExecuteSelectionAsync(QuickSwitcherItemViewModel? item = null)
    {
        var target = item ?? (SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null);
        if (target == null) return;

        Close();

        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();

        switch (target.Type)
        {
            case QuickSwitcherItemType.Configuration:
                mainVm.NavigateToConfiguration(target.TargetId);
                break;

            case QuickSwitcherItemType.Set:
                mainVm.NavigateToSet(target.TargetId);
                break;

            case QuickSwitcherItemType.Item:
                mainVm.NavigateToItem(target.TargetId, target.SecondaryId);
                break;

            case QuickSwitcherItemType.Place:
                if (!string.IsNullOrEmpty(target.PlaceTarget))
                {
                    var parts = target.PlaceTarget.Split(':', 2);
                    var category = parts[0];
                    var sub = parts.Length > 1 ? parts[1] : string.Empty;

                    switch (category)
                    {
                        case "Tab":
                            switch (sub)
                            {
                                case "Library":
                                    mainVm.NavigateToContentManager();
                                    break;
                                case "Configurations":
                                    mainVm.NavigateToConfigurations();
                                    break;
                                case "Tools":
                                    mainVm.NavigateToTools();
                                    break;
                                case "Settings":
                                    mainVm.NavigateToSettings();
                                    break;
                            }
                            break;

                        case "Tools":
                            mainVm.NavigateToToolSection(sub);
                            break;

                        case "Settings":
                            mainVm.NavigateToSettingsSection(sub);
                            break;
                    }
                }
                break;
        }
    }

    private List<QuickSwitcherItemViewModel> GetPlaces()
    {
        var loc = LocalizationManager.Instance;
        var tealBrush = Brush.Parse("#2dd4bf");

        return new List<QuickSwitcherItemViewModel>
        {
            // Primary Tabs
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Tab:Library",
                Title = loc.GetString("nav.library") ?? "Library",
                Subtitle = "Main content library and package explorer",
                Keywords = "library packages mods cc content manager explore catalog items",
                IconKind = PackIconBoxIconsKind.RegularFolder,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Tab:Configurations",
                Title = loc.GetString("nav.configurations") ?? "Configurations",
                Subtitle = "Game profiles, presets, and active mod sets",
                Keywords = "configurations profiles presets setups configs active loadouts",
                IconKind = PackIconBoxIconsKind.RegularSliderAlt,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Tab:Tools",
                Title = loc.GetString("nav.tools") ?? "Tools",
                Subtitle = "Sims 3 utilities, cleaner, scanner, and creator tools",
                Keywords = "tools utilities maintenance health diagnostics scanner fixer cleaner",
                IconKind = PackIconBoxIconsKind.RegularSpanner,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Tab:Settings",
                Title = loc.GetString("nav.settings") ?? "Settings",
                Subtitle = "Preferences, appearance, directories, and options",
                Keywords = "settings preferences options configuration app setup storage engine",
                IconKind = PackIconBoxIconsKind.RegularCog,
                IconBrush = tealBrush
            },

            // Tools Tab Sub-sections
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Tools:Cache",
                Title = loc.GetString("tools.cache.title") ?? "Cache Cleaner",
                Subtitle = (loc.GetString("nav.tools") ?? "Tools") + "  •  " + (loc.GetString("tools.cache.subtitle") ?? "Purge temporary and corrupted cache files"),
                Keywords = "tools cache cleaner temp garbage compositor simcompositor cleanup delete purge thumbnails",
                IconKind = PackIconBoxIconsKind.RegularTrash,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Tools:Conflicts",
                Title = loc.GetString("tools.conflicts.title") ?? "Conflict Scanner",
                Subtitle = (loc.GetString("nav.tools") ?? "Tools") + "  •  " + (loc.GetString("tools.conflicts.subtitle") ?? "Scan for duplicate GUIDs and resource conflicts"),
                Keywords = "tools conflict scanner duplicate conflicts collision overrides scan check corrupt",
                IconKind = PackIconBoxIconsKind.RegularShield,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Tools:Patterns",
                Title = loc.GetString("tools.patterns.title") ?? "Pattern Fixer",
                Subtitle = (loc.GetString("nav.tools") ?? "Tools") + "  •  " + (loc.GetString("tools.patterns.subtitle") ?? "Detect and repair corrupted CAP patterns in packages"),
                Keywords = "tools pattern fixer corrupted cap patterns materials repair fix create a style",
                IconKind = PackIconBoxIconsKind.RegularPalette,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Tools:Collections",
                Title = loc.GetString("tools.collections.title") ?? "Collection Creator",
                Subtitle = (loc.GetString("nav.tools") ?? "Tools") + "  •  " + (loc.GetString("tools.collections.subtitle") ?? "Build custom in-game collections for Buy/Build mode"),
                Keywords = "tools collection creator in-game collections sets categories buy build catalog",
                IconKind = PackIconBoxIconsKind.RegularFolder,
                IconBrush = tealBrush
            },

            // Settings Tab Sub-sections
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Settings:Updates",
                Title = loc.GetString("settings.update.title") ?? "Updates",
                Subtitle = (loc.GetString("nav.settings") ?? "Settings") + "  •  " + (loc.GetString("settings.update_available") ?? "Check for app updates, changelog, and new releases"),
                Keywords = "settings updates releases version changelog download upgrade new",
                IconKind = PackIconBoxIconsKind.RegularRocket,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Settings:Appearance",
                Title = loc.GetString("settings.appearance.title") ?? "Appearance & Personalization",
                Subtitle = (loc.GetString("nav.settings") ?? "Settings") + "  •  " + (loc.GetString("settings.appearance.accent_color_help") ?? "Theme mode, accent colors, and display language"),
                Keywords = "settings appearance language colors accent theme personalization style ui dark light mode",
                IconKind = PackIconBoxIconsKind.RegularPalette,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Settings:Storage",
                Title = loc.GetString("settings.storage.title") ?? "Storage & Paths",
                Subtitle = (loc.GetString("nav.settings") ?? "Settings") + "  •  " + (loc.GetString("settings.storage.library_dir_help") ?? "Library directory, Sims 3 game path, documents path"),
                Keywords = "settings storage directories paths library game documents electronic arts folder base",
                IconKind = PackIconBoxIconsKind.RegularFolder,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Settings:ObservedFolders",
                Title = loc.GetString("settings.observed.title") ?? "Monitored Folders & Auto-Import",
                Subtitle = (loc.GetString("nav.settings") ?? "Settings") + "  •  " + (loc.GetString("settings.observed.auto_scan_help") ?? "Watched directories and automatic launch ingestion"),
                Keywords = "settings observed folders watch downloads monitoring auto import ingestion directory",
                IconKind = PackIconBoxIconsKind.RegularFolderOpen,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Settings:Engine",
                Title = loc.GetString("settings.engine.title") ?? "Cache Compilation Engine",
                Subtitle = (loc.GetString("nav.settings") ?? "Settings") + "  •  " + (loc.GetString("settings.engine.cache_mode") ?? "Dynamic cache, static bundle, and compression level"),
                Keywords = "settings engine cache mode static bundle dynamic compression performance build speed",
                IconKind = PackIconBoxIconsKind.RegularCog,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Settings:Maintenance",
                Title = loc.GetString("settings.maintenance.title") ?? "Maintenance & Diagnostics",
                Subtitle = (loc.GetString("nav.settings") ?? "Settings") + "  •  " + (loc.GetString("settings.maintenance.help") ?? "Recheck types, database auto-fix, CC Magic migration"),
                Keywords = "settings maintenance diagnostics database repair fix cc magic migration recheck types s3mo",
                IconKind = PackIconBoxIconsKind.RegularSpanner,
                IconBrush = tealBrush
            },
            new()
            {
                Type = QuickSwitcherItemType.Place,
                PlaceTarget = "Settings:Thanks",
                Title = loc.GetString("settings.thanks.title") ?? "About & Credits",
                Subtitle = (loc.GetString("nav.settings") ?? "Settings") + "  •  " + "Special thanks, open-source acknowledgements, and GitHub",
                Keywords = "settings thanks acknowledgements credits granthes peter inge jones github about license",
                IconKind = PackIconBoxIconsKind.RegularHeart,
                IconBrush = tealBrush
            }
        };
    }
}
