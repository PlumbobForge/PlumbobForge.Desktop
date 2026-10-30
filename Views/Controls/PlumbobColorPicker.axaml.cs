using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.Views.Controls;

public partial class PlumbobColorPicker : UserControl
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<PlumbobColorPicker, Color>(
            nameof(Color),
            Color.Parse("#10b981"),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsSavePresetVisibleProperty =
        AvaloniaProperty.Register<PlumbobColorPicker, bool>(
            nameof(IsSavePresetVisible),
            false);

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public bool IsSavePresetVisible
    {
        get => GetValue(IsSavePresetVisibleProperty);
        set => SetValue(IsSavePresetVisibleProperty, value);
    }

    public event EventHandler<ColorChangedEventArgs>? ColorChanged;
    public event EventHandler<Color>? SavePresetRequested;

    private bool _isUpdating;

    public PlumbobColorPicker()
    {
        InitializeComponent();

        HueSlider.ColorModel = ColorModel.Hsva;
        HueSlider.ColorComponent = ColorComponent.Component1;

        RedSlider.ColorModel = ColorModel.Rgba;
        RedSlider.ColorComponent = ColorComponent.Component1;

        GreenSlider.ColorModel = ColorModel.Rgba;
        GreenSlider.ColorComponent = ColorComponent.Component2;

        BlueSlider.ColorModel = ColorModel.Rgba;
        BlueSlider.ColorComponent = ColorComponent.Component3;

        SpectrumControl.ColorChanged += OnSpectrumColorChanged;
        HueSlider.ColorChanged += OnHueColorChanged;
        RedSlider.ColorChanged += OnRedSliderChanged;
        GreenSlider.ColorChanged += OnGreenSliderChanged;
        BlueSlider.ColorChanged += OnBlueSliderChanged;

        UpdateControlsFromColor(Color, source: "Init");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColorProperty)
        {
            var newCol = change.GetNewValue<Color>();
            UpdateControlsFromColor(newCol, source: "External");
        }
        else if (change.Property == IsSavePresetVisibleProperty)
        {
            SavePresetButton.IsVisible = change.GetNewValue<bool>();
        }
    }

    private void UpdateControlsFromColor(Color color, string source)
    {
        if (_isUpdating) return;
        _isUpdating = true;

        try
        {
            var hex = ColorPaletteGenerator.ColorToHex(color);

            if (source != "HexBox")
            {
                HexTextBox.Text = hex;
            }

            PreviewSwatch.Background = new SolidColorBrush(color);

            if (source != "Spectrum" && source != "Hue")
            {
                SpectrumControl.Color = color;
                HueSlider.Color = color;
            }

            if (source != "Red")
            {
                RedSlider.Color = color;
            }
            if (source != "Green")
            {
                GreenSlider.Color = color;
            }
            if (source != "Blue")
            {
                BlueSlider.Color = color;
            }

            RedValueText.Text = color.R.ToString();
            GreenValueText.Text = color.G.ToString();
            BlueValueText.Text = color.B.ToString();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void PropagateColorChange(Color newColor, string source)
    {
        if (_isUpdating) return;

        var oldColor = Color;
        if (oldColor == newColor) return;

        SetCurrentValue(ColorProperty, newColor);
        UpdateControlsFromColor(newColor, source);
        ColorChanged?.Invoke(this, new ColorChangedEventArgs(oldColor, newColor));
    }

    private void OnSpectrumColorChanged(object? sender, ColorChangedEventArgs e)
    {
        PropagateColorChange(e.NewColor, "Spectrum");
    }

    private void OnHueColorChanged(object? sender, ColorChangedEventArgs e)
    {
        PropagateColorChange(SpectrumControl.Color, "Hue");
    }

    private void OnRedSliderChanged(object? sender, ColorChangedEventArgs e)
    {
        var current = Color;
        var updated = Color.FromArgb(current.A, e.NewColor.R, current.G, current.B);
        PropagateColorChange(updated, "Red");
    }

    private void OnGreenSliderChanged(object? sender, ColorChangedEventArgs e)
    {
        var current = Color;
        var updated = Color.FromArgb(current.A, current.R, e.NewColor.G, current.B);
        PropagateColorChange(updated, "Green");
    }

    private void OnBlueSliderChanged(object? sender, ColorChangedEventArgs e)
    {
        var current = Color;
        var updated = Color.FromArgb(current.A, current.R, current.G, e.NewColor.B);
        PropagateColorChange(updated, "Blue");
    }

    private void OnHexTextBoxChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdating) return;
        var text = HexTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (ColorPaletteGenerator.TryParseHex(text, out var parsed))
        {
            PropagateColorChange(parsed, "HexBox");
        }
    }

    private void OnSimpleTabClick(object? sender, RoutedEventArgs e)
    {
        SimplePanel.IsVisible = true;
        AdvancedPanel.IsVisible = false;
        SimpleTabButton.Classes.Add("active");
        AdvancedTabButton.Classes.Remove("active");
        UpdateControlsFromColor(Color, source: "External");
    }

    private void OnAdvancedTabClick(object? sender, RoutedEventArgs e)
    {
        SimplePanel.IsVisible = false;
        AdvancedPanel.IsVisible = true;
        SimpleTabButton.Classes.Remove("active");
        AdvancedTabButton.Classes.Add("active");
        UpdateControlsFromColor(Color, source: "External");
    }

    private void OnSavePresetClick(object? sender, RoutedEventArgs e)
    {
        SavePresetRequested?.Invoke(this, Color);
    }
}
