using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using IconPacks.Avalonia.BoxIcons;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.Views.Dialogs;

public record RetagDialogInput(
    string PackageType,
    string? UserTags,
    string? CasCategories,
    string? CasAge = null,
    string? CasGender = null,
    string? CasOutfit = null,
    string? BBKind = null,
    string? BBRooms = null,
    string? BBFunctions = null);

public record RetagDialogResult(
    string PackageType,
    string UserTags,
    string CasCategories,
    string? CasAge = null,
    string? CasGender = null,
    string? CasOutfit = null,
    string? BBKind = null,
    string? BBRooms = null,
    string? BBFunctions = null);

public partial class RetagDialogWindow : Window
{
    private static readonly char[] Delimiters = { ',', ';' };

    public ObservableCollection<string> Tags { get; } = new();
    private string _selectedType = "CAS";
    private string? _selectedCategory;
    private bool _advancedOpen;

    private PillGroup? _bbKinds, _ages, _genders, _outfits, _rooms, _functions;

    public RetagDialogWindow()
    {
        InitializeComponent();
        UserTagsItemsControl.ItemsSource = Tags;

        // Attach tunneling KeyDown event to intercept Backspace before TextBox swallows it
        TagInputTextBox.AddHandler(InputElement.KeyDownEvent, OnTagInputKeyDown, RoutingStrategies.Tunnel);
    }

    public RetagDialogWindow(RetagDialogInput input) : this()
    {
        // Populate Tags
        if (!string.IsNullOrWhiteSpace(input.UserTags))
        {
            foreach (var tag in input.UserTags.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = tag.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !Tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    Tags.Add(trimmed);
                }
            }
        }

