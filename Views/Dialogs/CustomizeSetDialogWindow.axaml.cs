using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using IconPacks.Avalonia.BoxIcons;
using PlumbobForge.Desktop.Services;
using PlumbobForge.Desktop.Services.Localization;
using PlumbobForge.Desktop.Views.Controls;

namespace PlumbobForge.Desktop.Views.Dialogs;

public partial class CustomizeSetDialogWindow : Window
{
    private string _setName;
    private string? _selectedColorHex;
    private string? _selectedIconName;

    private readonly List<Button> _allIconButtons = new();
    private readonly List<Button> _colorButtons = new();
    private Button? _rainbowWheelButton;
    private PlumbobColorPicker? _flyoutColorPicker;

    public string ResultSetName => SetNameTextBox.Text?.Trim() ?? _setName;
    public string? ResultColorHex => _selectedColorHex;
    public string? ResultIconName => _selectedIconName;

    // Curated Sims 3 CC / Set Icons
    private static readonly List<PackIconBoxIconsKind> AvailableIcons = new()
    {
        // Sets, Folders, System
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

        // CAS, Sims, Clothing & Accessories
        PackIconBoxIconsKind.RegularUser,
        PackIconBoxIconsKind.RegularFace,
        PackIconBoxIconsKind.RegularFaceAlt,
        PackIconBoxIconsKind.RegularBody,
        PackIconBoxIconsKind.RegularHanger,
        PackIconBoxIconsKind.RegularTShirt,
        PackIconBoxIconsKind.RegularPant,
        PackIconBoxIconsKind.RegularSneaker,
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
        PackIconBoxIconsKind.RegularShoppingBag,
        PackIconBoxIconsKind.RegularSlider
    };

    private static readonly Dictionary<PackIconBoxIconsKind, string> IconKeywords = new()
    {
        [PackIconBoxIconsKind.RegularFolder] = "folder directory set container",
        [PackIconBoxIconsKind.RegularFolderOpen] = "folder open directory set",
        [PackIconBoxIconsKind.RegularFolderHeart] = "folder heart love favorite set",
        [PackIconBoxIconsKind.RegularFolderStar] = "folder star bookmark favorite set",
        [PackIconBoxIconsKind.RegularFolderCheck] = "folder check done ready set",
        [PackIconBoxIconsKind.RegularPackage] = "package box cc mod archive item",
        [PackIconBoxIconsKind.RegularBox] = "box package storage archive",
        [PackIconBoxIconsKind.RegularLayers] = "layers stack set config settings build",
        [PackIconBoxIconsKind.RegularGrid] = "grid gallery layout view items",
        [PackIconBoxIconsKind.RegularTag] = "tag label price category",
        [PackIconBoxIconsKind.RegularBookmark] = "bookmark favorite saved ribbon",
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
        [PackIconBoxIconsKind.RegularFaceAlt] = "face sim cas head expression",
        [PackIconBoxIconsKind.RegularBody] = "body sim cas figure outfit clothes skin",
        [PackIconBoxIconsKind.RegularHanger] = "hanger closet wardrobe clothes cas fashion outfit",
        [PackIconBoxIconsKind.RegularTShirt] = "shirt tshirt clothes cas top outfit apparel",
        [PackIconBoxIconsKind.RegularPant] = "pants jeans trousers bottom clothes cas bottoms",
        [PackIconBoxIconsKind.RegularSneaker] = "sneaker shoes footwear boots cas kicks",
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
        [PackIconBoxIconsKind.RegularShoppingBag] = "shop shopping bag store buy cart clothes retail",
        [PackIconBoxIconsKind.RegularSlider] = "slider settings tune adjust sliders config"
    };

