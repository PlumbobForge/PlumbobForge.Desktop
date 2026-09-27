using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using IconPacks.Avalonia.BoxIcons;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.Views.Dialogs;

public record CustomizeSetResult(bool Confirmed, string Name, string? Icon, string? Color);

public partial class CustomizeSetDialogWindow : Window
{
    private static readonly List<PackIconBoxIconsKind> AvailableIcons = new()
    {
        // Sets & Folders
        PackIconBoxIconsKind.RegularFolder,
        PackIconBoxIconsKind.RegularFolderOpen,
        PackIconBoxIconsKind.RegularFolderHeart,
        PackIconBoxIconsKind.RegularFolderStar,
        PackIconBoxIconsKind.RegularFolderCheck,
        PackIconBoxIconsKind.RegularPackage,
        PackIconBoxIconsKind.RegularBox,
        PackIconBoxIconsKind.RegularLayers,
        PackIconBoxIconsKind.RegularGrid,
        PackIconBoxIconsKind.RegularTag,
        PackIconBoxIconsKind.RegularBookmark,
        PackIconBoxIconsKind.RegularBookmarks,

        // Sims, CAS & Clothing
        PackIconBoxIconsKind.RegularUser,
        PackIconBoxIconsKind.RegularFace,
        PackIconBoxIconsKind.RegularFaceAlt,
        PackIconBoxIconsKind.RegularBody,
        PackIconBoxIconsKind.RegularHanger,
        PackIconBoxIconsKind.RegularTShirt,
        PackIconBoxIconsKind.RegularPant,
        PackIconBoxIconsKind.RegularSneaker,
        PackIconBoxIconsKind.RegularGlasses,
        PackIconBoxIconsKind.RegularSmile,
        PackIconBoxIconsKind.RegularPalette,
        PackIconBoxIconsKind.RegularDiamond,
        PackIconBoxIconsKind.RegularSlider,

        // Lots, Objects & Build/Buy
        PackIconBoxIconsKind.RegularHome,
        PackIconBoxIconsKind.RegularGlobe,
        PackIconBoxIconsKind.RegularChair,
        PackIconBoxIconsKind.RegularBed,
        PackIconBoxIconsKind.RegularCar,
        PackIconBoxIconsKind.RegularCoffee,

        // Extras, Media & Lifestyle
        PackIconBoxIconsKind.RegularHeart,
        PackIconBoxIconsKind.RegularStar,
        PackIconBoxIconsKind.RegularSparkles,
        PackIconBoxIconsKind.RegularShoppingBag,
        PackIconBoxIconsKind.RegularShoppingBagAlt,
        PackIconBoxIconsKind.RegularGift,
        PackIconBoxIconsKind.RegularTrophy,
        PackIconBoxIconsKind.RegularMusic,
        PackIconBoxIconsKind.RegularFilm,
        PackIconBoxIconsKind.RegularJoystick,
        PackIconBoxIconsKind.RegularCamera,
        PackIconBoxIconsKind.RegularBook,
        PackIconBoxIconsKind.RegularPlanet,
        PackIconBoxIconsKind.RegularGhost,
        PackIconBoxIconsKind.RegularSun,
        PackIconBoxIconsKind.RegularMoon
    };

    private static readonly List<(string Name, string? Hex)> AvailableColors = new()
    {
        ("Default", null),
        ("Emerald", "#10b981"),
        ("Teal", "#14b8a6"),
        ("Cyan", "#06b6d4"),
        ("Sky", "#38bdf8"),
        ("Blue", "#3b82f6"),
        ("Indigo", "#6366f1"),
        ("Violet", "#8b5cf6"),
        ("Purple", "#a855f7"),
        ("Fuchsia", "#d946ef"),
        ("Pink", "#ec4899"),
        ("Rose", "#f43f5e"),
        ("Red", "#ef4444"),
        ("Orange", "#f97316"),
        ("Amber", "#eab308"),
        ("Lime", "#84cc16"),
        ("Slate", "#94a3b8")
    };

    private string _selectedIconName = "RegularFolder";
    private string? _selectedColorHex = null;
    private readonly List<Button> _iconButtons = new();
    private readonly List<Button> _colorButtons = new();

    public CustomizeSetDialogWindow()
    {
        InitializeComponent();
        PopulatePickers();
    }

