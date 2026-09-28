using System;
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
    private ConfigSetItemViewModel? _dragCandidate;
    private bool _pendingSelectionNarrow = false;

    // Rubberband Drag Selection State
    private bool _isRubberbanding = false;
    private Point _rubberbandStartPoint;
    private Panel? _activeRubberbandHost;
    private Border? _activeRubberbandBorder;
    private ItemsControl? _activeItemsControl;
    private bool _isRubberbandForEnabled = false;
    private KeyModifiers _rubberbandModifiers;
    private readonly HashSet<long> _initialSelectedIdsBeforeDrag = new();

    public ConfigurationsView()
    {
        InitializeComponent();

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
    private void OnConfigItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ConfigItemViewModel config && DataContext is ConfigurationsViewModel vm)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _ = vm.SelectConfiguration(config);
            }
        }
    }
    #endregion

    #region Set Card Pointer & Drag-Drop Handling
    private void OnSetCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInteractiveControl(e.Source)) return;

        if (sender is Border border && border.DataContext is ConfigSetItemViewModel set && DataContext is ConfigurationsViewModel vm)
        {
            var point = e.GetCurrentPoint(border);
            if (point.Properties.IsLeftButtonPressed)
            {
                _dragStartPos = point.Position;
                _dragCandidate = set;

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
                        var col = set.IsEnabledInConfig ? vm.EnabledSets : vm.DisabledSets;
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

        if (_dragStartPos.HasValue && _dragCandidate != null && sender is Border border && border.DataContext is ConfigSetItemViewModel set)
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
                        var collection = candidate.IsEnabledInConfig ? vm.EnabledSets : vm.DisabledSets;
                        var selectedInCol = collection.Where(s => s.IsSelected).Select(s => s.Id).ToList();

                        if (!selectedInCol.Contains(candidate.Id))
                        {
                            selectedInCol = new List<long> { candidate.Id };
                        }

                        var dragPayload = new ConfigDragPayload
                        {
                            SourceIsEnabled = candidate.IsEnabledInConfig,
                            SetIds = selectedInCol
                        };

                        var data = new DataObject();
                        data.Set("plumbobforge-config-sets", dragPayload.Serialize());

                        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
                    }
                }
            }
        }
    }

    private void OnSetCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ConfigSetItemViewModel set && DataContext is ConfigurationsViewModel vm)
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
        if (sender is Border border && border.DataContext is ConfigSetItemViewModel set && DataContext is ConfigurationsViewModel vm)
        {
            _ = vm.ToggleSetEnabled(set);
            e.Handled = true;
        }
    }
    #endregion

    #region Drop Zones
    private void OnDropZoneDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("plumbobforge-config-sets"))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        try
        {
            var payload = ConfigDragPayload.Deserialize(e.Data.Get("plumbobforge-config-sets")?.ToString());

            if (payload != null)
            {
                bool isOverEnabled = IsPointerOverControl(EnabledColumnBorder, e.GetPosition(this));
                bool isOverDisabled = IsPointerOverControl(DisabledColumnBorder, e.GetPosition(this));

                if ((isOverEnabled && !payload.SourceIsEnabled) || (isOverDisabled && payload.SourceIsEnabled))
                {
                    e.DragEffects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }
            }
        }
        catch { }

        e.DragEffects = DragDropEffects.None;
    }

    private void OnDropZoneDragLeave(object? sender, DragEventArgs e)
    {
    }

    private async void OnDropZoneDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("plumbobforge-config-sets") || DataContext is not ConfigurationsViewModel vm) return;

        try
        {
            var payload = ConfigDragPayload.Deserialize(e.Data.Get("plumbobforge-config-sets")?.ToString());

            if (payload != null && payload.SetIds.Count > 0)
            {
                bool isOverEnabled = IsPointerOverControl(EnabledColumnBorder, e.GetPosition(this));
                bool isOverDisabled = IsPointerOverControl(DisabledColumnBorder, e.GetPosition(this));

                if (isOverEnabled && !payload.SourceIsEnabled)
                {
                    // Dragged from Disabled -> Enabled
                    e.Handled = true;
                    await vm.RequestMoveSetsAsync(payload.SetIds, targetToEnabled: true);
                }
                else if (isOverDisabled && payload.SourceIsEnabled)
                {
                    // Dragged from Enabled -> Disabled
                    e.Handled = true;
                    await vm.RequestMoveSetsAsync(payload.SetIds, targetToEnabled: false);
                }
            }
        }
        catch { }
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

        _initialSelectedIdsBeforeDrag.Clear();
        bool isCtrlOrShift = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) != 0;

        if (isCtrlOrShift)
        {
            var activeCol = isEnabled ? vm.EnabledSets : vm.DisabledSets;
            foreach (var set in activeCol.Where(s => s.IsSelected))
            {
                _initialSelectedIdsBeforeDrag.Add(set.Id);
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
        var targetCol = _isRubberbandForEnabled ? vm.EnabledSets : vm.DisabledSets;
        bool isCtrl = (_rubberbandModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        foreach (var card in _activeItemsControl.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("config-set-card")))
        {
            if (card.DataContext is ConfigSetItemViewModel setVm)
            {
                var cardPos = card.TranslatePoint(new Point(0, 0), _activeRubberbandHost);
                if (cardPos.HasValue)
                {
                    var cardRect = new Rect(cardPos.Value, card.Bounds.Size);
                    bool intersects = selectionRect.Intersects(cardRect);

                    if (isCtrl)
                    {
                        if (_initialSelectedIdsBeforeDrag.Contains(setVm.Id))
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
                        if (_initialSelectedIdsBeforeDrag.Contains(setVm.Id))
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
            _initialSelectedIdsBeforeDrag.Clear();

            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
    #endregion

    private class ConfigDragPayload
    {
        public bool SourceIsEnabled { get; set; }
        public List<long> SetIds { get; set; } = new();

        public string Serialize()
        {
            return $"{(SourceIsEnabled ? "1" : "0")}:{string.Join(',', SetIds)}";
        }

        public static ConfigDragPayload? Deserialize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var colonIdx = text.IndexOf(':');
            if (colonIdx <= 0) return null;

            var isEnabled = text.Substring(0, colonIdx) == "1";
            var idsStr = text.Substring(colonIdx + 1);
            var ids = new List<long>();
            foreach (var part in idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part, out var id)) ids.Add(id);
            }

            return new ConfigDragPayload
            {
                SourceIsEnabled = isEnabled,
                SetIds = ids
            };
        }
    }
}