    // Built-in Curated Color Palette
    private static readonly (string Name, string? Hex)[] AvailableColors = new[]
    {
        ("Default", (string?)null),
        ("Emerald Green", "#10b981"),
        ("Teal", "#14b8a6"),
        ("Sky Blue", "#0ea5e9"),
        ("Indigo", "#6366f1"),
        ("Purple", "#8b5cf6"),
        ("Pink", "#ec4899"),
        ("Rose", "#f43f5e"),
        ("Amber Orange", "#f59e0b"),
        ("Yellow", "#eab308"),
        ("Slate Gray", "#64748b")
    };

    public CustomizeSetDialogWindow()
    {
        InitializeComponent();
        _setName = string.Empty;
        PopulateIcons();
        PopulateColorPickers();
        CustomColorPresetsService.PresetsChanged += OnPresetsChanged;
        Closed += (s, e) => CustomColorPresetsService.PresetsChanged -= OnPresetsChanged;
    }

    public CustomizeSetDialogWindow(string currentName, string? currentIconName, string? currentColorHex)
    {
        InitializeComponent();

        _setName = currentName ?? string.Empty;
        _selectedColorHex = currentColorHex;
        _selectedIconName = currentIconName;

        SetNameTextBox.Text = _setName;
        PopulateIcons();
        PopulateColorPickers();
        CustomColorPresetsService.PresetsChanged += OnPresetsChanged;
        Closed += (s, e) => CustomColorPresetsService.PresetsChanged -= OnPresetsChanged;

        UpdateActiveSelections();
        UpdatePreview();
    }

    private void OnPresetsChanged()
    {
        PopulateColorPickers();
        UpdatePreview();
    }

    private void PopulateIcons()
    {
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
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
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
    }

