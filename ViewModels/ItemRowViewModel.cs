using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlumbobForge.Desktop.ViewModels;

public class ItemRowViewModel : ObservableObject
{
    public IReadOnlyList<ItemViewModel> Items { get; }
    public int ColumnCount { get; }

    public ItemViewModel? Item1 => Items.Count > 0 ? Items[0] : null;
    public ItemViewModel? Item2 => Items.Count > 1 ? Items[1] : null;
    public ItemViewModel? Item3 => Items.Count > 2 ? Items[2] : null;
    public ItemViewModel? Item4 => Items.Count > 3 ? Items[3] : null;
    public ItemViewModel? Item5 => Items.Count > 4 ? Items[4] : null;
    public ItemViewModel? Item6 => Items.Count > 5 ? Items[5] : null;
    public ItemViewModel? Item7 => Items.Count > 6 ? Items[6] : null;
    public ItemViewModel? Item8 => Items.Count > 7 ? Items[7] : null;
    public ItemViewModel? Item9 => Items.Count > 8 ? Items[8] : null;
    public ItemViewModel? Item10 => Items.Count > 9 ? Items[9] : null;
    public ItemViewModel? Item11 => Items.Count > 10 ? Items[10] : null;
    public ItemViewModel? Item12 => Items.Count > 11 ? Items[11] : null;

    public ItemRowViewModel(IReadOnlyList<ItemViewModel> items, int columnCount)
    {
        Items = items;
        ColumnCount = columnCount;
    }
}