        _bbKinds = new PillGroup(BuildBuyCategoriesPanel, string.IsNullOrEmpty(input.BBKind) ? "Objects" : input.BBKind, new (string, string, PackIconBoxIconsKind?)[]
        {
            ("Objects", "filters.bb_kinds.Objects", PackIconBoxIconsKind.RegularDesk),
            ("DoorsWindows", "filters.bb_kinds.DoorsWindows", PackIconBoxIconsKind.RegularDoorOpen),
            ("Landscaping", "filters.bb_kinds.Landscaping", PackIconBoxIconsKind.RegularTree),
            ("WallsFloors", "filters.bb_kinds.WallsFloors", PackIconBoxIconsKind.RegularBrick),
            ("Terrain", "filters.bb_kinds.Terrain", PackIconBoxIconsKind.RegularMountainView),
            ("FencesRailings", "filters.bb_kinds.FencesRailings", PackIconBoxIconsKind.RegularTrafficBarrier),
            ("Stairs", "filters.bb_kinds.Stairs", PackIconBoxIconsKind.RegularStepsUp),
            ("Roofs", "filters.bb_kinds.Roofs", PackIconBoxIconsKind.RegularBuildingHouse),
            ("OtherBuild", "filters.bb_kinds.OtherBuild", PackIconBoxIconsKind.RegularHardHat),
        });
        _ages = new PillGroup(AgePanel, input.CasAge,
            new[] { "Baby", "Toddler", "Child", "Teen", "YoungAdult", "Adult", "Elder" }.Select(a => (a, $"filters.ages.{a}", (PackIconBoxIconsKind?)null)));
        _genders = new PillGroup(GenderPanel, input.CasGender,
            new[] { "Male", "Female" }.Select(g => (g, $"filters.genders.{g}", (PackIconBoxIconsKind?)null)));
        _outfits = new PillGroup(OutfitPanel, input.CasOutfit,
            new[] { "Everyday", "Formal", "Sleepwear", "Swimwear", "Athletic", "Career", "Outerwear" }.Select(o => (o, $"filters.outfits.{o}", (PackIconBoxIconsKind?)null)));
        _rooms = new PillGroup(RoomPanel, input.BBRooms, new (string, string, PackIconBoxIconsKind?)[]
        {
            ("Living", "filters.bb_rooms.Living", PackIconBoxIconsKind.RegularTv),
            ("Dining", "filters.bb_rooms.Dining", PackIconBoxIconsKind.RegularForkKnife),
            ("Kitchen", "filters.bb_rooms.Kitchen", PackIconBoxIconsKind.RegularOven),
            ("Nursery", "filters.bb_rooms.Nursery", PackIconBoxIconsKind.RegularChild),
            ("Bathroom", "filters.bb_rooms.Bathroom", PackIconBoxIconsKind.RegularBath),
            ("Laundry", "filters.bb_rooms.Laundry", PackIconBoxIconsKind.RegularWasher),
            ("Bedroom", "filters.bb_rooms.Bedroom", PackIconBoxIconsKind.RegularBed),
            ("Study", "filters.bb_rooms.Study", PackIconBoxIconsKind.RegularBookOpen),
            ("Outdoor", "filters.bb_rooms.Outdoor", PackIconBoxIconsKind.RegularSun),
            ("Community", "filters.bb_rooms.Community", PackIconBoxIconsKind.RegularBuildings),
            ("Resort", "filters.bb_rooms.Resort", PackIconBoxIconsKind.RegularBeach),
        });
        _functions = new PillGroup(FunctionPanel, input.BBFunctions, new (string, string, PackIconBoxIconsKind?)[]
        {
            ("Appliances", "filters.bb_functions.Appliances", PackIconBoxIconsKind.RegularFridge),
            ("Electronics", "filters.bb_functions.Electronics", PackIconBoxIconsKind.RegularComputer),
            ("Entertainment", "filters.bb_functions.Entertainment", PackIconBoxIconsKind.RegularJoystick),
            ("Lighting", "filters.bb_functions.Lighting", PackIconBoxIconsKind.RegularLightBulb),
            ("Plumbing", "filters.bb_functions.Plumbing", PackIconBoxIconsKind.RegularShower),
            ("Decor", "filters.bb_functions.Decor", PackIconBoxIconsKind.RegularFrame),
            ("Kids", "filters.bb_functions.Kids", PackIconBoxIconsKind.RegularToyCar),
            ("Storage", "filters.bb_functions.Storage", PackIconBoxIconsKind.RegularCabinet),
            ("Comfort", "filters.bb_functions.Comfort", PackIconBoxIconsKind.RegularBench),
            ("Surfaces", "filters.bb_functions.Surfaces", PackIconBoxIconsKind.RegularTable),
            ("Vehicles", "filters.bb_functions.Vehicles", PackIconBoxIconsKind.RegularCar),
            ("Pets", "filters.bb_functions.Pets", PackIconBoxIconsKind.RegularPawPrint),
            ("Misc", "filters.bb_functions.Misc", PackIconBoxIconsKind.RegularShapes),
        });

