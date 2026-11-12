using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.ContentManager;

internal sealed class ItemViewTracker
{
    private ItemViewModel? _shown;
    private bool _inTree;

    public void SetInTree(bool inTree, object? dataContext)
    {
        _inTree = inTree;
        Update(dataContext);
    }

    public void Update(object? dataContext)
    {
        var item = _inTree ? dataContext as ItemViewModel : null;
        if (ReferenceEquals(item, _shown)) return;
        _shown?.DetachView();
        _shown = item;
        _shown?.AttachView();
    }
}
