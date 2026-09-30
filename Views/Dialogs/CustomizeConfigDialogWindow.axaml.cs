using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using IconPacks.Avalonia.BoxIcons;
using PlumbobForge.Desktop.Services;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class CustomizeConfigDialogWindow : Window
{
    private string _configName;
    private string? _description;
    private string? _selectedColorHex;
    private string? _selectedIconName;
    private readonly bool _isActive;

    private readonly List<Button> _allIconButtons = new();
    private readonly List<Button> _colorButtons = new();

    public string ResultConfigName => ConfigNameTextBox.Text?.Trim() ?? _configName;
    public string? ResultDescription => string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text.Trim();
    public string? ResultColorHex => _selectedColorHex;
    public string? ResultIconName => _selectedIconName;

    // Curated Configuration / Preset Profile Icons
    private static readonly List<PackIconBoxIconsKind> AvailableIcons = new()
    {
        // Core Config & System
        PackIconBoxIconsKind.RegularSlider,
                PackIconBoxIconsKind.RegularCog,
                PackIconBoxIconsKind.RegularLayers,
        PackIconBoxIconsKind.RegularFolder,
        PackIconBoxIconsKind.RegularFolderOpen,
        PackIconBoxIconsKind.RegularFolderStar,
        PackIconBoxIconsKind.RegularPackage,
        PackIconBoxIconsKind.RegularBox,
        PackIconBoxIconsKind.RegularGrid,
        PackIconBoxIconsKind.RegularBookmarks,

        // Pets & Animals
        PackIconBoxIconsKind.RegularCat,
        PackIconBoxIconsKind.RegularDog,
        PackIconBoxIconsKind.RegularPawPrint,
        PackIconBoxIconsKind.RegularBone,
        PackIconBoxIconsKind.RegularFish,
        PackIconBoxIconsKind.RegularBird,
        PackIconBoxIconsKind.RegularBug,

        // Nature, Gardening & Weather
        PackIconBoxIconsKind.RegularLeaf,
        PackIconBoxIconsKind.RegularFlower,
        PackIconBoxIconsKind.RegularTree,
        PackIconBoxIconsKind.RegularPlantPot,
        PackIconBoxIconsKind.RegularSun,
        PackIconBoxIconsKind.RegularMoon,
        PackIconBoxIconsKind.RegularFlame,
        PackIconBoxIconsKind.RegularWater,

        // Occult, Supernatural & Magic
        PackIconBoxIconsKind.RegularMagicWand,
        PackIconBoxIconsKind.RegularGhost,
        PackIconBoxIconsKind.RegularSparkles,
        PackIconBoxIconsKind.RegularPlanet,

        // CAS & Sims
        PackIconBoxIconsKind.RegularUser,
        PackIconBoxIconsKind.RegularFace,
        PackIconBoxIconsKind.RegularHanger,
        PackIconBoxIconsKind.RegularTShirt,
        PackIconBoxIconsKind.RegularGlasses,
        PackIconBoxIconsKind.RegularMask,
        PackIconBoxIconsKind.RegularDiamond,
        PackIconBoxIconsKind.RegularSmile,

        // Lots, Objects & Build/Buy
        PackIconBoxIconsKind.RegularHome,
        PackIconBoxIconsKind.RegularBuilding,
        PackIconBoxIconsKind.RegularStore,
        PackIconBoxIconsKind.RegularGlobe,
        PackIconBoxIconsKind.RegularChair,
        PackIconBoxIconsKind.RegularBed,
        PackIconBoxIconsKind.RegularBath,
        PackIconBoxIconsKind.RegularTv,
        PackIconBoxIconsKind.RegularCar,

        // Food, Dining & Kitchen
        PackIconBoxIconsKind.RegularForkKnife,
        PackIconBoxIconsKind.RegularCoffee,
        PackIconBoxIconsKind.RegularBirthdayCake,
        PackIconBoxIconsKind.RegularPizza,
        PackIconBoxIconsKind.RegularWine,
        PackIconBoxIconsKind.RegularCookie,

        // Hobbies, Activities & Lifestyle
        PackIconBoxIconsKind.RegularPalette,
        PackIconBoxIconsKind.RegularBrush,
        PackIconBoxIconsKind.RegularMusic,
        PackIconBoxIconsKind.RegularFilm,
        PackIconBoxIconsKind.RegularJoystick,
        PackIconBoxIconsKind.RegularCamera,
        PackIconBoxIconsKind.RegularBook,
        PackIconBoxIconsKind.RegularFootball,
        PackIconBoxIconsKind.RegularBasketball,
        PackIconBoxIconsKind.RegularSwimming,

        // Celebrations & Rewards
        PackIconBoxIconsKind.RegularParty,
        PackIconBoxIconsKind.RegularGift,
        PackIconBoxIconsKind.RegularCrown,
        PackIconBoxIconsKind.RegularTrophy,
        PackIconBoxIconsKind.RegularMedal,
        PackIconBoxIconsKind.RegularHeart,
        PackIconBoxIconsKind.RegularStar,
        PackIconBoxIconsKind.RegularShield
    };

    private static readonly Dictionary<PackIconBoxIconsKind, string> IconKeywords = new()
    {
        [PackIconBoxIconsKind.RegularSlider] = "slider settings tune adjust sliders config preset",
                [PackIconBoxIconsKind.RegularCog] = "cog gear settings options system preference",
                [PackIconBoxIconsKind.RegularLayers] = "layers stack set config settings build preset",
        [PackIconBoxIconsKind.RegularFolder] = "folder directory set container",
        [PackIconBoxIconsKind.RegularFolderOpen] = "folder open directory set",
        [PackIconBoxIconsKind.RegularFolderStar] = "folder star bookmark favorite set",
        [PackIconBoxIconsKind.RegularPackage] = "package box cc mod archive item",
        [PackIconBoxIconsKind.RegularBox] = "box package storage archive",
        [PackIconBoxIconsKind.RegularGrid] = "grid gallery layout view items",
        [PackIconBoxIconsKind.RegularBookmarks] = "bookmarks favorites saved",

        // Pets
        [PackIconBoxIconsKind.RegularCat] = "cat kitten pet pets animal",
        [PackIconBoxIconsKind.RegularDog] = "dog puppy pet pets animal",
        [PackIconBoxIconsKind.RegularPawPrint] = "paw print pet pets animal dog cat",
        [PackIconBoxIconsKind.RegularBone] = "bone pet pets dog treat",
        [PackIconBoxIconsKind.RegularFish] = "fish pet pets nature water ocean aquarium",
        [PackIconBoxIconsKind.RegularBird] = "bird pet pets animal nature fly parrot",
        [PackIconBoxIconsKind.RegularBug] = "bug butterfly insect nature garden pet",

        // Nature & Occult
        [PackIconBoxIconsKind.RegularLeaf] = "leaf nature plant garden outdoor fall autumn tree",
        [PackIconBoxIconsKind.RegularFlower] = "flower nature plant garden spring rose beauty flora",
        [PackIconBoxIconsKind.RegularTree] = "tree nature plant forest wood outdoor landscaping",
        [PackIconBoxIconsKind.RegularPlantPot] = "plant pot garden flower nature indoor decor deco",
        [PackIconBoxIconsKind.RegularSun] = "sun sunny summer weather day light nature warm",
        [PackIconBoxIconsKind.RegularMoon] = "moon night occult vampire lunar supernatural dark sleep",
        [PackIconBoxIconsKind.RegularFlame] = "flame fire hot fireplace warm burn heat",
        [PackIconBoxIconsKind.RegularWater] = "water drop pool ocean bath rain liquid aqua",
        [PackIconBoxIconsKind.RegularMagicWand] = "magic wand occult witch wizard supernatural fantasy spell",
        [PackIconBoxIconsKind.RegularGhost] = "ghost spooky halloween occult spirit supernatural haunted",
        [PackIconBoxIconsKind.RegularSparkles] = "sparkles magic special shiny effects glitter star new",
        [PackIconBoxIconsKind.RegularPlanet] = "planet space alien futuristic sci-fi astronomy",

        // CAS
        [PackIconBoxIconsKind.RegularUser] = "user sim person avatar profile cas",
        [PackIconBoxIconsKind.RegularFace] = "face sim cas head makeup skin beauty",
        [PackIconBoxIconsKind.RegularHanger] = "hanger closet wardrobe clothes cas fashion outfit",
        [PackIconBoxIconsKind.RegularTShirt] = "shirt tshirt clothes cas top outfit apparel",
        [PackIconBoxIconsKind.RegularGlasses] = "glasses accessories eyewear cas shades sunglasses",
        [PackIconBoxIconsKind.RegularMask] = "mask masquerade cas makeup costume facial skin",
        [PackIconBoxIconsKind.RegularSmile] = "smile happy emotion mood traits personality",
        [PackIconBoxIconsKind.RegularDiamond] = "diamond gem plumbob crystal rare premium jewelry",

        // Home & Build
        [PackIconBoxIconsKind.RegularHome] = "home house lot build architecture residential",
        [PackIconBoxIconsKind.RegularBuilding] = "building venue community lot city commercial highrise",
        [PackIconBoxIconsKind.RegularStore] = "store shop market retail commercial boutique",
        [PackIconBoxIconsKind.RegularGlobe] = "globe world travel map earth world vacation",
        [PackIconBoxIconsKind.RegularChair] = "chair furniture seat comfort buy living dining",
        [PackIconBoxIconsKind.RegularBed] = "bed sleep furniture bedroom buy comfort",
        [PackIconBoxIconsKind.RegularBath] = "bath bathroom shower wash tub hygiene plumbing",
        [PackIconBoxIconsKind.RegularTv] = "tv television electronics screen appliance video media",
        [PackIconBoxIconsKind.RegularCar] = "car vehicle transport drive parking automobile auto",

        // Food
        [PackIconBoxIconsKind.RegularForkKnife] = "food kitchen dinner meal eat cooking recipe restaurant dish",
        [PackIconBoxIconsKind.RegularCoffee] = "coffee cup drink cafe food kitchen mug beverage morning",
        [PackIconBoxIconsKind.RegularBirthdayCake] = "cake birthday party dessert sweet food celebration bakery",
        [PackIconBoxIconsKind.RegularPizza] = "pizza food dinner fast snack slice Italian",
        [PackIconBoxIconsKind.RegularWine] = "wine drink alcohol beverage bar dinner celebration glass",
        [PackIconBoxIconsKind.RegularCookie] = "cookie snack dessert sweet biscuit baking food",

        // Hobbies & Rewards
        [PackIconBoxIconsKind.RegularPalette] = "palette color art paint draw theme design easel",
        [PackIconBoxIconsKind.RegularBrush] = "brush paint art makeup hair craft beauty",
        [PackIconBoxIconsKind.RegularMusic] = "music audio sound song radio stereo instruments band",
        [PackIconBoxIconsKind.RegularFilm] = "film movie video cinema media tv watch recording",
        [PackIconBoxIconsKind.RegularJoystick] = "game gaming joystick arcade play console toys",
        [PackIconBoxIconsKind.RegularCamera] = "camera photo photography picture snapshot image lens",
        [PackIconBoxIconsKind.RegularBook] = "book read library study novel skill novel homework",
        [PackIconBoxIconsKind.RegularFootball] = "football soccer sport sports ball fitness athletics",
        [PackIconBoxIconsKind.RegularBasketball] = "basketball sport sports ball fitness hoop game",
        [PackIconBoxIconsKind.RegularSwimming] = "swimming swim pool water fitness summer sport resort",
        [PackIconBoxIconsKind.RegularParty] = "party celebrate confetti holiday festival event fun",
        [PackIconBoxIconsKind.RegularGift] = "gift present reward box party holiday christmas",
        [PackIconBoxIconsKind.RegularCrown] = "crown king queen royal royalty luxury gold vip prestige",
        [PackIconBoxIconsKind.RegularTrophy] = "trophy award win achievement champion first tournament",
        [PackIconBoxIconsKind.RegularMedal] = "medal award win achievement prize honor sport badge",
        [PackIconBoxIconsKind.RegularHeart] = "heart love favorite romantic like romance wedding",
        [PackIconBoxIconsKind.RegularStar] = "star rating favorite sparkle highlight review celebrity",
        [PackIconBoxIconsKind.RegularShield] = "shield security protection safe verified protect"
    };

    // Preset Accent Palette
    private static readonly (string Name, string? Hex)[] AvailableColors = new[]
    {
        ("Default", (string?)null),
        ("Sky Blue", "#0ea5e9"),
        ("Indigo", "#6366f1"),
        ("Purple", "#8b5cf6"),
        ("Emerald Green", "#10b981"),
        ("Teal", "#14b8a6"),
        ("Pink", "#ec4899"),
        ("Rose", "#f43f5e"),
        ("Amber Orange", "#f59e0b"),
        ("Yellow", "#eab308"),
        ("Slate Gray", "#64748b")
    };

    public CustomizeConfigDialogWindow()
    {
        InitializeComponent();
        _configName = string.Empty;
        PopulateVisualPickers();
    }

    public CustomizeConfigDialogWindow(string currentName, string? currentDesc, string? currentIconName, string? currentColorHex, bool isDefault = false, bool isActive = false)
    {
        InitializeComponent();

        _configName = currentName ?? string.Empty;
        _description = currentDesc;
        _selectedColorHex = currentColorHex;
        _selectedIconName = currentIconName;
        _isActive = isActive;

        ConfigNameTextBox.Text = _configName;
        DescriptionTextBox.Text = _description ?? string.Empty;
        PreviewBadge.IsVisible = _isActive;

        PopulateVisualPickers();
        UpdateActiveSelections();
        UpdatePreview();
    }

    private void PopulateVisualPickers()
    {
        // 1. Populate Icons
        IconsGrid.Children.Clear();
        _allIconButtons.Clear();

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
                    Width = 16,
                    Height = 16,
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

            _allIconButtons.Add(btn);
            IconsGrid.Children.Add(btn);
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

    private void OnIconSearchChanged(object? sender, TextChangedEventArgs e)
    {
        FilterIcons(IconSearchBox.Text);
    }

    private void FilterIcons(string? query)
    {
        query = query?.Trim().ToLowerInvariant() ?? string.Empty;
        IconsGrid.Children.Clear();
        int matchCount = 0;

        foreach (var btn in _allIconButtons)
        {
            bool match = false;
            if (string.IsNullOrWhiteSpace(query))
            {
                match = true;
            }
            else
            {
                var iconTag = (string?)btn.Tag ?? string.Empty;
                if (iconTag.ToLowerInvariant().Contains(query))
                {
                    match = true;
                }
                else if (Enum.TryParse<PackIconBoxIconsKind>(iconTag, out var kind))
                {
                    if (IconKeywords.TryGetValue(kind, out var keywords) && keywords.Contains(query))
                    {
                        match = true;
                    }
                }
            }

            if (match)
            {
                IconsGrid.Children.Add(btn);
                matchCount++;
            }
        }

        if (NoIconsTextBlock != null)
        {
            NoIconsTextBlock.IsVisible = matchCount == 0;
        }
    }

    private void UpdateActiveSelections()
    {
        foreach (var btn in _allIconButtons)
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
        var displayName = string.IsNullOrWhiteSpace(ConfigNameTextBox.Text) ? "Configuration Name" : ConfigNameTextBox.Text.Trim();
        PreviewName.Text = displayName;

        var desc = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? "No description provided." : DescriptionTextBox.Text.Trim();
        PreviewDescription.Text = desc;

        // Update Icon
        if (!string.IsNullOrEmpty(_selectedIconName) && Enum.TryParse<PackIconBoxIconsKind>(_selectedIconName, out var kind))
        {
            PreviewIcon.Kind = kind;
        }
        else
        {
            PreviewIcon.Kind = PackIconBoxIconsKind.RegularSlider;
        }

        // Update Icon Color (clean, without background box)
        var iconBrush = _selectedColorHex != null ? Brush.Parse(_selectedColorHex) : ThemeService.GetCurrentAccentBrush();
        PreviewIcon.Foreground = iconBrush;
    }

    private void OnNameChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePreview();
        ValidateName();
    }

    private void OnDescriptionChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private bool ValidateName()
    {
        var text = ConfigNameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ErrorTextBlock.Text = "Configuration name cannot be empty.";
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
        _selectedColorHex = null;
        _selectedIconName = null;
        IconSearchBox.Text = string.Empty;
        UpdateActiveSelections();
        UpdatePreview();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (!ValidateName()) return;
        Close(new CustomizeConfigResult(true, ResultConfigName, ResultDescription, ResultIconName, ResultColorHex));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new CustomizeConfigResult(false, _configName, _description, _selectedIconName, _selectedColorHex));
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}

public record CustomizeConfigResult(bool Confirmed, string Name, string? Description, string? Icon, string? Color);
