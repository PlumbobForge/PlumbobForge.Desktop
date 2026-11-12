using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlumbobForge.Desktop.ViewModels;

public class ItemRowViewModel : ObservableObject
{
    public IReadOnlyList<ItemViewModel> Items { get; }
    public int ColumnCount { get; }

    public ItemRowViewModel(IReadOnlyList<ItemViewModel> items, int columnCount)
    {
        Items = items;
        ColumnCount = columnCount;
    }
}