    private void PopulateColorPickers()
    {
        ColorsWrapPanel.Children.Clear();
        _colorButtons.Clear();

        // 1. Built-in Preset Swatches
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

        // 2. Saved Custom Presets (with SolidStar indicator & Right-Click ContextMenu)
        foreach (var presetHex in CustomColorPresetsService.Presets)
        {
            var hexCopy = presetHex;
            var btn = new Button
            {
                Classes = { "color-pick-btn" },
                Tag = hexCopy,
                Background = Brush.Parse(hexCopy),
                Content = new PackIconBoxIcons
                {
                    Kind = PackIconBoxIconsKind.SolidStar,
                    Width = 9,
                    Height = 9,
                    Foreground = Brushes.White,
                    Opacity = 0.85,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(btn, $"{hexCopy} ({LocalizationManager.Instance.GetString("dialogs.customize_set.custom_preset_tip")})");

            var menu = new ContextMenu();
            var deleteItem = new MenuItem
            {
                Header = LocalizationManager.Instance.GetString("dialogs.customize_set.delete_preset"),
                Icon = new PackIconBoxIcons { Kind = PackIconBoxIconsKind.RegularTrash, Width = 14, Height = 14 }
            };
            deleteItem.Click += (s, e) =>
            {
                CustomColorPresetsService.RemovePreset(hexCopy);
            };
            menu.Items.Add(deleteItem);
            btn.ContextMenu = menu;

            btn.Click += (s, e) =>
            {
                _selectedColorHex = hexCopy;
                UpdateActiveSelections();
                UpdatePreview();
            };

            _colorButtons.Add(btn);
            ColorsWrapPanel.Children.Add(btn);
        }

        // 3. Rainbow Color Wheel / Sphere Button with Spectrum Picker Flyout
        _rainbowWheelButton = new Button
        {
            Classes = { "color-pick-btn" },
            Tag = "__CUSTOM_WHEEL__",
            Background = CustomColorPresetsService.RainbowWheelBrush
        };
        ToolTip.SetTip(_rainbowWheelButton, LocalizationManager.Instance.GetString("dialogs.customize_set.pick_custom_color"));

        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight
        };

        var initialColor = ColorPaletteGenerator.TryParseHex(_selectedColorHex, out var parsedCol)
            ? parsedCol
            : Color.Parse("#10b981");

        _flyoutColorPicker = new PlumbobColorPicker
        {
            Color = initialColor,
            IsSavePresetVisible = true
        };

        _flyoutColorPicker.ColorChanged += (s, e) =>
        {
            var hex = ColorPaletteGenerator.ColorToHex(e.NewColor);
            _selectedColorHex = hex;
            _rainbowWheelButton.Background = new SolidColorBrush(e.NewColor);
            UpdateActiveSelections();
            UpdatePreview();
        };

        _flyoutColorPicker.SavePresetRequested += (s, col) =>
        {
            var hex = ColorPaletteGenerator.ColorToHex(col);
            CustomColorPresetsService.AddPreset(hex);
            flyout.Hide();
        };

        flyout.Content = new Border
        {
            Background = (IBrush)this.FindResource("BgSurface")!,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = _flyoutColorPicker
        };

        _rainbowWheelButton.Flyout = flyout;

        _rainbowWheelButton.Click += (s, e) =>
        {
            if (ColorPaletteGenerator.TryParseHex(_selectedColorHex, out var activeCol) && _flyoutColorPicker != null)
            {
                _flyoutColorPicker.Color = activeCol;
            }
        };

        _colorButtons.Add(_rainbowWheelButton);
        ColorsWrapPanel.Children.Add(_rainbowWheelButton);

        UpdateActiveSelections();
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

        bool matchedBuiltInOrPreset = false;
        foreach (var btn in _colorButtons)
        {
            if (ReferenceEquals(btn, _rainbowWheelButton)) continue;

            bool isSel = string.Equals((string?)btn.Tag, _selectedColorHex, StringComparison.OrdinalIgnoreCase);
            if (isSel)
            {
                matchedBuiltInOrPreset = true;
                if (!btn.Classes.Contains("selected")) btn.Classes.Add("selected");
            }
            else
            {
                btn.Classes.Remove("selected");
            }
        }

        if (_rainbowWheelButton != null)
        {
            bool isCustomActive = !string.IsNullOrEmpty(_selectedColorHex) && !matchedBuiltInOrPreset;
            if (isCustomActive && ColorPaletteGenerator.TryParseHex(_selectedColorHex, out var customColor))
            {
                _rainbowWheelButton.Background = new SolidColorBrush(customColor);
                if (!_rainbowWheelButton.Classes.Contains("selected")) _rainbowWheelButton.Classes.Add("selected");
            }
            else
            {
                _rainbowWheelButton.Background = CustomColorPresetsService.RainbowWheelBrush;
                _rainbowWheelButton.Classes.Remove("selected");
            }
        }
    }

    private void UpdatePreview()
    {
        var displayName = string.IsNullOrWhiteSpace(SetNameTextBox.Text) ? "Set Name" : SetNameTextBox.Text.Trim();
        PreviewName.Text = displayName;

        // Update Icon
        if (!string.IsNullOrEmpty(_selectedIconName) && Enum.TryParse<PackIconBoxIconsKind>(_selectedIconName, out var kind))
        {
            PreviewIcon.Kind = kind;
        }
        else
        {
            PreviewIcon.Kind = PackIconBoxIconsKind.RegularFolder;
        }

        // Update Icon Color (clean, without background box)
        var iconBrush = _selectedColorHex != null ? Brush.Parse(_selectedColorHex) : ThemeService.GetCurrentAccentBrush();
        PreviewIcon.Foreground = iconBrush;
    }

    private void OnSetNameChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePreview();
        ValidateName();
    }

    private bool ValidateName()
    {
        var text = SetNameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ErrorTextBlock.Text = "Set name cannot be empty.";
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
        Close(new CustomizeSetResult(true, ResultSetName, ResultIconName, ResultColorHex));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new CustomizeSetResult(false, _setName, _selectedIconName, _selectedColorHex));
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}

public record CustomizeSetResult(bool Confirmed, string Name, string? Icon, string? Color);
