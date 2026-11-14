using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views;

public partial class GuidePanelView : UserControl
{
    private GuideViewModel? _vm;

    public GuidePanelView()
    {
        InitializeComponent();
        SearchBox.KeyDown += OnSearchKeyDown;
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as GuideViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A new page starts at the top.
        if (e.PropertyName == nameof(GuideViewModel.Version))
            ContentScroller.Offset = new Vector(0, 0);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm != null)
        {
            if (!string.IsNullOrEmpty(_vm.SearchText)) _vm.SearchText = string.Empty;
            else _vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    public void FocusSearch() => Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
}
