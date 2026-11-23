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
using PlumbobForge.Desktop.Views.Controls;
using PlumbobForge.Desktop.Services.Localization;

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
            if (DataContext is ContentManagerViewModel vm)
            {
                vm.RequestScrollToItem -= OnRequestScrollToItem;
                vm.RequestScrollToItem += OnRequestScrollToItem;
                if (vm.Items.Count == 0)
                {
                    await vm.LoadDataAsync();
                }
            }
        };

        _autoscrollTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _autoscrollTimer.Tick += OnAutoscrollTick;

        _columnRecalcTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _columnRecalcTimer.Tick += OnColumnRecalcTimerTick;

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
        // Items dragged from the library itself also carry their files (so they can be dropped in Explorer);
        // those are moves, not imports.
        if (e.DataTransfer.Contains(AppDragData.ItemDrag) || e.DataTransfer.Contains(AppDragData.SetsDrag))
        {
            if (DataContext is ContentManagerViewModel vmInternal) vmInternal.IsDragOver = false;
            return;
        }

        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.ToList();
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
            if (e.DataTransfer.Contains(AppDragData.ItemDrag) || e.DataTransfer.Contains(AppDragData.SetsDrag)) return;

            if (e.DataTransfer.Contains(DataFormat.File))
            {
                var files = e.DataTransfer.TryGetFiles()?.Select(f => f.Path.LocalPath).ToArray();
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
        if (e.DataTransfer.Contains(AppDragData.ItemDrag))
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
        if (e.DataTransfer.Contains(AppDragData.ItemDrag) && sender is Border border && border.DataContext is SetNodeViewModel targetSet)
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

    private readonly Avalonia.Threading.DispatcherTimer _columnRecalcTimer;
    private double _pendingAvailableWidth;

    private void OnItemsListBoxSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ContentManagerViewModel vm && e.NewSize.Width > 0)
        {
            double availableWidth = e.NewSize.Width - 32;
            _pendingAvailableWidth = availableWidth;

            // If rows haven't been calculated yet, calculate immediately
            if (vm.FilteredRows.Count == 0)
            {
                vm.RecalculateColumns(availableWidth);
                return;
            }

            // Otherwise debounce to prevent row rebuilding storms during sidebar collapse/expand transitions
            _columnRecalcTimer.Stop();
            _columnRecalcTimer.Start();
        }
    }

    private void OnColumnRecalcTimerTick(object? sender, EventArgs e)
    {
        _columnRecalcTimer.Stop();
        if (DataContext is ContentManagerViewModel vm && _pendingAvailableWidth > 0)
        {
            vm.RecalculateColumns(_pendingAvailableWidth);
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

    #region Quick Jump Scroll Support
    #region Item context menu
    private static string L(string key) => LocalizationManager.Instance.GetString(key);

    private void OnItemsMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is not ContentManagerViewModel vm || vm.GetItemMenuState() is not { } state)
        {
            e.Cancel = true;
            return;
        }
        BuildItemsMenu(ItemsContextMenu, vm, state);
    }

    // Open, organize, state, edit, delete; icons only on the open group.
    public static void BuildItemsMenu(ItemsControl menu, ContentManagerViewModel vm, ContentManagerViewModel.ItemMenuState state)
    {
        var ids = state.Selected.Select(i => i.Entity.Id).ToList();
        var libraryIds = state.Selected.Where(i => !i.Entity.IsModsItem).Select(i => i.Entity.Id).ToList();
        bool many = state.Count > 1;

        var openIn = MenuBuilder.Sub(L("library.menu_open_in"), new Control?[]
        {
            MenuBuilder.Item(L("library.menu_cc_optimizer"), () => _ = vm.OpenInOptimizerAsync(ids)),
            MenuBuilder.Item(L("library.menu_mesh_report"), () => _ = vm.OpenInMeshReportAsync(ids,
                many ? string.Format(L("library.menu_scope_items"), state.Count) : state.Selected[0].FileName)),
            state.RevealPath != null ? MenuBuilder.Item(L("library.menu_file_explorer"), () => ContentManagerViewModel.RevealInExplorer(state.RevealPath, true)) : null,
        }, "RegularWindow");

        // Moving only offers the kind of place the selection comes from; a mix gets both.
        (string, Action) SetPlace(ContentManagerViewModel.MenuTarget t) => (t.Label, () => _ = vm.MoveSelectedToSetIdAsync(t.Id));
        var moveToSet = state.AnyLibrary || !state.AnyMods
            ? MenuBuilder.PlaceSub(L("library.menu_move_to_set"), state.Sets.Select(SetPlace).ToList(), vm.RecentMoveTargets(state.Sets).Select(SetPlace).ToList(),
                L("library.menu_filter_sets"), L("library.menu_no_match"))
            : null;
        var moveToFolder = state.AnyMods
            ? MenuBuilder.PlaceSub(L("library.menu_move_to_folder"),
                state.Folders.Select(n => (vm.ModsFolderLabel(n), (Action)(() => _ = vm.MoveSelectedToModsFolderAsync(n)))).ToList(),
                Array.Empty<(string, Action)>(), L("library.menu_filter_folders"), L("library.menu_no_match"))
            : null;
        var collections = state.Collections.Count == 0 ? null : MenuBuilder.Sub(L("library.menu_collections"),
            state.Collections.Select(c => (Control?)MenuBuilder.Check(c.Name, c.State, () => _ = vm.ToggleCollectionMembershipAsync(c, libraryIds, Array.Empty<long>())))
                .Append(MenuBuilder.Divider())
                .Append(MenuBuilder.Item(L("library.menu_new_collection"), () => _ = vm.NewCollectionWithAsync(Array.Empty<long>(), libraryIds))));

        string enable = state.AllEnabled
            ? (many ? string.Format(L("library.menu_disable_items"), state.Count) : L("library.menu_disable_item"))
            : (many ? string.Format(L("library.menu_enable_items"), state.Count) : L("library.menu_enable_item"));
        bool vaultActions = vm.ShowVaultItemActions;

        MenuBuilder.Fill(menu,
            new Control?[]
            {
                MenuBuilder.Item(L("library.look_inside"), () => vm.LookInsideSelectedCommand.Execute(null), "RegularSearch", "Enter"),
                openIn,
            },
            new Control?[]
            {
                moveToSet,
                moveToFolder,
                collections,
                MenuBuilder.Item(L(state.AnyNotFavorite ? "library.menu_add_favorites" : "library.menu_remove_favorites"), () => vm.ToggleFavoriteSelectedCommand.Execute(null), gesture: "F"),
            },
            new Control?[] { MenuBuilder.Item(enable, () => vm.ToggleEnableSelectedCommand.Execute(null), gesture: "Space") },
            new Control?[]
            {
                MenuBuilder.Item(L("library.retag_selected"), () => vm.EditTagsSelectedCommand.Execute(null)),
                vaultActions && !many ? MenuBuilder.Item(L("library.menu_replace"), () => vm.ReplaceSelectedCommand.Execute(null)) : null,
                vaultActions ? MenuBuilder.Item(vm.ExportItemsMenuHeader, () => vm.ExportSelectedCommand.Execute(null)) : null,
            },
            new Control?[]
            {
                MenuBuilder.Danger(many ? string.Format(L("library.menu_delete_items"), state.Count) : L("library.menu_delete_item"),
                    () => vm.DeleteSelectedCommand.Execute(null), "Delete"),
                MenuBuilder.Danger(L("library.menu_delete_permanently"), () => vm.DeleteSelectedPermanentlyCommand.Execute(null), "Shift+Delete"),
            });
    }

    // Delete removes the selected items (Undo on the toast; Shift+Delete asks, and can delete permanently) when the last click was in the item list
    // and nothing is being typed. Rows and cards swallow presses, so focus alone can't tell.
    private TopLevel? _keyTopLevel;
    private bool _itemsAreaActive;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _keyTopLevel = TopLevel.GetTopLevel(this);
        _keyTopLevel?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
        _keyTopLevel?.AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _keyTopLevel?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _keyTopLevel?.RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
        _keyTopLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _itemsAreaActive = e.Source is Visual v && (ReferenceEquals(v, ItemsGridContainer) || v.GetVisualAncestors().Contains(ItemsGridContainer));
        // Rows swallow presses, so without this a toolbar button clicked earlier keeps focus and Space would press it.
        if (_itemsAreaActive && e.Source is not TextBox) ItemsGridContainer.Focus();
    }

    // Library shortcuts. Ctrl+F works anywhere in the library; the rest only after a click in the item list, and
    // never while something is being typed. The same keys are shown next to the matching context menu entries.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !IsEffectivelyVisible || DataContext is not ContentManagerViewModel vm) return;
        if (e.Source is TextBox || _keyTopLevel?.FocusManager?.GetFocusedElement() is TextBox) return;

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            FocusSearch(vm);
            return;
        }
        if (!_itemsAreaActive) return;

        if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            vm.SelectAllCommand.Execute(null);
            return;
        }
        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.Shift && vm.HasSelectedItems)
        {
            e.Handled = true;
            vm.DeleteSelectedPermanentlyCommand.Execute(null);
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None || !vm.HasSelectedItems) return;
        switch (e.Key)
        {
            case Key.Delete:
                vm.DeleteSelectedCommand.Execute(null);
                break;
            case Key.Enter:
                vm.LookInsideSelectedCommand.Execute(null);
                break;
            case Key.Space:
                vm.ToggleEnableSelectedCommand.Execute(null);
                break;
            case Key.F:
                vm.ToggleFavoriteSelectedCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Edit Filters on a smart set: the filter panel open, scrolled to the top, the cursor in the search box.
    private void OnRequestEditFilters()
    {
        if (DataContext is not ContentManagerViewModel vm || !IsEffectivelyVisible) return;
        var scroll = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "FiltersScroll");
        scroll?.ScrollToHome();
        FocusSearch(vm);
    }

    public void FocusSearch(ContentManagerViewModel vm)
    {
        if (vm.IsFiltersSidebarCollapsed) vm.IsFiltersSidebarCollapsed = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "LibrarySearchBox");
            if (box == null) return;
            box.Focus();
            box.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
    #endregion

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ContentManagerViewModel vm)
        {
            vm.RequestScrollToItem -= OnRequestScrollToItem;
            vm.RequestScrollToItem += OnRequestScrollToItem;
            vm.RequestEditFilters -= OnRequestEditFilters;
            vm.RequestEditFilters += OnRequestEditFilters;
        }
    }

    private void OnRequestScrollToItem(long itemId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ExecuteScrollToItem(itemId);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void ExecuteScrollToItem(long itemId, int retryCount = 0)
    {
        if (DataContext is not ContentManagerViewModel vm) return;

        if (vm.IsCompactListView)
        {
            var targetItem = vm.FilteredItems.FirstOrDefault(i => i.Id == itemId);
            if (targetItem != null)
            {
                ItemsCompactListBox.ScrollIntoView(targetItem);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var container = ItemsCompactListBox.ContainerFromItem(targetItem) as Control;
                    if (container != null)
                    {
                        container.BringIntoView();
                    }
                    else if (retryCount < 4)
                    {
                        Avalonia.Threading.DispatcherTimer.RunOnce(() => ExecuteScrollToItem(itemId, retryCount + 1), TimeSpan.FromMilliseconds(50));
                    }
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
        }
        else
        {
            var targetRow = vm.FilteredRows.FirstOrDefault(r => r.Items.Any(i => i.Id == itemId));
            if (targetRow != null)
            {
                ItemsListBox.ScrollIntoView(targetRow);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var rowContainer = ItemsListBox.ContainerFromItem(targetRow) as Control;
                    if (rowContainer != null)
                    {
                        var card = rowContainer.GetVisualDescendants()
                            .OfType<Border>()
                            .FirstOrDefault(b => b.Classes.Contains("item-card") && (b.DataContext as ItemViewModel)?.Id == itemId);
                        if (card != null)
                        {
                            card.BringIntoView();
                        }
                        else
                        {
                            rowContainer.BringIntoView();
                        }
                    }
                    else if (retryCount < 4)
                    {
                        Avalonia.Threading.DispatcherTimer.RunOnce(() => ExecuteScrollToItem(itemId, retryCount + 1), TimeSpan.FromMilliseconds(50));
                    }
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
        }
    }
    #endregion
}
