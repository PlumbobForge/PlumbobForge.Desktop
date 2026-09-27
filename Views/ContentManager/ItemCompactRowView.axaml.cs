using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.ContentManager;

public partial class ItemCompactRowView : UserControl
{
    private Point? _dragStartPos;
    private bool _pendingSelectionNarrow;

    public ItemCompactRowView()
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

    private void OnItemRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInteractiveChild(e.Source)) return;

        if (DataContext is ItemViewModel itemVm)
        {
            var cmView = this.FindAncestorOfType<ContentManagerView>();
            if (cmView?.DataContext is ContentManagerViewModel vm)
            {
                var point = e.GetCurrentPoint(this);
                if (point.Properties.IsLeftButtonPressed)
                {
                    _dragStartPos = point.Position;
                    bool isCtrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
                    bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

                    if (isCtrl || isShift)
                    {
                        vm.SelectItem(itemVm, isCtrl, isShift);
                        _pendingSelectionNarrow = false;
                    }
                    else
                    {
                        if (itemVm.IsSelected)
                        {
                            // If multiple items are selected, do NOT deselect other items on press.
                            // The user might be starting a drag of all selected items.
                            // Defer narrowing the selection until PointerReleased if no drag happens.
                            if (vm.SelectedCount > 1)
                            {
                                _pendingSelectionNarrow = true;
                            }
                            else
                            {
                                _pendingSelectionNarrow = false;
                            }
                        }
                        else
                        {
                            vm.SelectItem(itemVm, isCtrlPressed: false, isShiftPressed: false);
                            _pendingSelectionNarrow = false;
                        }
                    }
                }
                else if (point.Properties.IsRightButtonPressed)
                {
                    if (!itemVm.IsSelected)
                    {
                        vm.SelectItem(itemVm, isCtrlPressed: false, isShiftPressed: false);
                        _pendingSelectionNarrow = false;
                    }
                }
            }
        }
    }

    private async void OnItemRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (IsInteractiveChild(e.Source)) return;

        if (DataContext is ItemViewModel itemVm && _dragStartPos.HasValue)
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed && itemVm.IsSelected)
            {
                var delta = point.Position - _dragStartPos.Value;
                if (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4)
                {
                    _pendingSelectionNarrow = false;
                    _dragStartPos = null;

                    var data = new DataObject();
                    data.Set("plumbob-item-drag", "move");
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
                }
            }
        }
    }

    private void OnItemRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is ItemViewModel itemVm)
        {
            var cmView = this.FindAncestorOfType<ContentManagerView>();
            if (cmView?.DataContext is ContentManagerViewModel vm)
            {
                if (_pendingSelectionNarrow)
                {
                    vm.SelectItem(itemVm, isCtrlPressed: false, isShiftPressed: false);
                }
            }
        }
        _pendingSelectionNarrow = false;
        _dragStartPos = null;
    }

    private void OnItemRowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pendingSelectionNarrow = false;
        _dragStartPos = null;
    }
}
