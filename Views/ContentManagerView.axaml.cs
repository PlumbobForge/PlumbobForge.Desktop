using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.ViewModels;
using PlumbobForge.Desktop.Views.ContentManager;

namespace PlumbobForge.Desktop.Views;

public partial class ContentManagerView : UserControl
{
    private Point? _rubberbandStartPoint;
    private bool _isRubberbanding;
    private KeyModifiers _currentDragModifiers;

    public ContentManagerView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ContentManagerViewModel vm && vm.Items.Count == 0)
            {
                await vm.LoadDataAsync();
            }
        };

        _autoscrollTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _autoscrollTimer.Tick += OnAutoscrollTick;

        // Tunneling handlers on ItemsGridContainer so dragging starts even from gaps/margins between cards
        ItemsGridContainer.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel);
        ItemsGridContainer.AddHandler(PointerMovedEvent, OnGridPointerMoved, RoutingStrategies.Tunnel);
        ItemsGridContainer.AddHandler(PointerReleasedEvent, OnGridPointerReleased, RoutingStrategies.Tunnel);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    #region Drag & Drop Files Import
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles()?.ToList();
            if (files != null && files.Count > 0)
            {
                bool hasImportableFile = files.Any(f =>
                {
                    var path = f.Path.LocalPath;
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    return ext == ".package" || ext == ".sims3pack" || ext == ".world" || ext == ".sim" ||
                           ext == ".zip" || ext == ".7z" || ext == ".rar" || PKGManager.IsArchiveExtension(path);
                });

                if (hasImportableFile)
                {
                    e.DragEffects = DragDropEffects.Copy;
                    if (DataContext is ContentManagerViewModel vm)
                    {
                        vm.IsDragOver = true;
                    }
                    e.Handled = true;
                    return;
                }
            }
        }

        e.DragEffects = DragDropEffects.None;
        if (DataContext is ContentManagerViewModel vm2)
        {
            vm2.IsDragOver = false;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is ContentManagerViewModel vm)
        {
            vm.IsDragOver = false;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is ContentManagerViewModel vm)
        {
            vm.IsDragOver = false;

            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles()?.Select(f => f.Path.LocalPath).ToArray();
                if (files != null && files.Length > 0)
                {
                    await vm.ImportDroppedFilesAsync(files);
                }
            }
        }
    }
    #endregion

    #region Sets Drag & Drop Target
    private void OnSetNodeDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("plumbob-item-drag"))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnSetNodeDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("plumbob-item-drag") && sender is Border border && border.DataContext is SetNodeViewModel targetSet)
        {
            if (DataContext is ContentManagerViewModel vm)
            {
                await vm.MoveSelectedToSetAsync(targetSet.Entity);
            }
            e.Handled = true;
        }
    }
    #endregion

    #region Rubberband Drag Selection
    private readonly HashSet<long> _initialSelectedIdsBeforeDrag = new();

    private bool IsClickOnItemCard(object? source)
    {
        Visual? visual = source as Visual;
        while (visual != null && visual != ItemsGridContainer)
        {
            if (visual is ContentHeaderBarView)
            {
                return true;
            }
            if (visual is Border b && (b.Classes.Contains("item-card") || b.Classes.Contains("compact-row")))
            {
                return true;
            }
            if (visual is Button or TextBox or ComboBox or Slider or Thumb or RepeatButton or Track or Avalonia.Controls.ContextMenu or MenuItem)
            {
                return true;
            }
            visual = visual.GetVisualParent();
        }
        return false;
    }

    private ListBox? ActiveListBox => (DataContext is ContentManagerViewModel vm && vm.IsCompactListView) ? ItemsCompactListBox : ItemsListBox;
    private ScrollViewer? ItemsScrollViewer => ActiveListBox?.FindDescendantOfType<ScrollViewer>();

    private void OnItemsListBoxSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ContentManagerViewModel vm && e.NewSize.Width > 0)
        {
            double availableWidth = e.NewSize.Width - 32;
            vm.RecalculateColumns(availableWidth);
        }
    }

    #region Middle-Click Autoscroll
    private bool _isAutoscrolling;
    private Point _autoscrollAnchorPos;
    private Point _autoscrollCurrentPos;
    private readonly Avalonia.Threading.DispatcherTimer _autoscrollTimer;

    private void StartAutoscroll(Point anchor)
    {
        _isAutoscrolling = true;
        _autoscrollAnchorPos = anchor;
        _autoscrollCurrentPos = anchor;

        Canvas.SetLeft(AutoscrollAnchor, anchor.X - 14);
        Canvas.SetTop(AutoscrollAnchor, anchor.Y - 14);
        AutoscrollAnchor.IsVisible = true;

        _autoscrollTimer.Start();
    }

    private void StopAutoscroll()
    {
        if (_isAutoscrolling)
        {
            _isAutoscrolling = false;
            _autoscrollTimer.Stop();
            AutoscrollAnchor.IsVisible = false;
        }
    }

    private void OnAutoscrollTick(object? sender, EventArgs e)
    {
        if (!_isAutoscrolling) return;

        var sv = ItemsScrollViewer;
        if (sv == null) return;

        double deltaY = _autoscrollCurrentPos.Y - _autoscrollAnchorPos.Y;
        const double deadzone = 12.0;

        if (Math.Abs(deltaY) > deadzone)
        {
            double speed = (deltaY > 0 ? deltaY - deadzone : deltaY + deadzone) * 0.35;
            double currentOffset = sv.Offset.Y;
            double newOffset = Math.Clamp(currentOffset + speed, 0, Math.Max(0, sv.Extent.Height - sv.Viewport.Height));
            sv.Offset = new Vector(sv.Offset.X, newOffset);
        }
    }
    #endregion

    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(ItemsGridContainer);

        // Middle Click Autoscroll Toggle
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (_isAutoscrolling)
            {
                StopAutoscroll();
            }
            else
            {
                StartAutoscroll(point.Position);
            }
            e.Handled = true;
            return;
        }
        else if (_isAutoscrolling)
        {
            StopAutoscroll();
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            if (IsClickOnItemCard(e.Source))
            {
                // Click is on an interactive card or button - allow it to pass through to the card
                return;
            }

            // Click is on empty space, background, or gaps between cards -> start rubberband selection!
            _rubberbandStartPoint = point.Position;
            _isRubberbanding = true;
            _currentDragModifiers = e.KeyModifiers;
            e.Pointer.Capture(ItemsGridContainer);

            Canvas.SetLeft(RubberbandBox, _rubberbandStartPoint.Value.X);
            Canvas.SetTop(RubberbandBox, _rubberbandStartPoint.Value.Y);
            RubberbandBox.Width = 0;
            RubberbandBox.Height = 0;
            RubberbandBox.IsVisible = true;

            _initialSelectedIdsBeforeDrag.Clear();
            if (DataContext is ContentManagerViewModel vm)
            {
                bool isAdditiveOrToggle = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (isAdditiveOrToggle)
                {
                    foreach (var item in vm.Items.Where(i => i.IsSelected))
                    {
                        _initialSelectedIdsBeforeDrag.Add(item.Id);
                    }
                }
                else
                {
                    vm.DeselectAll();
                }
            }

            e.Handled = true;
        }
    }

    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isAutoscrolling)
        {
            _autoscrollCurrentPos = e.GetPosition(ItemsGridContainer);
            return;
        }

        if (_isRubberbanding && _rubberbandStartPoint.HasValue)
        {
            var current = e.GetPosition(ItemsGridContainer);
            double x = Math.Min(_rubberbandStartPoint.Value.X, current.X);
            double y = Math.Min(_rubberbandStartPoint.Value.Y, current.Y);
            double w = Math.Abs(current.X - _rubberbandStartPoint.Value.X);
            double h = Math.Abs(current.Y - _rubberbandStartPoint.Value.Y);

            Canvas.SetLeft(RubberbandBox, x);
            Canvas.SetTop(RubberbandBox, y);
            RubberbandBox.Width = w;
            RubberbandBox.Height = h;

            var selRect = new Rect(x, y, w, h);
            UpdateSelectionFromRect(selRect, _currentDragModifiers);
            e.Handled = true;
        }
    }

    private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isRubberbanding)
        {
            _isRubberbanding = false;
            _rubberbandStartPoint = null;
            _initialSelectedIdsBeforeDrag.Clear();
            RubberbandBox.IsVisible = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void UpdateSelectionFromRect(Rect selRect, KeyModifiers modifiers)
    {
        if (DataContext is not ContentManagerViewModel vm) return;
        var activeList = ActiveListBox;
        if (activeList == null) return;

        var visibleCards = activeList.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => (b.Classes.Contains("item-card") || b.Classes.Contains("compact-row")) && b.DataContext is ItemViewModel)
            .ToList();

        bool isCtrl = modifiers.HasFlag(KeyModifiers.Control);
        bool isShift = modifiers.HasFlag(KeyModifiers.Shift);

        var currentSelected = new HashSet<long>(_initialSelectedIdsBeforeDrag);

        foreach (var cardBorder in visibleCards)
        {
            if (cardBorder.DataContext is ItemViewModel item)
            {
                var origin = cardBorder.TranslatePoint(new Point(0, 0), ItemsGridContainer);
                if (origin.HasValue)
                {
                    var cardBounds = new Rect(origin.Value.X, origin.Value.Y, cardBorder.Bounds.Width, cardBorder.Bounds.Height);
                    bool intersects = selRect.Intersects(cardBounds);

                    if (intersects)
                    {
                        if (isCtrl)
                        {
                            // Invert / deselect if it was already selected prior to drag
                            if (_initialSelectedIdsBeforeDrag.Contains(item.Id))
                            {
                                currentSelected.Remove(item.Id);
                            }
                            else
                            {
                                currentSelected.Add(item.Id);
                            }
                        }
                        else
                        {
                            currentSelected.Add(item.Id);
                        }
                    }
                    else
                    {
                        if (!isCtrl && !isShift)
                        {
                            currentSelected.Remove(item.Id);
                        }
                        else
                        {
                            if (_initialSelectedIdsBeforeDrag.Contains(item.Id))
                            {
                                currentSelected.Add(item.Id);
                            }
                            else
                            {
                                currentSelected.Remove(item.Id);
                            }
                        }
                    }
                }
            }
        }

        foreach (var item in vm.FilteredItems)
        {
            item.IsSelected = currentSelected.Contains(item.Id);
        }

        vm.UpdateSelectionState();
    }
    #endregion
}
