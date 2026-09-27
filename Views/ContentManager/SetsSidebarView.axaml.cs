using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.ContentManager;

public partial class SetsSidebarView : UserControl
{
    private Point? _dragStartPos;
    private bool _pendingSelectionNarrow;

    #region Rubberband Drag Selection Fields
    private readonly HashSet<long> _initialSelectedSetIdsBeforeDrag = new();
    private Point? _rubberbandStartPoint;
    private bool _isRubberbanding;
    private KeyModifiers _currentDragModifiers;
    #endregion

    public SetsSidebarView()
    {
        InitializeComponent();
    }

    private static bool IsInteractiveChild(object? source)
    {
        if (source is Visual v)
        {
            var p = v;
            while (p != null)
            {
                if (p is Button or TextBox or Avalonia.Controls.ContextMenu or MenuItem)
                {
                    return true;
                }
                p = p.GetVisualParent();
            }
        }
        return false;
    }

    #region Pointer & Multi-Selection Drag Handling
    private void OnSetRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInteractiveChild(e.Source)) return;

        if (sender is Control control && control.DataContext is SetNodeViewModel setNode)
        {
            if (DataContext is ContentManagerViewModel vm)
            {
                var point = e.GetCurrentPoint(control);
                if (point.Properties.IsLeftButtonPressed)
                {
                    if (setNode.IsAllItems)
                    {
                        // "All Items" cannot be selected for drag or dragged
                        vm.SelectedSetNode = setNode;
                        vm.DeselectAllSets();
                        _pendingSelectionNarrow = false;
                        _dragStartPos = null;
                        return;
                    }

                    _dragStartPos = point.Position;
                    bool isCtrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
                    bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

                    if (isCtrl || isShift)
                    {
                        vm.SelectSetNode(setNode, isCtrl, isShift);
                        _pendingSelectionNarrow = false;
                    }
                    else
                    {
                        if (setNode.IsSelected)
                        {
                            var selectedSets = vm.GetSelectedSetNodes();
                            if (selectedSets.Count > 1)
                            {
                                _pendingSelectionNarrow = true;
                            }
                            else
                            {
                                _pendingSelectionNarrow = false;
                                vm.SelectedSetNode = setNode;
                            }
                        }
                        else
                        {
                            vm.SelectSetNode(setNode, isCtrlPressed: false, isShiftPressed: false);
                            _pendingSelectionNarrow = false;
                        }
                    }
                }
            }
        }
    }

    private void OnSetRowPointerEntered(object? sender, PointerEventArgs e)
    {
        if (IsInteractiveChild(e.Source)) return;

        // Sweep selection when left mouse button is pressed and moving across sets with Shift
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (sender is Control control && control.DataContext is SetNodeViewModel setNode && !setNode.IsAllItems)
            {
                if (DataContext is ContentManagerViewModel vm)
                {
                    vm.SelectSetNode(setNode, isCtrlPressed: false, isShiftPressed: true);
                }
                else
                {
                    setNode.IsSelected = true;
                }
            }
        }
    }

    private async void OnSetRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (IsInteractiveChild(e.Source)) return;

        if (sender is Control control && control.DataContext is SetNodeViewModel setNode && _dragStartPos.HasValue && setNode.CanDrag)
        {
            if (DataContext is ContentManagerViewModel vm)
            {
                var point = e.GetCurrentPoint(control);
                if (point.Properties.IsLeftButtonPressed)
                {
                    var delta = point.Position - _dragStartPos.Value;
                    if (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4)
                    {
                        _pendingSelectionNarrow = false;
                        _dragStartPos = null;

                        var selectedSets = vm.GetSelectedSetNodes();
                        List<long> idsToMove;
                        if (setNode.IsSelected && selectedSets.Count > 0)
                        {
                            idsToMove = selectedSets.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToList();
                        }
                        else if (setNode.Id.HasValue)
                        {
                            idsToMove = new List<long> { setNode.Id.Value };
                        }
                        else
                        {
                            return;
                        }

                        var data = new DataObject();
                        data.Set("plumbob-sets-drag", string.Join(",", idsToMove));
                        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
                    }
                }
            }
        }
    }

    private void OnSetRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control control && control.DataContext is SetNodeViewModel setNode)
        {
            if (DataContext is ContentManagerViewModel vm)
            {
                if (_pendingSelectionNarrow)
                {
                    vm.SelectSetNode(setNode, isCtrlPressed: false, isShiftPressed: false);
                }
            }
        }
        _pendingSelectionNarrow = false;
        _dragStartPos = null;
    }

    private void OnSetRowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pendingSelectionNarrow = false;
        _dragStartPos = null;
    }
    #endregion

    #region Rubberband Marquee Drag Selection
    private bool IsClickOnInteractiveSetElement(object? source)
    {
        if (source is Visual v)
        {
            var p = v;
            while (p != null && p != SetsTreeContainer)
            {
                if (p is Button or TextBox or Avalonia.Controls.ContextMenu or MenuItem)
                {
                    return true;
                }
                if (p is Border b && b.Classes.Contains("set-node-row"))
                {
                    return true;
                }
                p = p.GetVisualParent();
            }
        }
        return false;
    }

    private void OnTreeContainerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(SetsTreeContainer);
        if (point.Properties.IsLeftButtonPressed)
        {
            if (IsClickOnInteractiveSetElement(e.Source))
            {
                return;
            }

            _rubberbandStartPoint = point.Position;
            _isRubberbanding = true;
            _currentDragModifiers = e.KeyModifiers;
            e.Pointer.Capture(SetsTreeContainer);

            Canvas.SetLeft(SetsRubberbandBox, _rubberbandStartPoint.Value.X);
            Canvas.SetTop(SetsRubberbandBox, _rubberbandStartPoint.Value.Y);
            SetsRubberbandBox.Width = 0;
            SetsRubberbandBox.Height = 0;
            SetsRubberbandBox.IsVisible = true;

            _initialSelectedSetIdsBeforeDrag.Clear();
            if (DataContext is ContentManagerViewModel vm)
            {
                bool isAdditiveOrToggle = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (isAdditiveOrToggle)
                {
                    foreach (var s in vm.GetSelectedSetNodes())
                    {
                        if (s.Id.HasValue)
                        {
                            _initialSelectedSetIdsBeforeDrag.Add(s.Id.Value);
                        }
                    }
                }
                else
                {
                    vm.DeselectAllSets();
                }
            }

            e.Handled = true;
        }
    }

    private void OnTreeContainerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isRubberbanding && _rubberbandStartPoint.HasValue)
        {
            var current = e.GetPosition(SetsTreeContainer);
            double x = Math.Min(_rubberbandStartPoint.Value.X, current.X);
            double y = Math.Min(_rubberbandStartPoint.Value.Y, current.Y);
            double w = Math.Abs(current.X - _rubberbandStartPoint.Value.X);
            double h = Math.Abs(current.Y - _rubberbandStartPoint.Value.Y);

            Canvas.SetLeft(SetsRubberbandBox, x);
            Canvas.SetTop(SetsRubberbandBox, y);
            SetsRubberbandBox.Width = w;
            SetsRubberbandBox.Height = h;

            var selRect = new Rect(x, y, w, h);
            UpdateSetSelectionFromRect(selRect, _currentDragModifiers);
            e.Handled = true;
        }
    }

    private void OnTreeContainerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isRubberbanding)
        {
            _isRubberbanding = false;
            _rubberbandStartPoint = null;
            _initialSelectedSetIdsBeforeDrag.Clear();
            SetsRubberbandBox.IsVisible = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void UpdateSetSelectionFromRect(Rect selRect, KeyModifiers modifiers)
    {
        if (DataContext is not ContentManagerViewModel vm) return;

        var visibleSetBorders = SetsTreeView.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Classes.Contains("set-node-row") && b.DataContext is SetNodeViewModel setNode && !setNode.IsAllItems)
            .ToList();

        bool isCtrl = modifiers.HasFlag(KeyModifiers.Control);

        var currentSelectedIds = new HashSet<long>(_initialSelectedSetIdsBeforeDrag);

        foreach (var border in visibleSetBorders)
        {
            if (border.DataContext is SetNodeViewModel setNode && setNode.Id.HasValue)
            {
                var origin = border.TranslatePoint(new Point(0, 0), SetsTreeContainer);
                if (origin.HasValue)
                {
                    var bounds = new Rect(origin.Value.X, origin.Value.Y, border.Bounds.Width, border.Bounds.Height);
                    bool intersects = selRect.Intersects(bounds);

                    if (intersects)
                    {
                        if (isCtrl)
                        {
                            if (_initialSelectedSetIdsBeforeDrag.Contains(setNode.Id.Value))
                            {
                                currentSelectedIds.Remove(setNode.Id.Value);
                            }
                            else
                            {
                                currentSelectedIds.Add(setNode.Id.Value);
                            }
                        }
                        else
                        {
                            currentSelectedIds.Add(setNode.Id.Value);
                        }
                    }
                }
            }
        }

        void ApplySelection(IEnumerable<SetNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsAllItems && node.Id.HasValue)
                {
                    node.IsSelected = currentSelectedIds.Contains(node.Id.Value);
                }
                ApplySelection(node.Children);
            }
        }
        ApplySelection(vm.RootSets);
    }
    #endregion

    #region Drag & Drop Targets
    private void OnSetNodeDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("plumbob-item-drag"))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
        else if (e.Data.Contains("plumbob-sets-drag"))
        {
            if (sender is Control control && control.DataContext is SetNodeViewModel targetNode)
            {
                // If target is "All Items", dropping un-subsets to root level
                if (targetNode.IsAllItems)
                {
                    e.DragEffects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }

                // Check circular references: target cannot be one of the dragged sets or their descendants
                var rawIds = e.Data.Get("plumbob-sets-drag")?.ToString();
                if (!string.IsNullOrEmpty(rawIds) && targetNode.Id.HasValue)
                {
                    var ids = rawIds.Split(',').Select(s => long.TryParse(s, out var id) ? id : -1).Where(id => id > 0).ToHashSet();
                    if (ids.Contains(targetNode.Id.Value) || (DataContext is ContentManagerViewModel vm && ids.Any(id => vm.FindNodeById(vm.RootSets, id)?.ContainsDescendant(targetNode.Id.Value) == true)))
                    {
                        e.DragEffects = DragDropEffects.None;
                        e.Handled = true;
                        return;
                    }
                }

                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;
            }
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnSetNodeDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ContentManagerViewModel vm) return;

        if (e.Data.Contains("plumbob-item-drag") && sender is Control control && control.DataContext is SetNodeViewModel setNode)
        {
            await vm.MoveSelectedToSetAsync(setNode.Entity);
            e.Handled = true;
        }
        else if (e.Data.Contains("plumbob-sets-drag") && sender is Control ctrl && ctrl.DataContext is SetNodeViewModel targetNode)
        {
            var rawIds = e.Data.Get("plumbob-sets-drag")?.ToString();
            if (!string.IsNullOrEmpty(rawIds))
            {
                var ids = rawIds.Split(',').Select(s => long.TryParse(s, out var id) ? id : -1).Where(id => id > 0).ToList();
                if (ids.Count > 0)
                {
                    long? targetParentId = targetNode.IsAllItems ? null : targetNode.Id;
                    await vm.MoveSetsAsync(ids, targetParentId);
                    e.Handled = true;
                }
            }
        }
    }

    private void OnRootDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("plumbob-sets-drag") || e.Data.Contains("plumbob-item-drag"))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private async void OnRootDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ContentManagerViewModel vm) return;

        if (e.Data.Contains("plumbob-sets-drag"))
        {
            var rawIds = e.Data.Get("plumbob-sets-drag")?.ToString();
            if (!string.IsNullOrEmpty(rawIds))
            {
                var ids = rawIds.Split(',').Select(s => long.TryParse(s, out var id) ? id : -1).Where(id => id > 0).ToList();
                if (ids.Count > 0)
                {
                    // Dropping onto root container un-subsets sets to top level
                    await vm.MoveSetsAsync(ids, null);
                    e.Handled = true;
                }
            }
        }
        else if (e.Data.Contains("plumbob-item-drag"))
        {
            await vm.MoveSelectedToSetAsync(null);
            e.Handled = true;
        }
    }
    #endregion
}
