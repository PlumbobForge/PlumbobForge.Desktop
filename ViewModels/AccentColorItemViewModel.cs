using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class AccentColorItemViewModel : ObservableObject
{
    public AccentOption Accent { get; }

    public string Name => Accent.Name;
    public string DisplayName => Accent.DisplayName;
    public IBrush SwatchBrush { get; }

    [ObservableProperty]
    private bool _isSelected;

    public AccentColorItemViewModel(AccentOption accent, bool isSelected)
    {
        Accent = accent;
        IsSelected = isSelected;
        SwatchBrush = new SolidColorBrush(Color.Parse(accent.Hex));
    }
}
