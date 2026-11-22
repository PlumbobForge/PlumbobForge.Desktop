using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PlumbobForge.Desktop.ViewModels;
using PlumbobForge.Desktop.Views.Controls;
using PlumbobForge.Desktop.Services.Localization;

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
        // Takes focus on a click (rows swallow presses), so the arrow keys come here and not to a search box.
        Focusable = true;
        FocusAdorner = null;
        DataContextChanged += (_, _) => WatchViewModel();
        _slowClickTimer.Tick += OnSlowClickTimerTick;
    }

    // F2 is heard from the whole window: clicking a row doesn't move keyboard focus into the sidebar.
    private TopLevel? _keyTopLevel;

    private ContentManagerViewModel? _renameSource;

    // A new smart set opens its name for editing, in the sidebar that is on screen.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_keyTopLevel != null) ListenForRename();
    }

    private void ListenForRename()
    {
        if (_renameSource != null) _renameSource.RequestSidebarRename -= OnRequestSidebarRename;
        _renameSource = _keyTopLevel != null ? DataContext as ContentManagerViewModel : null;
        if (_renameSource != null) _renameSource.RequestSidebarRename += OnRequestSidebarRename;
    }

    // Returns true when this sidebar took the rename; one sidebar only, or two boxes would fight over focus.
    private bool OnRequestSidebarRename(SetNodeViewModel node)
    {
        if (_keyTopLevel is Window { IsActive: false } || !IsEffectivelyVisible) return false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => StartRename(node), Avalonia.Threading.DispatcherPriority.Loaded);
        return true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _keyTopLevel = TopLevel.GetTopLevel(this);
        ListenForRename();
        _keyTopLevel?.AddHandler(KeyDownEvent, OnSidebarKeyDown, RoutingStrategies.Bubble);
        // Arrows are taken first, before the window's own focus navigation moves focus into the tree.
        _keyTopLevel?.AddHandler(KeyDownEvent, OnSidebarArrowKeyDown, RoutingStrategies.Tunnel);
        _keyTopLevel?.AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // A click anywhere outside the rename box ends the rename, the same as the box losing focus.
    private bool _sidebarActive;

    private async void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _sidebarActive = e.Source is Visual src && (ReferenceEquals(src, this) || src.GetVisualAncestors().Contains(this));
        if (_sidebarActive && e.Source is Visual pressed && !pressed.GetSelfAndVisualAncestors().OfType<TextBox>().Any()) Focus();
        if (DataContext is not ContentManagerViewModel vm) return;
        var node = vm.RenamingNode();
        if (node == null || node.IsRenameSaving) return;
        for (var v = e.Source as Visual; v != null; v = v.GetVisualParent())
        {
            if (v is TextBox t && t.Classes.Contains("rename-box")) return;
        }
        if (!await vm.CommitQuickRenameAsync(node)) vm.CancelQuickRename(node);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _keyTopLevel?.RemoveHandler(KeyDownEvent, OnSidebarKeyDown);
        _keyTopLevel?.RemoveHandler(KeyDownEvent, OnSidebarArrowKeyDown);
        _keyTopLevel?.RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
        _keyTopLevel = null;
        ListenForRename();
        CancelSlowClick();
        base.OnDetachedFromVisualTree(e);
    }

    #region Inline rename
    // A second click on the name of the set or folder that is already the only one selected starts a rename,
    // after a pause, so a double-click or a quick re-click doesn't.
    private readonly Avalonia.Threading.DispatcherTimer _slowClickTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private SetNodeViewModel? _slowClickNode;
    private SetNodeViewModel? _slowClickArmed;

    public void StartRename(SetNodeViewModel node)
    {
        if (DataContext is not ContentManagerViewModel vm || !node.CanQuickRename) return;
        vm.StartQuickRename(node);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var box = SetsTreeView.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => t.Classes.Contains("rename-box") && ReferenceEquals(t.DataContext, node));
            if (box == null) return;
            box.Focus();
            box.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnSidebarArrowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None && !e.Handled && _sidebarActive && IsEffectivelyVisible
            && e.Source is not TextBox && _keyTopLevel?.FocusManager?.GetFocusedElement() is not TextBox && DataContext is ContentManagerViewModel nav)
        {
            if (MoveSidebarSelection(nav, e.Key)) e.Handled = true;
        }
    }

    private void OnSidebarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || e.KeyModifiers != KeyModifiers.None || e.Handled || !IsEffectivelyVisible) return;
        // Typing somewhere (search, a dialog field) keeps F2 to itself.
        if (e.Source is TextBox || _keyTopLevel?.FocusManager?.GetFocusedElement() is TextBox) return;
        if (DataContext is not ContentManagerViewModel vm) return;
        if (vm.SelectedSetNode is { CanQuickRename: true } node)
        {
            StartRename(node);
            e.Handled = true;
        }
    }

    // Up/Down walk the rows as they are shown (collapsed branches skipped); Right opens a branch, Left closes it
    // or goes up to its parent.
    public bool MoveSidebarSelection(ContentManagerViewModel vm, Key key)
    {
        var rows = SetsTreeView.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("set-node-row") && b.DataContext is SetNodeViewModel && b.IsEffectivelyVisible)
            .OrderBy(b => b.TranslatePoint(new Point(0, 0), SetsTreeView)?.Y ?? double.MaxValue)
            .ToList();
        if (rows.Count == 0) return false;
        var current = vm.SelectedSetNode;
        int at = rows.FindIndex(b => ReferenceEquals(b.DataContext, current));

        SetNodeViewModel? next = null;
        switch (key)
        {
            case Key.Up:
                next = (SetNodeViewModel)rows[at <= 0 ? 0 : at - 1].DataContext!;
                break;
            case Key.Down:
                next = (SetNodeViewModel)rows[at < 0 ? 0 : Math.Min(rows.Count - 1, at + 1)].DataContext!;
                break;
            case Key.Right when current is { HasChildren: true, IsExpanded: false }:
                current.IsExpanded = true;
                return true;
            case Key.Left when current is { HasChildren: true, IsExpanded: true }:
                current.IsExpanded = false;
                return true;
            case Key.Left when current != null:
                next = vm.AllSidebarNodes().FirstOrDefault(n => n.Children.Contains(current) && !n.IsSectionHeader);
                break;
            default:
                return false;
        }
        if (next == null) return false;
        if (!ReferenceEquals(next, current) || !next.IsSelected) vm.SelectSetNode(next, isCtrlPressed: false, isShiftPressed: false);
        rows.FirstOrDefault(b => ReferenceEquals(b.DataContext, next))?.BringIntoView();
        return true;
    }

    private async void OnRenameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: SetNodeViewModel node } || DataContext is not ContentManagerViewModel vm) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelQuickRename(node);
            SetsTreeView.Focus();
        }
        else if (e.Key is Key.Enter or Key.Return)
        {
            e.Handled = true;
            if (await vm.CommitQuickRenameAsync(node)) SetsTreeView.Focus();
        }
    }

    // Clicking away saves; a name that can't be used is dropped rather than leaving the box stuck open.
    private async void OnRenameBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: SetNodeViewModel node } || DataContext is not ContentManagerViewModel vm) return;
        if (!node.IsRenaming || node.IsRenameSaving) return;
        if (!await vm.CommitQuickRenameAsync(node)) vm.CancelQuickRename(node);
    }

    // Text isn't hit-testable on its own (the row's border gets the press), so this goes by position:
    // the name's own text, not the empty space after it.
    private static bool IsOnTitle(Control row, PointerEventArgs e)
    {
        var title = row.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Classes.Contains("set-title") && t.IsEffectivelyVisible);
        if (title == null) return false;
        var p = e.GetPosition(title);
        double textWidth = Math.Min(title.Bounds.Width, title.TextLayout?.WidthIncludingTrailingWhitespace ?? title.Bounds.Width);
        return p.X >= 0 && p.Y >= 0 && p.X <= textWidth + 4 && p.Y <= title.Bounds.Height;
    }

    // Called at the start of a row press, before the selection changes.
    private void NoteRowPress(Control row, SetNodeViewModel node, PointerPressedEventArgs e, ContentManagerViewModel vm)
    {
        _slowClickTimer.Stop();
        bool soleSelection = ReferenceEquals(vm.SelectedSetNode, node) && node.IsSelected && vm.GetSelectedSetNodes().Count <= 1;
        _slowClickNode = node.CanQuickRename && !node.IsRenaming && soleSelection && e.ClickCount == 1
                         && e.KeyModifiers == KeyModifiers.None && IsOnTitle(row, e) ? node : null;
    }

    private void NoteRowRelease(SetNodeViewModel node)
    {
        if (ReferenceEquals(_slowClickNode, node))
        {
            _slowClickArmed = node;
            _slowClickTimer.Start();
        }
        _slowClickNode = null;
    }

    private void CancelSlowClick()
    {
        _slowClickNode = null;
        _slowClickArmed = null;
        _slowClickTimer.Stop();
    }

    private void OnSlowClickTimerTick(object? sender, EventArgs e)
    {
        _slowClickTimer.Stop();
        var node = _slowClickArmed;
        _slowClickArmed = null;
        if (node != null && DataContext is ContentManagerViewModel vm && ReferenceEquals(vm.SelectedSetNode, node) && node.IsSelected) StartRename(node);
    }
    #endregion

    #region Width and resizing
    private const double CollapsedWidth = 48;
    private ContentManagerViewModel? _watchedVm;
    private double? _resizeStartX;
    private double _resizeStartWidth;

    private void WatchViewModel()
    {
        if (_watchedVm != null) _watchedVm.PropertyChanged -= OnViewModelPropertyChanged;
        _watchedVm = DataContext as ContentManagerViewModel;
        if (_watchedVm != null) _watchedVm.PropertyChanged += OnViewModelPropertyChanged;
        ApplyWidth();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContentManagerViewModel.SetsSidebarWidth) or nameof(ContentManagerViewModel.IsSetsSidebarCollapsed)) ApplyWidth();
    }

    // Open: the shell is the chosen width. Collapsed: 48px, with the content slid left so its collapse button stays in view.
    private void ApplyWidth()
    {
        double width = _watchedVm?.SetsSidebarWidth ?? ContentManagerViewModel.SetsSidebarDefaultWidth;
        bool collapsed = _watchedVm?.IsSetsSidebarCollapsed ?? false;
        SidebarContent.Width = width;
        SidebarShell.Width = collapsed ? CollapsedWidth : width;
        SidebarContent.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse(
            collapsed ? $"translateX({(CollapsedWidth - width).ToString(System.Globalization.CultureInfo.InvariantCulture)}px)" : "translateX(0px)");
    }

    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_watchedVm == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
        _resizeStartX = e.GetPosition(this).X;
        _resizeStartWidth = _watchedVm.SetsSidebarWidth;
        // No easing while the edge follows the pointer.
        SidebarShell.Transitions = new Avalonia.Animation.Transitions();
        SidebarContent.Transitions = new Avalonia.Animation.Transitions();
        ResizeGrip.Classes.Add("dragging");
        e.Pointer.Capture(ResizeGrip);
        e.Handled = true;
    }

    private void OnResizeGripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeStartX is not double startX || _watchedVm == null) return;
        _watchedVm.SetsSidebarWidth = _resizeStartWidth + (e.GetPosition(this).X - startX);
        e.Handled = true;
    }

    private void OnResizeGripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizeStartX == null) return;
        e.Pointer.Capture(null);
        EndResize();
        e.Handled = true;
    }

    private void OnResizeGripPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndResize();

    private void EndResize()
    {
        if (_resizeStartX == null) return;
        _resizeStartX = null;
        ResizeGrip.Classes.Remove("dragging");
        SidebarShell.ClearValue(TransitionsProperty);
        SidebarContent.ClearValue(TransitionsProperty);
    }

    private void OnResizeGripDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_watchedVm != null) _watchedVm.SetsSidebarWidth = ContentManagerViewModel.SetsSidebarDefaultWidth;
        e.Handled = true;
    }
    #endregion

    #region Set and folder context menu
    private static string L(string key) => LocalizationManager.Instance.GetString(key);

    // Open, add/organize, configuration, edit, delete; icons only on the open group. Right-clicking one of several
    // selected sets acts on all of them; what only makes sense for one (rename, new subset) is left out then.
    // Smart sets are saved filters: no collections, moves or configuration, just their filters, name and delete.
    public static void BuildSmartSetMenu(ItemsControl menu, ContentManagerViewModel vm, SetNodeViewModel node, Action<SetNodeViewModel> startRename)
    {
        MenuBuilder.Fill(menu,
            new Control?[]
            {
                MenuBuilder.Item(L("library.smart_edit_filters"), () => vm.EditSmartSetFilters(node), "RegularFilter"),
                MenuBuilder.Item(L("library.menu_rename"), () => startRename(node), gesture: "F2"),
            },
            new Control?[]
            {
                ReferenceEquals(vm.SelectedSetNode, node) && vm.ShowUpdateSmartSet ? MenuBuilder.Item(L("library.smart_save_changes"), () => vm.UpdateSmartSet()) : null,
                ReferenceEquals(vm.SelectedSetNode, node) && vm.ShowUpdateSmartSet ? MenuBuilder.Item(L("library.smart_save_new_menu"), () => vm.SaveSmartSetAsNew()) : null,
            },
            new Control?[] { MenuBuilder.Danger(L("library.smart_delete"), () => vm.DeleteSmartSet(node)) });
    }

    public static void BuildSetMenu(ItemsControl menu, ContentManagerViewModel vm, SetNodeViewModel node, Action<SetNodeViewModel> startRename)
    {
        var targets = vm.SetMenuTargets(node);
        bool many = targets.Count > 1;
        var items = targets.SelectMany(vm.ItemsOfNode).ToList();
        var ids = items.Select(i => i.Entity.Id).ToList();
        string label = many ? string.Format(L("library.sets_count"), targets.Count) : node.Name;

        var open = MenuBuilder.Sub(L("library.menu_open_in"), new Control?[]
        {
            ids.Count > 0 ? MenuBuilder.Item(L("library.menu_cc_optimizer"), () => _ = vm.OpenInOptimizerAsync(ids)) : null,
            ids.Count > 0 ? MenuBuilder.Item(L("library.menu_mesh_report"), () => _ = vm.OpenInMeshReportAsync(ids, label)) : null,
            node.IsModsFolder ? MenuBuilder.Item(L("library.menu_file_explorer"), () => ContentManagerViewModel.RevealInExplorer(vm.ModsFolderFullPath(node), false)) : null,
        }, "RegularWindow");

        Control? collections = null;
        var setIds = targets.Where(n => !n.IsModsFolder && n.Id.HasValue).Select(n => n.Id!.Value).ToList();
        if (!node.IsModsFolder && setIds.Count > 0)
        {
            var entries = vm.GetSetCollectionEntries(setIds);
            if (entries.Count > 0)
            {
                collections = MenuBuilder.Sub(L("library.menu_collections"),
                    entries.Select(c => (Control?)MenuBuilder.Check(c.Name, c.State, () => _ = vm.ToggleCollectionMembershipAsync(c, Array.Empty<long>(), setIds)))
                        .Append(MenuBuilder.Divider())
                        .Append(MenuBuilder.Item(L("library.menu_new_collection"), () => _ = vm.NewCollectionWithAsync(setIds, Array.Empty<long>()))));
            }
        }

        bool canDelete = many ? targets.Any(n => n.ShowDeleteEntry) : node.ShowDeleteEntry;
        string deleteText = node.IsModsFolder ? L("library.menu_delete_folder")
            : many ? string.Format(L("library.menu_delete_sets"), targets.Count(n => n.Id.HasValue && !n.IsDefault)) : L("library.menu_delete_set");

        MenuBuilder.Fill(menu,
            new Control?[]
            {
                ids.Count > 0 ? MenuBuilder.Item(L("library.look_inside"), () => _ = vm.LookInsideItemsAsync(items), "RegularSearch") : null,
                open,
            },
            new Control?[]
            {
                !many && node.CanAddSubSet ? MenuBuilder.Item(L("library.menu_new_subset"), () => _ = vm.AddSubSetAsync(node)) : null,
                !many && node.CanAddModsFolder ? MenuBuilder.Item(L("library.mods_new_folder"), () => _ = vm.AddModsFolderAsync(node)) : null,
                !many && node.IsModsFolder && vm.CanTidy(node) ? MenuBuilder.Item(L("library.tidy_menu"), () => _ = vm.TidyByTypeAsync(node)) : null,
                collections,
            },
            new Control?[]
            {
                node.ShowConfigurationToggle ? MenuBuilder.Item(node.ConfigurationToggleHeader, () => _ = vm.ToggleInActiveConfigurationAsync(node)) : null,
            },
            new Control?[]
            {
                !many && node.CanQuickRename ? MenuBuilder.Item(L("library.menu_rename"), () => startRename(node), gesture: "F2") : null,
                !many && node.CanCustomize ? MenuBuilder.Item(L("library.menu_customize"), () => _ = node.IsModsFolder ? vm.CustomizeModsFolderAsync(node) : vm.CustomizeSetAsync(node)) : null,
            },
            new Control?[]
            {
                canDelete ? MenuBuilder.Danger(deleteText, () => _ = node.IsModsFolder ? vm.DeleteModsFolderAsync(node) : vm.DeleteSetAsync(node)) : null,
            });
    }
    #endregion

    // The Mods Folder header's own menu: tidy Packages, or a new folder in it. Other headers have none.
    private void OnSectionHeaderContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: SetNodeViewModel node } control || DataContext is not ContentManagerViewModel vm
            || !node.IsModsRoot || control.ContextMenu is not { } menu)
        {
            e.Handled = true;
            return;
        }
        MenuBuilder.Fill(menu, new Control?[]
        {
            MenuBuilder.Item(L("library.mods_new_folder_in_packages"), () => _ = vm.AddPackagesFolderAsync()),
            vm.CanTidy(node) ? MenuBuilder.Item(L("library.tidy_menu"), () => _ = vm.TidyByTypeAsync(node)) : null,
        });
    }

    // A section header's name opens its whole section: every Mods item, or every library item. The chevron collapses it.
    private void OnSectionHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInteractiveChild(e.Source)) return;
        if (sender is not Control control || control.DataContext is not SetNodeViewModel node || DataContext is not ContentManagerViewModel vm) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        // Saved Filters has nothing of its own to show: its header just opens and closes.
        if (node.IsSmartHeader)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }
        vm.SelectedSetNode = node;
        vm.DeselectAllSets();
        node.IsSelected = true;
    }

    private void OnSectionSortClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control target || SortSetsButton.Flyout is not { } flyout) return;
        SortSetsFlyoutContent.DataContext = DataContext;
        flyout.ShowAt(target);
    }

    private void OnSectionAddClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SetNodeViewModel node } || DataContext is not ContentManagerViewModel vm) return;
        if (node.IsSmartHeader) vm.SaveSmartSetCommand.Execute(null);
        else if (node.IsSetsHeader) vm.CreateTopLevelSetCommand.Execute(null);
        else _ = vm.AddPackagesFolderAsync();
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
                NoteRowPress(control, setNode, e, vm);
                var point = e.GetCurrentPoint(control);
                if (point.Properties.IsLeftButtonPressed)
                {
                    if (setNode.IsSpecialNode)
                    {
                        // "All Items", "Favorites" and the Mods folder section cannot be selected for drag or dragged
                        vm.SelectedSetNode = setNode;
                        vm.DeselectAllSets();
                        if (setNode.IsModsNode) setNode.IsSelected = true;
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
            if (sender is Control control && control.DataContext is SetNodeViewModel setNode && !setNode.IsSpecialNode)
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
                        CancelSlowClick();
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

                        await AppDragData.StartAsync(AppDragData.Create(AppDragData.SetsDrag, string.Join(",", idsToMove)), DragDropEffects.Move);
                    }
                }
            }
        }
    }

    private void OnSetRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control control && control.DataContext is SetNodeViewModel setNode)
        {
            NoteRowRelease(setNode);
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
                if (!node.IsSpecialNode && node.Id.HasValue)
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
        // Items can be dropped on a Mods folder (not on the section header, which is no folder); sets can't.
        if (sender is Control modsControl && modsControl.DataContext is SetNodeViewModel { IsModsNode: true } modsNode)
        {
            e.DragEffects = modsNode.IsModsFolder && e.DataTransfer.Contains(AppDragData.ItemDrag) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.DataTransfer.Contains(AppDragData.ItemDrag))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
        else if (e.DataTransfer.Contains(AppDragData.SetsDrag))
        {
            if (sender is Control control && control.DataContext is SetNodeViewModel targetNode)
            {
                // Sets cannot be dropped into Favorites
                if (targetNode.IsFavorites)
                {
                    e.DragEffects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                // If target is "All Items", dropping un-subsets to root level
                if (targetNode.IsAllItems)
                {
                    e.DragEffects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }

                // Check circular references: target cannot be one of the dragged sets or their descendants
                var rawIds = e.DataTransfer.TryGetValue(AppDragData.SetsDrag);
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
        if (sender is Control modsControl && modsControl.DataContext is SetNodeViewModel { IsModsNode: true } modsNode)
        {
            if (modsNode.IsModsFolder && e.DataTransfer.Contains(AppDragData.ItemDrag))
            {
                await vm.MoveSelectedToModsFolderAsync(modsNode);
            }
            e.Handled = true;
            return;
        }

        if (e.DataTransfer.Contains(AppDragData.ItemDrag) && sender is Control control && control.DataContext is SetNodeViewModel setNode)
        {
            if (setNode.IsFavorites)
            {
                await vm.FavoriteSelectedItemsAsync(true);
            }
            else
            {
                await vm.MoveSelectedToSetAsync(setNode.Entity);
            }
            e.Handled = true;
        }
        else if (e.DataTransfer.Contains(AppDragData.SetsDrag) && sender is Control ctrl && ctrl.DataContext is SetNodeViewModel targetNode)
        {
            var rawIds = e.DataTransfer.TryGetValue(AppDragData.SetsDrag);
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
        if (e.DataTransfer.Contains(AppDragData.SetsDrag) || e.DataTransfer.Contains(AppDragData.ItemDrag))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private async void OnRootDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ContentManagerViewModel vm) return;

        if (e.DataTransfer.Contains(AppDragData.SetsDrag))
        {
            var rawIds = e.DataTransfer.TryGetValue(AppDragData.SetsDrag);
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
        else if (e.DataTransfer.Contains(AppDragData.ItemDrag))
        {
            await vm.MoveSelectedToSetAsync(null);
            e.Handled = true;
        }
    }
    private void OnSetRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control control && control.DataContext is SetNodeViewModel node)
        {
            if (node.IsSmartSet && DataContext is ContentManagerViewModel svm && control.ContextMenu is { } smartMenu)
            {
                if (!node.IsSelected) svm.SelectSetNode(node, isCtrlPressed: false, isShiftPressed: false);
                BuildSmartSetMenu(smartMenu, svm, node, StartRename);
                return;
            }
            if (node.IsSpecialNode && !node.IsModsFolder)
            {
                e.Handled = true;
                return;
            }

            if (DataContext is ContentManagerViewModel vm)
            {
                if (!node.IsSelected) vm.SelectedSetNode = node;
                // Built here, where the row is known; the menu's own DataContext isn't set yet when it opens.
                if (control.ContextMenu is { } menu) BuildSetMenu(menu, vm, node, StartRename);
            }
        }
    }

    #endregion
}
