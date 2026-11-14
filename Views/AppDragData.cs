using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PlumbobForge.Desktop.Views;

// The app's own drag-and-drop formats, and how a drag is started.
internal static class AppDragData
{
    // Library items being moved to a set (they also carry their files, for Explorer).
    public static readonly DataFormat<string> ItemDrag = DataFormat.CreateStringApplicationFormat("plumbob-item-drag");
    // Sets being moved in the sets tree: comma-separated set ids.
    public static readonly DataFormat<string> SetsDrag = DataFormat.CreateStringApplicationFormat("plumbob-sets-drag");
    // Configuration cards moving between the Enabled and Disabled columns (ConfigDragPayload).
    public static readonly DataFormat<string> ConfigCards = DataFormat.CreateStringApplicationFormat("plumbobforge-config-sets");

    // Avalonia 12 starts a system drag only from the press that began it; our drags start once the
    // pointer has moved a few pixels, so the latest press is remembered here.
    private static PointerPressedEventArgs? _lastPress;
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, e) => _lastPress = e, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public static DataTransfer Create(DataFormat<string> format, string value)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(format, value));
        return data;
    }

    public static Task<DragDropEffects> StartAsync(IDataTransfer data, DragDropEffects allowed) =>
        _lastPress is { } press
            ? DragDrop.DoDragDropAsync(press, data, allowed)
            : Task.FromResult(DragDropEffects.None);
}
