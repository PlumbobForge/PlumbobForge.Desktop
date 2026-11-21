using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views;

public partial class ConfigurationsView : UserControl
{
    private Point? _dragStartPos;
    private ConfigColumnEntry? _dragCandidate;
    private bool _pendingSelectionNarrow = false;
    private Point? _configDragStart;
    private ConfigItemViewModel? _configDragCandidate;

    private const double DragThreshold = 4;
    private const double RowGapMiddle = 2;
    private const double AutoScrollEdge = 28;
    private const double AutoScrollStep = 10;

    // Rubberband Drag Selection State
    private bool _isRubberbanding = false;
    private Point _rubberbandStartPoint;
    private Panel? _activeRubberbandHost;
    private Border? _activeRubberbandBorder;
    private ItemsControl? _activeItemsControl;
    private bool _isRubberbandForEnabled = false;
    private KeyModifiers _rubberbandModifiers;
    private readonly HashSet<ConfigColumnEntry> _initialSelectedBeforeDrag = new();

    public ConfigurationsView()
    {
        InitializeComponent();

        // Crossing onto another element raises Leave then Enter, not Over, so Enter has to keep the line up.
        AddHandler(DragDrop.DragEnterEvent, OnDropZoneDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDropZoneDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDropZoneDragLeave);
        AddHandler(DragDrop.DropEvent, OnDropZoneDrop);

        // Attach pointer events for rubberband drag selection on both list hosts
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is ConfigurationsViewModel vm)
            {
                _ = vm.LoadDataAsync();
            }

            if (EnabledListHost != null)
            {
                EnabledListHost.PointerPressed += (s, e) => OnListHostPointerPressed(s, e, isEnabled: true);
                EnabledListHost.PointerMoved += OnListHostPointerMoved;
                EnabledListHost.PointerReleased += OnListHostPointerReleased;
            }

