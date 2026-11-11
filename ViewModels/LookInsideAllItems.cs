using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public sealed partial class LookInsideAllItems : ObservableObject
{
    private static string L(string key, params object[] args) => LocalizationManager.Instance.GetString(key, args);

    private readonly List<LookInsideObjectItem> _objects;
    private readonly List<LookInsideObjectItem> _editableCas;

    public string Title => L("look_inside.all_items");
    public string Subtitle { get; }
    public IReadOnlyList<LookInsideImage> MosaicImages { get; }

    public IReadOnlyList<AllItemsFlagOption> OutfitOptions { get; }
    public bool HasEditableCas => _editableCas.Count > 0;

    public int SkippedCasCount { get; }
    public bool HasSkippedCas => SkippedCasCount > 0;
    public string SkippedCasText => L("look_inside.all_items_skipped_cas", SkippedCasCount);

    public string InGameText => L("look_inside.all_items_in_game", _objects.Count(o => o.IsEnabledInGame), _objects.Count);
    public bool CanTurnAllOn => _objects.Any(o => !o.IsEnabledInGame);
    public bool CanTurnAllOff => _objects.Any(o => o.IsEnabledInGame);

    public bool? ValidForRandom
    {
        get => Aggregate(_editableCas.Select(o => o.ValidForRandom));
        set
        {
            if (value is not bool v) { OnPropertyChanged(); return; }
            foreach (var obj in _editableCas) obj.ValidForRandom = v;
        }
    }

    public bool HasMixed => ValidForRandom == null || OutfitOptions.Any(o => o.IsChecked == null);

    public LookInsideAllItems(IReadOnlyList<LookInsideObjectItem> objects)
    {
        _objects = objects.ToList();
        _editableCas = _objects.Where(o => o.CanEditCasFlags).ToList();
        SkippedCasCount = _objects.Count(o => o.IsCas && !o.CanEditCasFlags);

        int cas = _objects.Count(o => o.IsCas);
        int bb = _objects.Count(o => o.IsBuildBuy);
        var parts = new List<string> { L("look_inside.all_items_count", _objects.Count) };
        if (cas > 0 && bb > 0)
        {
            parts.Add(L("look_inside.summary_cas", cas));
            parts.Add(L("look_inside.summary_bb", bb));
        }
        Subtitle = string.Join("  ·  ", parts);
        MosaicImages = PickMosaic(_objects);

        OutfitOptions = CasFlagsService.OutfitBits
            .Select(b => new AllItemsFlagOption(L($"filters.outfits.{b.Key}"), b.Bit, _editableCas))
            .ToList();
    }

    [RelayCommand]
    private void TurnAllOn()
    {
        foreach (var obj in _objects) obj.IsEnabledInGame = true;
    }

    [RelayCommand]
    private void TurnAllOff()
    {
        foreach (var obj in _objects) obj.IsEnabledInGame = false;
    }

    public void Refresh()
    {
        foreach (var option in OutfitOptions) option.Refresh();
        OnPropertyChanged(nameof(ValidForRandom));
        OnPropertyChanged(nameof(HasMixed));
        OnPropertyChanged(nameof(InGameText));
        OnPropertyChanged(nameof(CanTurnAllOn));
        OnPropertyChanged(nameof(CanTurnAllOff));
    }

    internal static bool? Aggregate(IEnumerable<bool> values)
    {
        bool any = false, all = true;
        foreach (bool v in values)
        {
            any |= v;
            all &= v;
        }
        return all ? true : any ? null : false;
    }

    internal static IReadOnlyList<LookInsideImage> PickMosaic(IEnumerable<LookInsideObjectItem> objects) =>
        objects.Select(o => o.Preview).OfType<LookInsideImage>().Take(4).ToList();
}

public sealed class AllItemsFlagOption : ObservableObject
{
    private readonly IReadOnlyList<LookInsideObjectItem> _objects;

    public string Label { get; }
    public uint Bit { get; }

    public bool? IsChecked
    {
        get => LookInsideAllItems.Aggregate(_objects.Select(o => o.OutfitOptions.FirstOrDefault(x => x.Bit == Bit)?.IsChecked == true));
        set
        {
            if (value is not bool v) { OnPropertyChanged(); return; }
            foreach (var obj in _objects)
            {
                var option = obj.OutfitOptions.FirstOrDefault(x => x.Bit == Bit);
                if (option != null) option.IsChecked = v;
            }
        }
    }

    public AllItemsFlagOption(string label, uint bit, IReadOnlyList<LookInsideObjectItem> objects)
    {
        Label = label;
        Bit = bit;
        _objects = objects;
    }

    public void Refresh() => OnPropertyChanged(nameof(IsChecked));
}
