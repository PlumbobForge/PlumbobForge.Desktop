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
    private string _selectedCategory = "All"; // "All", "Sets", "Configurations", "Items"

    public bool IsAllSelected => SelectedCategory == "All";
    public bool IsConfigsSelected => SelectedCategory == "Configurations";
    public bool IsSetsSelected => SelectedCategory == "Sets";
    public bool IsItemsSelected => SelectedCategory == "Items";

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

            bool includeConfigs = SelectedCategory == "All" || SelectedCategory == "Configurations";
            bool includeSets = SelectedCategory == "All" || SelectedCategory == "Sets";
            bool includeItems = SelectedCategory == "All" || SelectedCategory == "Items";

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
                if (target.SecondaryId.HasValue)
                {
                    mainVm.NavigateToItem(target.SecondaryId.Value, target.TargetId);
                }
                break;
        }
    }
}
