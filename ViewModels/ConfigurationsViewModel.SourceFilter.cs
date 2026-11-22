using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

// All / Sets / Mods: one filter both lists follow, shown only when sets and the Mods folder are both managed.
// Library items count as Sets, Mods files as Mods. Hidden rows are out of the lists, so selecting, dragging
// and moving only ever touch what is shown.
public partial class ConfigurationsViewModel
{
    public const string SourceAll = "All";
    public const string SourceSets = "Sets";
    public const string SourceMods = "Mods";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceAll))]
    [NotifyPropertyChangedFor(nameof(IsSourceSets))]
    [NotifyPropertyChangedFor(nameof(IsSourceMods))]
    private string _sourceFilter = ConfigurationsUiState.Load().SourceFilter;

    [ObservableProperty]
    private int _setsSourceCount;

    [ObservableProperty]
    private int _modsSourceCount;

    // Shown over a list the filter or search left empty; null otherwise.
    [ObservableProperty]
    private string? _enabledEmptyText;

    [ObservableProperty]
    private string? _disabledEmptyText;

    public bool ShowSourceFilter => ModsFolderSupport.IsEnabled && !ModsFolderSupport.ModsOnly;
    private string EffectiveSourceFilter => ShowSourceFilter ? SourceFilter : SourceAll;
    public bool IsSourceAll => EffectiveSourceFilter == SourceAll;
    public bool IsSourceSets => EffectiveSourceFilter == SourceSets;
    public bool IsSourceMods => EffectiveSourceFilter == SourceMods;

    // What the lists hold, for their titles and search hints: Mods-only always lists Mods, sets-only always sets.
    private string ListKind =>
        ModsFolderSupport.ModsOnly ? SourceMods
        : !ModsFolderSupport.IsEnabled ? SourceSets
        : EffectiveSourceFilter;

    public string EnabledListTitle => LM(ListKind switch
    {
        SourceSets => "configurations.enabled_sets",
        SourceMods => "configurations.enabled_mods",
        _ => "configurations.enabled_all"
    });

    public string DisabledListTitle => LM(ListKind switch
    {
        SourceSets => "configurations.disabled_sets",
        SourceMods => "configurations.disabled_mods",
        _ => "configurations.disabled_all"
    });

    public string ListSearchPlaceholder => LM(ListKind switch
    {
        SourceSets => "configurations.search_sets_placeholder",
        SourceMods => "configurations.search_mods_placeholder",
        _ => "configurations.search_all_placeholder"
    });

    private bool _listTextsHooked;

    private void UpdateListTexts()
    {
        if (!_listTextsHooked)
        {
            _listTextsHooked = true;
            PlumbobForge.Desktop.Services.Localization.LocalizationManager.Instance.LanguageChanged += _ => UpdateListTexts();
        }
        OnPropertyChanged(nameof(EnabledListTitle));
        OnPropertyChanged(nameof(DisabledListTitle));
        OnPropertyChanged(nameof(ListSearchPlaceholder));
    }

    [RelayCommand]
    public void SetSourceFilter(string filter)
    {
        if (filter is SourceAll or SourceSets or SourceMods) SourceFilter = filter;
    }

    partial void OnSourceFilterChanged(string value)
    {
        // Nothing stays selected out of sight.
        foreach (var card in _itemCards) card.IsSelected = false;
        ClearSelection();
        ConfigurationsUiState.Save(new ConfigurationsUiState { SourceFilter = value });
        RefreshSetsColumns();
    }

    // Called with both full columns, before the filter and searches.
    private void UpdateSourceCounts(List<ConfigColumnEntry> enabled, List<ConfigColumnEntry> disabled)
    {
        OnPropertyChanged(nameof(ShowSourceFilter));
        OnPropertyChanged(nameof(IsSourceAll));
        OnPropertyChanged(nameof(IsSourceSets));
        OnPropertyChanged(nameof(IsSourceMods));
        UpdateListTexts();
        SetsSourceCount = enabled.Count(e => !e.IsModsEntry) + disabled.Count(e => !e.IsModsEntry);
        ModsSourceCount = enabled.Count(e => e.IsModsEntry) + disabled.Count(e => e.IsModsEntry);
    }

    private List<ConfigColumnEntry> ApplySourceFilter(List<ConfigColumnEntry> column) => EffectiveSourceFilter switch
    {
        SourceSets => column.Where(e => !e.IsModsEntry).ToList(),
        SourceMods => column.Where(e => e.IsModsEntry).ToList(),
        _ => column
    };

    private void UpdateEmptyTexts()
    {
        EnabledEmptyText = EmptyText(EnabledSets.Count, true, EnabledSetsSearchQuery);
        DisabledEmptyText = EmptyText(DisabledSets.Count, false, DisabledSetsSearchQuery);
    }

    private string? EmptyText(int count, bool enabledColumn, string search)
    {
        if (count > 0 || SelectedConfiguration == null) return null;
        if (!string.IsNullOrWhiteSpace(search)) return LM("configurations.empty_search");
        string side = enabledColumn ? "on" : "off";
        return EffectiveSourceFilter switch
        {
            SourceSets => LM($"configurations.empty_{side}_sets"),
            SourceMods => LM($"configurations.empty_{side}_mods"),
            _ => null
        };
    }
}