            if (DisabledListHost != null)
            {
                DisabledListHost.PointerPressed += (s, e) => OnListHostPointerPressed(s, e, isEnabled: false);
                DisabledListHost.PointerMoved += OnListHostPointerMoved;
                DisabledListHost.PointerReleased += OnListHostPointerReleased;
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ConfigurationsViewModel vm && VisualRoot != null)
            {
                _ = vm.LoadDataAsync();
            }
        };
    }

    private void OnSetActiveConfigClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConfigItemViewModel item)
        {
            var vm = (DataContext as ConfigurationsViewModel) ?? App.Services?.GetService<ConfigurationsViewModel>();
            if (vm != null) _ = vm.ActivateConfigurationAsync(item);
        }
    }

    private void OnEditConfigClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConfigItemViewModel item)
        {
            var vm = (DataContext as ConfigurationsViewModel) ?? App.Services?.GetService<ConfigurationsViewModel>();
            if (vm != null) _ = vm.CustomizeConfigurationAsync(item);
        }
    }

    private void OnDuplicateConfigClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConfigItemViewModel item)
        {
            var vm = (DataContext as ConfigurationsViewModel) ?? App.Services?.GetService<ConfigurationsViewModel>();
            if (vm != null) _ = vm.DuplicateConfigurationAsync(item);
        }
    }

    private void OnDeleteConfigClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConfigItemViewModel item)
        {
            var vm = (DataContext as ConfigurationsViewModel) ?? App.Services?.GetService<ConfigurationsViewModel>();
            if (vm != null) _ = vm.DeleteConfigurationAsync(item);
        }
    }

    private static bool IsInteractiveControl(object? source)
    {
        if (source is Visual v)
        {
            var p = v;
            while (p != null)
            {
                if (p is Button or TextBox or Avalonia.Controls.ContextMenu or MenuItem) return true;
                p = p.GetVisualParent();
            }
        }
        return false;
    }

    private static bool IsSourceInsideSetCard(object? source)
    {
        if (source is Visual v)
        {
            var card = v.FindAncestorOfType<Border>(includeSelf: true);
            while (card != null)
            {
                if (card.Classes.Contains("config-set-card")) return true;
                card = card.GetVisualParent()?.FindAncestorOfType<Border>(includeSelf: true);
            }
        }
        return false;
    }

    #region Configuration Sidebar Selection
    // Selecting activates a configuration, so that waits for the release: dragging a row must not switch to it.
    private void OnConfigItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ConfigItemViewModel config)
        {
            var point = e.GetCurrentPoint(border);
            if (point.Properties.IsLeftButtonPressed)
            {
                _configDragStart = point.Position;
                _configDragCandidate = config;
            }
        }
    }

    private async void OnConfigItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_configDragStart is not { } start || _configDragCandidate is not { } candidate || sender is not Border border) return;

        var point = e.GetCurrentPoint(border);
        if (!point.Properties.IsLeftButtonPressed) return;

        var delta = point.Position - start;
        if (Math.Abs(delta.X) <= DragThreshold && Math.Abs(delta.Y) <= DragThreshold) return;

        _configDragStart = null;
        _configDragCandidate = null;
        await AppDragData.StartAsync(AppDragData.Create(AppDragData.ConfigOrder, candidate.Id.ToString()), DragDropEffects.Move);
        ClearConfigDrop();
    }

    private void OnConfigItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_configDragCandidate is { } config && DataContext is ConfigurationsViewModel vm)
        {
            _ = vm.SelectConfiguration(config);
        }
        _configDragStart = null;
        _configDragCandidate = null;
    }

    private void OnConfigItemPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _configDragStart = null;
        _configDragCandidate = null;
    }
    #endregion

    #region Drop Lines
    private readonly record struct ConfigDropSpot(long? BeforeId, double LineX, double LineY);
    private readonly record struct SetDropSpot(long? BeforeId, double LineX, double LineY);

    private static List<(Border Row, Point Origin)> RowsIn(Control list, string rowClass, Visual relativeTo)
    {
        var rows = new List<(Border, Point)>();
        foreach (var b in list.GetVisualDescendants().OfType<Border>())
        {
            if (b.Classes.Contains(rowClass) && b.IsEffectivelyVisible && b.TranslatePoint(new Point(0, 0), relativeTo) is { } origin)
            {
                rows.Add((b, origin));
            }
        }
        return rows;
    }

    private ConfigDropSpot? FindConfigDropSpot(DragEventArgs e)
    {
        var rows = RowsIn(ConfigItemsControl, "config-node-row", ConfigListHost);
        if (rows.Count == 0) return null;

        double y = e.GetPosition(ConfigListHost).Y;
        int index = rows.FindIndex(r => y <= r.Origin.Y + r.Row.Bounds.Height);
        if (index < 0)
        {
            var last = rows[^1];
            return new ConfigDropSpot(null, last.Origin.X, last.Origin.Y + last.Row.Bounds.Height + RowGapMiddle);
        }

        var (row, top) = rows[index];
        bool upperHalf = y < top.Y + row.Bounds.Height / 2;
        if (upperHalf) return new ConfigDropSpot(((ConfigItemViewModel)row.DataContext!).Id, top.X, top.Y - RowGapMiddle);

        long? next = index + 1 < rows.Count ? ((ConfigItemViewModel)rows[index + 1].Row.DataContext!).Id : null;
        return new ConfigDropSpot(next, top.X, top.Y + row.Bounds.Height + RowGapMiddle);
    }

    private void UpdateConfigDrop(DragEventArgs e)
    {
        e.Handled = true;
        if (FindConfigDropSpot(e) is not { } spot || !IsPointerOverControl(ConfigListHost, e.GetPosition(this)))
        {
            e.DragEffects = DragDropEffects.None;
            ClearConfigDrop();
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        ShowDropLine(ConfigDropLine, ConfigListHost, spot.LineX, spot.LineY);
        AutoScroll(ConfigScrollViewer, ConfigListHost, e.GetPosition(ConfigListHost).Y);
    }

    private void ClearConfigDrop() => ConfigDropLine.IsVisible = false;

    // A set moves among its siblings only, so the line snaps to their level wherever the pointer is.
    private SetDropSpot? FindSetDropSpot(DragEventArgs e, ConfigDragPayload payload)
    {
        var host = payload.SourceIsEnabled ? EnabledListHost : DisabledListHost;
        var list = payload.SourceIsEnabled ? EnabledItemsControl : DisabledItemsControl;
        // Item cards share the row style but are not part of the tree.
        var rows = RowsIn(list, "config-set-card", host).Where(r => r.Row.DataContext is ConfigSetItemViewModel).ToList();
        var shown = rows.Select(r => (ConfigSetItemViewModel)r.Row.DataContext!).ToList();

        var moving = payload.OrderIds.ToHashSet();
        var movingRows = shown.Where(r => moving.Contains(r.Id)).ToList();
        if (movingRows.Count == 0) return null;
        long? parent = movingRows[0].ParentSetsEntityId;
        if (movingRows.Any(r => r.ParentSetsEntityId != parent)) return null;

        double y = e.GetPosition(host).Y;
        int hovered = rows.FindIndex(r => y <= r.Origin.Y + r.Row.Bounds.Height);
        bool pastEnd = hovered < 0;
        if (pastEnd) hovered = rows.Count - 1;

        // Walk up from the row under the pointer to the sibling that holds it.
        int anchor = hovered;
        while (shown[anchor].ParentSetsEntityId != parent)
        {
            int depth = shown[anchor].Depth;
            anchor--;
            while (anchor >= 0 && shown[anchor].Depth >= depth) anchor--;
            if (anchor < 0) return null;
        }
        if (moving.Contains(shown[anchor].Id)) return null;

        var (row, top) = rows[hovered];
        bool before = anchor == hovered && !pastEnd && y < top.Y + row.Bounds.Height / 2;
        if (before) return new SetDropSpot(shown[anchor].Id, rows[anchor].Origin.X, rows[anchor].Origin.Y - RowGapMiddle);

        // After the sibling and everything open under it.
        int end = anchor + 1;
        while (end < rows.Count && shown[end].Depth > shown[anchor].Depth) end++;
        var nextSibling = shown.Skip(end).FirstOrDefault(r => r.ParentSetsEntityId == parent && !moving.Contains(r.Id));
        var lastRow = rows[end - 1];
        return new SetDropSpot(nextSibling?.Id, rows[anchor].Origin.X, lastRow.Origin.Y + lastRow.Row.Bounds.Height + RowGapMiddle);
    }

    private void UpdateSetDrop(DragEventArgs e, ConfigDragPayload payload)
    {
        var line = payload.SourceIsEnabled ? EnabledDropLine : DisabledDropLine;
        var host = payload.SourceIsEnabled ? EnabledListHost : DisabledListHost;
        if (FindSetDropSpot(e, payload) is { } spot)
        {
            e.DragEffects = DragDropEffects.Move;
            ShowDropLine(line, host, spot.LineX, spot.LineY);
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            line.IsVisible = false;
        }
        e.Handled = true;
        AutoScroll(payload.SourceIsEnabled ? EnabledScrollViewer : DisabledScrollViewer, host, e.GetPosition(host).Y);
    }

    private void ClearSetDrop()
    {
        EnabledDropLine.IsVisible = false;
        DisabledDropLine.IsVisible = false;
    }

    private static void ShowDropLine(Panel line, Control host, double x, double y)
    {
        Canvas.SetLeft(line, x);
        Canvas.SetTop(line, y - line.Height / 2);
        line.Width = Math.Max(0, host.Bounds.Width - x - 6);
        line.IsVisible = true;
    }

    private static void AutoScroll(ScrollViewer scroller, Control host, double y)
    {
        double step = y < AutoScrollEdge ? -AutoScrollStep : y > host.Bounds.Height - AutoScrollEdge ? AutoScrollStep : 0;
        if (step == 0) return;
        double max = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        scroller.Offset = scroller.Offset.WithY(Math.Clamp(scroller.Offset.Y + step, 0, max));
    }
    #endregion

    #region Set & Item Card Pointer & Drag-Drop Handling
    private void OnSetCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInteractiveControl(e.Source)) return;

        if (sender is Border border && border.DataContext is ConfigColumnEntry set && DataContext is ConfigurationsViewModel vm)
        {
            var point = e.GetCurrentPoint(border);
            if (point.Properties.IsLeftButtonPressed)
            {
                _dragStartPos = point.Position;
                _dragCandidate = set;

                // A group row isn't in this column, so it can't be selected; it can still be dragged.
                if (set is ConfigSetItemViewModel { IsGroupOnly: true })
                {
                    _pendingSelectionNarrow = false;
                    e.Handled = true;
                    return;
                }

                bool isCtrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
                bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

                if (isCtrl || isShift)
                {
                    vm.SelectSetCard(set, isCtrl, isShift);
                    _pendingSelectionNarrow = false;
                }
                else
                {
                    if (set.IsSelected)
                    {
                        var col = set.IsInEnabledColumn ? vm.EnabledSets : vm.DisabledSets;
                        if (col.Count(s => s.IsSelected) > 1)
                        {
                            // Defer narrowing selection until release if no drag happens
                            _pendingSelectionNarrow = true;
                        }
                        else
                        {
                            _pendingSelectionNarrow = false;
                        }
                    }
                    else
                    {
                        vm.SelectSetCard(set, isCtrlPressed: false, isShiftPressed: false);
                        _pendingSelectionNarrow = false;
                    }
                }

                e.Handled = true;
            }
        }
    }

    private async void OnSetCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (IsInteractiveControl(e.Source)) return;

        if (_dragStartPos.HasValue && _dragCandidate != null && sender is Border border && border.DataContext is ConfigColumnEntry)
        {
            var point = e.GetCurrentPoint(border);
            if (point.Properties.IsLeftButtonPressed)
            {
                var delta = point.Position - _dragStartPos.Value;
                if (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4)
                {
                    _pendingSelectionNarrow = false;
                    _dragStartPos = null;
                    var candidate = _dragCandidate;
                    _dragCandidate = null;

                    if (DataContext is ConfigurationsViewModel vm)
                    {
                        var collection = candidate.IsInEnabledColumn ? vm.EnabledSets : vm.DisabledSets;
                        // A group row stands for its sub-sets in this column; it is never selected itself.
                        var groupRow = candidate is ConfigSetItemViewModel { IsGroupOnly: true } g ? g : null;
                        var dragged = candidate.IsSelected
                            ? collection.Where(s => s.IsSelected).ToList()
                            : new List<ConfigColumnEntry> { candidate };
                        var draggedSetIds = dragged.OfType<ConfigSetItemViewModel>().Select(s => s.Id).ToList();

                        var dragPayload = new ConfigDragPayload
                        {
                            SourceIsEnabled = candidate.IsInEnabledColumn,
                            SetIds = groupRow != null ? vm.MembersUnder(groupRow) : draggedSetIds,
                            ItemIds = groupRow != null ? new List<long>() : dragged.OfType<ConfigItemCardViewModel>().Select(i => i.ItemId).ToList(),
                            OrderIds = groupRow != null ? new List<long> { groupRow.Id } : draggedSetIds
                        };

                        await AppDragData.StartAsync(AppDragData.Create(AppDragData.ConfigCards, dragPayload.Serialize()), DragDropEffects.Move);
                        ClearSetDrop();
                    }
                }
            }
        }
    }

    private void OnSetCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ConfigColumnEntry set && DataContext is ConfigurationsViewModel vm)
        {
            if (_pendingSelectionNarrow)
            {
                vm.SelectSetCard(set, isCtrlPressed: false, isShiftPressed: false);
                _pendingSelectionNarrow = false;
            }
        }

        _pendingSelectionNarrow = false;
        _dragStartPos = null;
        _dragCandidate = null;
        e.Handled = true;
    }

    private void OnSetCardPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pendingSelectionNarrow = false;
        _dragStartPos = null;
        _dragCandidate = null;
    }

    private void OnSetCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsInteractiveControl(e.Source)) return;
        if (sender is Border border && DataContext is ConfigurationsViewModel vm)
        {
            if (border.DataContext is ConfigSetItemViewModel set)
            {
                if (set.IsGroupOnly) vm.ToggleColumnGroup(set);
                else _ = vm.ToggleSetEnabled(set);
            }
            else if (border.DataContext is ConfigItemCardViewModel item) _ = vm.ToggleItemCardAsync(item);
            e.Handled = true;
        }
    }

    private void OnConfigGroupClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConfigSetItemViewModel set } && DataContext is ConfigurationsViewModel vm)
        {
            vm.ToggleColumnGroup(set);
        }
    }
    #endregion

    #region Drop Zones
    private void OnDropZoneDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(AppDragData.ConfigOrder))
        {
            UpdateConfigDrop(e);
            return;
        }

        if (!e.DataTransfer.Contains(AppDragData.ConfigCards))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        try
        {
            var payload = ConfigDragPayload.Deserialize(e.DataTransfer.TryGetValue(AppDragData.ConfigCards));

            if (payload != null)
            {
                bool isOverEnabled = IsPointerOverControl(EnabledColumnBorder, e.GetPosition(this));
                bool isOverDisabled = IsPointerOverControl(DisabledColumnBorder, e.GetPosition(this));

                if ((isOverEnabled && !payload.SourceIsEnabled) || (isOverDisabled && payload.SourceIsEnabled))
                {
                    ClearSetDrop();
                    e.DragEffects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }

                // Over its own column: drag up or down with a line.
                if (isOverEnabled || isOverDisabled)
                {
                    UpdateSetDrop(e, payload);
                    return;
                }
            }
        }
        catch { }

        ClearSetDrop();
        e.DragEffects = DragDropEffects.None;
    }

    private void OnDropZoneDragLeave(object? sender, DragEventArgs e)
    {
        ClearSetDrop();
        ClearConfigDrop();
    }

    private async void OnDropZoneDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ConfigurationsViewModel vm) return;

        if (e.DataTransfer.Contains(AppDragData.ConfigOrder))
        {
            var spot = FindConfigDropSpot(e);
            bool overList = IsPointerOverControl(ConfigListHost, e.GetPosition(this));
            ClearConfigDrop();
            e.Handled = true;
            if (overList && spot.HasValue && long.TryParse(e.DataTransfer.TryGetValue(AppDragData.ConfigOrder), out var configId))
            {
                vm.PlaceConfiguration(configId, spot.Value.BeforeId);
            }
            return;
        }

        if (!e.DataTransfer.Contains(AppDragData.ConfigCards)) return;

        try
        {
            var payload = ConfigDragPayload.Deserialize(e.DataTransfer.TryGetValue(AppDragData.ConfigCards));

            if (payload != null && (payload.SetIds.Count > 0 || payload.ItemIds.Count > 0))
            {
                bool isOverEnabled = IsPointerOverControl(EnabledColumnBorder, e.GetPosition(this));
                bool isOverDisabled = IsPointerOverControl(DisabledColumnBorder, e.GetPosition(this));

                bool? toEnabled = isOverEnabled && !payload.SourceIsEnabled ? true
                    : isOverDisabled && payload.SourceIsEnabled ? false
                    : null;
                if (toEnabled.HasValue)
                {
                    e.Handled = true;
                    // Items flip between included and excluded; sets move as before (may ask about sub-sets).
                    if (payload.ItemIds.Count > 0) await vm.MoveItemsAsync(payload.ItemIds, toEnabled.Value);
                    if (payload.SetIds.Count > 0) await vm.RequestMoveSetsAsync(payload.SetIds, targetToEnabled: toEnabled.Value);
                }
                else if (isOverEnabled || isOverDisabled)
                {
                    e.Handled = true;
                    if (FindSetDropSpot(e, payload) is { } spot)
                    {
                        vm.PlaceConfigSets(payload.OrderIds, spot.BeforeId, payload.SourceIsEnabled);
                    }
                }
            }
        }
        catch { }
        ClearSetDrop();
    }

    private bool IsPointerOverControl(Control? control, Point rootPos)
    {
        if (control == null || !control.IsVisible) return false;
        var controlBounds = control.Bounds;
        var controlPos = control.TranslatePoint(new Point(0, 0), this);
        if (!controlPos.HasValue) return false;

        var rect = new Rect(controlPos.Value, controlBounds.Size);
        return rect.Contains(rootPos);
    }
    #endregion

    #region Rubberband Drag Selection
    private void OnListHostPointerPressed(object? sender, PointerPressedEventArgs e, bool isEnabled)
    {
        if (IsInteractiveControl(e.Source) || IsSourceInsideSetCard(e.Source))
        {
            return;
        }

        if (DataContext is not ConfigurationsViewModel vm) return;

        var host = sender as Panel;
        if (host == null) return;

        var point = e.GetCurrentPoint(host);
        if (!point.Properties.IsLeftButtonPressed) return;

        _isRubberbanding = true;
        _isRubberbandForEnabled = isEnabled;
        _rubberbandStartPoint = point.Position;
        _activeRubberbandHost = isEnabled ? EnabledListHost : DisabledListHost;
        _activeRubberbandBorder = isEnabled ? EnabledRubberbandBorder : DisabledRubberbandBorder;
        _activeItemsControl = isEnabled ? EnabledItemsControl : DisabledItemsControl;
        _rubberbandModifiers = e.KeyModifiers;

        _initialSelectedBeforeDrag.Clear();
        bool isCtrlOrShift = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) != 0;

        if (isCtrlOrShift)
        {
            var activeCol = isEnabled ? vm.EnabledSets : vm.DisabledSets;
            foreach (var set in activeCol.Where(s => s.IsSelected))
            {
                _initialSelectedBeforeDrag.Add(set);
            }
        }
        else
        {
            vm.ClearSelection();
        }

        if (_activeRubberbandBorder != null)
        {
            Canvas.SetLeft(_activeRubberbandBorder, _rubberbandStartPoint.X);
            Canvas.SetTop(_activeRubberbandBorder, _rubberbandStartPoint.Y);
            _activeRubberbandBorder.Width = 0;
            _activeRubberbandBorder.Height = 0;
            _activeRubberbandBorder.IsVisible = true;
        }

        e.Pointer.Capture(host);
        e.Handled = true;
    }

    private void OnListHostPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isRubberbanding || _activeRubberbandBorder == null || _activeRubberbandHost == null || _activeItemsControl == null || DataContext is not ConfigurationsViewModel vm)
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(_activeRubberbandHost).Position;
        double minX = Math.Min(_rubberbandStartPoint.X, currentPoint.X);
        double minY = Math.Min(_rubberbandStartPoint.Y, currentPoint.Y);
        double width = Math.Abs(currentPoint.X - _rubberbandStartPoint.X);
        double height = Math.Abs(currentPoint.Y - _rubberbandStartPoint.Y);

        Canvas.SetLeft(_activeRubberbandBorder, minX);
        Canvas.SetTop(_activeRubberbandBorder, minY);
        _activeRubberbandBorder.Width = width;
        _activeRubberbandBorder.Height = height;

        var selectionRect = new Rect(minX, minY, width, height);
        bool isCtrl = (_rubberbandModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        foreach (var card in _activeItemsControl.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("config-set-card")))
        {
            if (card.DataContext is ConfigColumnEntry setVm && setVm is not ConfigSetItemViewModel { IsGroupOnly: true })
            {
                var cardPos = card.TranslatePoint(new Point(0, 0), _activeRubberbandHost);
                if (cardPos.HasValue)
                {
                    var cardRect = new Rect(cardPos.Value, card.Bounds.Size);
                    bool intersects = selectionRect.Intersects(cardRect);

                    if (isCtrl)
                    {
                        if (_initialSelectedBeforeDrag.Contains(setVm))
                        {
                            setVm.IsSelected = !intersects;
                        }
                        else
                        {
                            setVm.IsSelected = intersects;
                        }
                    }
                    else
                    {
                        if (_initialSelectedBeforeDrag.Contains(setVm))
                        {
                            setVm.IsSelected = true;
                        }
                        else
                        {
                            setVm.IsSelected = intersects;
                        }
                    }
                }
            }
        }

        e.Handled = true;
    }

    private void OnListHostPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isRubberbanding)
        {
            _isRubberbanding = false;
            if (_activeRubberbandBorder != null)
            {
                _activeRubberbandBorder.IsVisible = false;
            }
            _activeRubberbandHost = null;
            _activeRubberbandBorder = null;
            _activeItemsControl = null;
            _initialSelectedBeforeDrag.Clear();

            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
    #endregion

    // Format: "enabled:setIds;itemIds", e.g. "1:3,4;120".
    private class ConfigDragPayload
    {
        public bool SourceIsEnabled { get; set; }
        public List<long> SetIds { get; set; } = new();
        public List<long> ItemIds { get; set; } = new();

        // The rows being dragged. Differs from SetIds for a group row, which stands for the sets under it.
        public List<long> OrderIds { get; set; } = new();

        public string Serialize()
        {
            return $"{(SourceIsEnabled ? "1" : "0")}:{string.Join(',', SetIds)};{string.Join(',', ItemIds)}|{string.Join(',', OrderIds)}";
        }

        public static ConfigDragPayload? Deserialize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var colonIdx = text.IndexOf(':');
            if (colonIdx <= 0) return null;

            // "<sets>;<items>|<order ids>": the order ids are the rows that move when dragged up or down.
            var lists = text.Substring(colonIdx + 1).Split('|');
            var parts = lists[0].Split(';');
            var setIds = ParseIds(parts[0]);
            return new ConfigDragPayload
            {
                SourceIsEnabled = text.Substring(0, colonIdx) == "1",
                SetIds = setIds,
                ItemIds = parts.Length > 1 ? ParseIds(parts[1]) : new List<long>(),
                OrderIds = lists.Length > 1 ? ParseIds(lists[1]) : setIds
            };
        }

        private static List<long> ParseIds(string text)
        {
            var ids = new List<long>();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part, out var id)) ids.Add(id);
            }
            return ids;
        }
    }
}
