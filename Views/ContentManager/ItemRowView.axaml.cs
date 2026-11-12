using System;
using Avalonia.Controls;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.ContentManager;

public partial class ItemRowView : UserControl
{
    public ItemRowView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        var cards = CardsGrid.Children;
        if (DataContext is not ItemRowViewModel row)
        {
            // Recycled: let go of the items so their thumbnails are no longer wanted.
            foreach (var card in cards) card.DataContext = null;
            return;
        }

        int columns = Math.Max(1, row.ColumnCount);
        CardsGrid.Columns = columns;
        while (cards.Count > columns)
        {
            cards[^1].DataContext = null;
            cards.RemoveAt(cards.Count - 1);
        }
        while (cards.Count < columns)
        {
            cards.Add(new ItemCardView());
        }

        for (int i = 0; i < cards.Count; i++)
        {
            var item = i < row.Items.Count ? row.Items[i] : null;
            cards[i].DataContext = item;
            cards[i].IsVisible = item != null;
        }
    }
}