    public CustomizeSetDialogWindow(string currentName, string? currentIcon, string? currentColor) : this()
    {
        SetNameTextBox.Text = currentName;
        _selectedIconName = string.IsNullOrWhiteSpace(currentIcon) ? "RegularFolder" : currentIcon;
        // If color was emerald default or null, treat as default (null)
        _selectedColorHex = (string.IsNullOrWhiteSpace(currentColor) || currentColor == "#10b981") ? null : currentColor;

        UpdateActiveSelections();
        UpdatePreview();
        ValidateInput();
    }

    private void PopulatePickers()
    {
        // 1. Populate Icons
        IconsWrapPanel.Children.Clear();
        _iconButtons.Clear();

        foreach (var iconKind in AvailableIcons)
        {
            var kindName = iconKind.ToString();
            var btn = new Button
            {
                Classes = { "icon-pick-btn" },
                Tag = kindName,
                Content = new PackIconBoxIcons
                {
                    Kind = iconKind,
                    Width = 18,
                    Height = 18,
                    Foreground = Brushes.White,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(btn, kindName);

            btn.Click += (s, e) =>
            {
                _selectedIconName = kindName;
                UpdateActiveSelections();
                UpdatePreview();
            };

            _iconButtons.Add(btn);
            IconsWrapPanel.Children.Add(btn);
        }

        // 2. Populate Colors
        ColorsWrapPanel.Children.Clear();
        _colorButtons.Clear();

        foreach (var (colorName, hex) in AvailableColors)
        {
            var brush = hex != null ? Brush.Parse(hex) : ThemeService.GetCurrentAccentBrush();
            var btn = new Button
            {
                Classes = { "color-pick-btn" },
                Tag = hex,
                Background = brush
            };
            ToolTip.SetTip(btn, hex != null ? colorName : "Default (Current Theme Accent)");

            btn.Click += (s, e) =>
            {
                _selectedColorHex = hex;
                UpdateActiveSelections();
                UpdatePreview();
            };

            _colorButtons.Add(btn);
            ColorsWrapPanel.Children.Add(btn);
        }
    }

    private void UpdateActiveSelections()
    {
        foreach (var btn in _iconButtons)
        {
            bool isSel = (string?)btn.Tag == _selectedIconName;
            if (isSel)
            {
                if (!btn.Classes.Contains("selected")) btn.Classes.Add("selected");
            }
            else
            {
                btn.Classes.Remove("selected");
            }
        }

        foreach (var btn in _colorButtons)
        {
            bool isSel = (string?)btn.Tag == _selectedColorHex;
            if (isSel)
            {
                if (!btn.Classes.Contains("selected")) btn.Classes.Add("selected");
            }
            else
            {
                btn.Classes.Remove("selected");
            }
        }
    }

    private void UpdatePreview()
    {
        if (Enum.TryParse<PackIconBoxIconsKind>(_selectedIconName, out var kind))
        {
            PreviewIcon.Kind = kind;
        }
        else
        {
            PreviewIcon.Kind = PackIconBoxIconsKind.RegularFolder;
        }

        var colorBrush = (!string.IsNullOrEmpty(_selectedColorHex) && _selectedColorHex != "#10b981")
            ? Brush.Parse(_selectedColorHex)
            : ThemeService.GetCurrentAccentBrush();

        PreviewIcon.Foreground = colorBrush;
        PreviewName.Text = string.IsNullOrWhiteSpace(SetNameTextBox.Text) ? "Set Name" : SetNameTextBox.Text.Trim();
    }

    private void OnSetNameChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePreview();
        ValidateInput();
    }

    private bool ValidateInput()
    {
        var name = SetNameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ErrorTextBlock.Text = "Name cannot be empty.";
            ErrorTextBlock.IsVisible = true;
            SaveButton.IsEnabled = false;
            return false;
        }

        if (!PlumbobForge.Desktop.Utils.NameValidator.IsValidName(name, out var error))
        {
            ErrorTextBlock.Text = error;
            ErrorTextBlock.IsVisible = true;
            SaveButton.IsEnabled = false;
            return false;
        }

        ErrorTextBlock.IsVisible = false;
        SaveButton.IsEnabled = true;
        return true;
    }

    private void OnResetDefaultClick(object? sender, RoutedEventArgs e)
    {
        _selectedIconName = "RegularFolder";
        _selectedColorHex = null;
        UpdateActiveSelections();
        UpdatePreview();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new CustomizeSetResult(false, SetNameTextBox.Text?.Trim() ?? string.Empty, null, null));
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (!ValidateInput()) return;

        var name = SetNameTextBox.Text?.Trim() ?? string.Empty;
        Close(new CustomizeSetResult(true, name, _selectedIconName, _selectedColorHex));
    }
}
