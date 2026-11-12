using Avalonia.Controls;

namespace PlumbobForge.Desktop.Views.ContentManager;

public partial class ItemDetailsFlyoutView : UserControl
{
    public ItemDetailsFlyoutView()
    {
        InitializeComponent();
    }

    public static void AttachTo(Button? button)
    {
        if (button == null || button.Flyout != null) return;

        var content = new ItemDetailsFlyoutView();
        content[!DataContextProperty] = button[!DataContextProperty];
        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = content
        };
        button.Flyout = flyout;
        flyout.ShowAt(button);
    }
}