        // Set Type (CAS, BuildBuy, Other)
        var currentPackageType = input.PackageType;
        if (string.Equals(currentPackageType, "BuildBuy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentPackageType, "Build-Buy", StringComparison.OrdinalIgnoreCase))
        {
            SetPackageType("BuildBuy");
        }
        else if (string.Equals(currentPackageType, "Other", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(currentPackageType, "World", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(currentPackageType, "Sim", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(currentPackageType, "Lot", StringComparison.OrdinalIgnoreCase))
        {
            SetPackageType("Other");
        }
        else
        {
            SetPackageType("CAS");
        }

        // Set Category
        _selectedCategory = input.CasCategories?.Trim();
        UpdateCategorySelection();
    }

    private void SetPackageType(string type)
    {
        _selectedType = type;
        TypeCasButton.IsChecked = (type == "CAS");
        TypeBuildBuyButton.IsChecked = (type == "BuildBuy");
        TypeOtherButton.IsChecked = (type == "Other");

        CasCategoriesPanel.IsVisible = (type == "CAS");
        BuildBuyCategoriesPanel.IsVisible = (type == "BuildBuy");
        OtherCategoriesPanel.IsVisible = (type == "Other");
        UpdateAdvancedVisibility();
    }

    private void UpdateAdvancedVisibility()
    {
        AdvancedSection.IsVisible = _selectedType is "CAS" or "BuildBuy";
        AdvancedChevron.Kind = _advancedOpen ? PackIconBoxIconsKind.RegularChevronDown : PackIconBoxIconsKind.RegularChevronRight;
        AdvancedCasPanel.IsVisible = _advancedOpen && _selectedType == "CAS";
        AdvancedBuildBuyPanel.IsVisible = _advancedOpen && _selectedType == "BuildBuy";
    }

    private void OnAdvancedToggleClick(object? sender, RoutedEventArgs e)
    {
        _advancedOpen = !_advancedOpen;
        UpdateAdvancedVisibility();
    }

    private void OnTypeCasClick(object? sender, RoutedEventArgs e) => SetPackageType("CAS");
    private void OnTypeBuildBuyClick(object? sender, RoutedEventArgs e) => SetPackageType("BuildBuy");
    private void OnTypeOtherClick(object? sender, RoutedEventArgs e) => SetPackageType("Other");

    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.Tag is string catName)
        {
            _selectedCategory = catName;
            UpdateCategorySelection();
        }
    }

    private void UpdateCategorySelection()
    {
        foreach (var panel in new[] { CasCategoriesPanel, OtherCategoriesPanel })
        {
            foreach (var child in panel.Children)
            {
                if (child is ToggleButton tb)
                {
                    tb.IsChecked = string.Equals(tb.Tag?.ToString(), _selectedCategory, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    private sealed class PillGroup
    {
        private readonly HashSet<string> _selected;
        private readonly List<string> _order = new();
        public bool Touched { get; private set; }

        public PillGroup(WrapPanel panel, string? current, IEnumerable<(string Token, string LabelKey, PackIconBoxIconsKind? Icon)> options)
        {
            _selected = new HashSet<string>(
                (current ?? string.Empty).Split(Delimiters, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (token, labelKey, icon) in options)
            {
                _order.Add(token);
                var label = new TextBlock { Text = LocalizationManager.Instance.GetString(labelKey), VerticalAlignment = VerticalAlignment.Center };
                object content = label;
                if (icon is { } kind)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                    row.Children.Add(new PackIconBoxIcons { Kind = kind, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(label);
                    content = row;
                }
                var pill = new ToggleButton { Content = content, IsChecked = _selected.Contains(token) };
                pill.Classes.Add("filter-pill");
                pill.IsCheckedChanged += (_, _) =>
                {
                    if (pill.IsChecked == true) _selected.Add(token); else _selected.Remove(token);
                    Touched = true;
                };
                panel.Children.Add(pill);
            }
        }

        public string? Result
        {
            get
            {
                if (!Touched) return null;
                // Values the dialog does not offer (from a scan) are kept, after the offered ones.
                var known = _order.Where(_selected.Contains);
                var unknown = _selected.Where(s => !_order.Contains(s, StringComparer.OrdinalIgnoreCase));
                return string.Join(",", known.Concat(unknown));
            }
        }
    }

    #region Tag Input Logic
    private void OnTagInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitCurrentTagInput();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            if (string.IsNullOrEmpty(TagInputTextBox.Text) && Tags.Count > 0)
            {
                Tags.RemoveAt(Tags.Count - 1);
                e.Handled = true;
            }
        }
    }

    private void OnTagInputTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text == ",")
        {
            CommitCurrentTagInput();
            e.Handled = true;
        }
    }

    private void CommitCurrentTagInput()
    {
        var text = TagInputTextBox.Text?.Trim(',', ' ');
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!Tags.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                Tags.Add(text);
            }
            TagInputTextBox.Text = string.Empty;
        }
    }

    private void OnRemoveTagClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string tag)
        {
            Tags.Remove(tag);
        }
    }
    #endregion

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        CommitCurrentTagInput();

        string userTags = string.Join(", ", Tags);
        string casCategory = _selectedCategory ?? string.Empty;
        bool cas = _selectedType == "CAS";
        bool bb = _selectedType == "BuildBuy";

        Close(new RetagDialogResult(_selectedType, userTags, casCategory,
            CasAge: cas ? _ages?.Result : null,
            CasGender: cas ? _genders?.Result : null,
            CasOutfit: cas ? _outfits?.Result : null,
            BBKind: bb ? _bbKinds?.Result : null,
            BBRooms: bb ? _rooms?.Result : null,
            BBFunctions: bb ? _functions?.Result : null));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
