using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IconPacks.Avalonia.BoxIcons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlumbobForge.Backend.Configuration;
using PlumbobForge.Backend.Services;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.ContentManager;

public static class ItemDragDropHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    private static string DragTempRoot => Path.Combine(Path.GetTempPath(), "PlumbobForge", "DragDropExport");

    private static void CleanUpPreviousDrags()
    {
        try
        {
            if (Directory.Exists(DragTempRoot)) Directory.Delete(DragTempRoot, true);
        }
        catch { }
    }

    // Explorer gets a hard link (or a copy), so a "move" drop cannot take the file out of the library.
    private static string? LinkOrCopy(string sourcePath, string dragDir)
    {
        try
        {
            Directory.CreateDirectory(dragDir);
            string target = Path.Combine(dragDir, Path.GetFileName(sourcePath));
            if (File.Exists(target)) File.Delete(target);
            if (!(OperatingSystem.IsWindows() && CreateHardLink(target, sourcePath, IntPtr.Zero)))
            {
                File.Copy(sourcePath, target, overwrite: true);
            }
            return target;
        }
        catch
        {
            return null;
        }
    }

    private static List<ItemViewModel> GetItemsToDrag(ItemViewModel itemVm, ContentManagerViewModel? cmVm)
    {
        if (itemVm.IsSelected && cmVm != null)
        {
            var selected = cmVm.Items.Where(i => i.IsSelected).ToList();
            if (selected.Count > 0) return selected;
        }
        return new List<ItemViewModel> { itemVm };
    }

    public static void BeginItemDrag(Control sourceVisual, PointerEventArgs e, ItemViewModel itemVm, ContentManagerViewModel? cmVm)
    {
        var items = GetItemsToDrag(itemVm, cmVm);
        if (!ItemDragSession.TryStart(sourceVisual, e, itemVm, items, cmVm))
        {
            // No overlay to draw on: fall back to a plain system drag.
            _ = StartSystemDragAsync(sourceVisual, e, items);
        }
    }

    internal static async Task StartSystemDragAsync(Visual sourceVisual, PointerEventArgs e, IReadOnlyList<ItemViewModel> itemsToDrag)
    {
        CleanUpPreviousDrags();

        var vaultService = App.Services?.GetService<VaultService>();
        var options = App.Services?.GetService<IOptionsMonitor<PlumbobForgeOptions>>()?.CurrentValue;

        var validFilePaths = new List<string>();

        foreach (var item in itemsToDrag)
        {
            // One folder per item so a multi-part Sims3Pack export and same-named items do not collide.
            string itemDir = Path.Combine(DragTempRoot, item.Entity.Id.ToString());

            if (item.Entity.IsVaultManaged && vaultService != null)
            {
                // Vault-managed mod: reconstruct standalone .package on the fly
                try
                {
                    Directory.CreateDirectory(itemDir);
                    string exportFileName = Path.ChangeExtension(item.Entity.FileName, ".package");
                    string tempPath = Path.Combine(itemDir, exportFileName);
                    if (await vaultService.ExportModAsync(item.Entity.Id, tempPath))
                    {
                        validFilePaths.AddRange(Directory.GetFiles(itemDir));
                    }
                }
                catch { }
            }
            else
            {
                string filePath = item.Entity.CompleteFileName;
                if (!File.Exists(filePath) && options != null)
                {
                    var fallback = Path.Combine(options.ManagedPackageFolderPath, item.Entity.FileName);
                    if (File.Exists(fallback)) filePath = fallback;
                }

                if (File.Exists(filePath))
                {
                    string? dragPath = LinkOrCopy(filePath, itemDir);
                    if (dragPath != null) validFilePaths.Add(dragPath);
                }
            }
        }

        var data = new DataObject();
        // Maintain internal drag payload so dragging back onto Sets in PlumbobForge still moves items
        data.Set("plumbob-item-drag", "move");

        // Populate file drop formats so Windows Explorer, Desktop, or external folders can receive the package
        var topLevel = TopLevel.GetTopLevel(sourceVisual);
        if (topLevel?.StorageProvider != null && validFilePaths.Count > 0)
        {
            var storageFiles = new List<IStorageItem>();
            foreach (var path in validFilePaths)
            {
                try
                {
                    var sf = await topLevel.StorageProvider.TryGetFileFromPathAsync(path);
                    if (sf != null) storageFiles.Add(sf);
                }
                catch { }
            }

            if (storageFiles.Count > 0)
            {
                data.Set(DataFormats.Files, storageFiles);
            }
        }

        if (validFilePaths.Count > 0)
        {
            data.Set("FileDrop", validFilePaths.ToArray());
        }

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
    }
}

internal sealed class ItemDragSession
{
    private static ItemDragSession? _current;

    private static readonly TimeSpan FlyDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan EndDuration = TimeSpan.FromMilliseconds(200);
    private static readonly Vector GhostOffset = new(14, 12);

    private readonly TopLevel _top;
    private readonly OverlayLayer _layer;
    private readonly Control _source;
    private readonly List<ItemViewModel> _items;
    private readonly ContentManagerViewModel? _vm;
    private readonly IPointer _pointer;
    private readonly Control _ghost;
    private readonly Cursor? _oldCursor;

    private Border? _targetRow;
    private SetNodeViewModel? _targetSet;
    private bool _targetIsRoot;
    private bool _ended;

    private ItemDragSession(TopLevel top, OverlayLayer layer, Control source, PointerEventArgs e, ItemViewModel grabbed, List<ItemViewModel> items, ContentManagerViewModel? vm)
    {
        _top = top;
        _layer = layer;
        _source = source;
        _items = items;
        _vm = vm;
        _pointer = e.Pointer;
        _oldCursor = top.Cursor;

        foreach (var item in _items) item.IsDragging = true;

        _ghost = CreateGhost(grabbed, items.Count);
        _layer.Children.Add(_ghost);
        MoveGhost(e.GetPosition(_layer));

        // The ghost grows out of the cursor.
        Dispatcher.UIThread.Post(() =>
        {
            _ghost.RenderTransform = TransformOperations.Parse("scale(1)");
            _ghost.Opacity = 1;
        }, DispatcherPriority.Background);

        _top.Cursor = Cursor.Default;
        _top.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _top.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _top.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        _top.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _pointer.Capture(_top);
    }

    public static bool TryStart(Control source, PointerEventArgs e, ItemViewModel grabbed, List<ItemViewModel> items, ContentManagerViewModel? vm)
    {
        _current?.Cancel();
        var top = TopLevel.GetTopLevel(source);
        var layer = top != null ? OverlayLayer.GetOverlayLayer(top) : null;
        if (top == null || layer == null) return false;
        _current = new ItemDragSession(top, layer, source, e, grabbed, items, vm);
        return true;
    }

    #region Visuals

    private T Res<T>(string key, T fallback) =>
        _top.TryFindResource(key, _top.ActualThemeVariant, out var value) && value is T typed ? typed : fallback;

    private Control CreateGhost(ItemViewModel grabbed, int count)
    {
        var accent = Res<IBrush>("AccentBrush", Brushes.SeaGreen);

        Control icon;
        if (grabbed.DisplayThumbnailBitmap is Bitmap thumb)
        {
            icon = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                Child = new Image { Source = thumb, Stretch = Stretch.UniformToFill }
            };
        }
        else
        {
            icon = new PackIconBoxIcons
            {
                Kind = PackIconBoxIconsKind.RegularPackage,
                Width = 18,
                Height = 18,
                Foreground = accent,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        var name = new TextBlock
        {
            Text = grabbed.FileName,
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = Res<IBrush>("TextMain", Brushes.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 240,
            VerticalAlignment = VerticalAlignment.Center
        };

        var pill = new Border
        {
            Background = Res<IBrush>("BgSurface", new SolidColorBrush(Color.Parse("#1e1e1e"))),
            BorderBrush = Res<IBrush>("BorderSubtle", Brushes.Transparent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 14, 8),
            BoxShadow = BoxShadows.Parse("0 8 24 0 #66000000"),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { icon, name }
            }
        };

        var root = new Panel
        {
            IsHitTestVisible = false,
            RenderTransformOrigin = RelativePoint.TopLeft,
            RenderTransform = TransformOperations.Parse("scale(0.6)"),
            Opacity = 0,
            Transitions = new Transitions
            {
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = FlyDuration, Easing = new CubicEaseOut() },
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = FlyDuration, Easing = new CubicEaseOut() }
            },
            Children = { pill }
        };

        if (count > 1)
        {
            root.Children.Add(new Border
            {
                Background = accent,
                MinWidth = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(6, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -9, -9, 0),
                Child = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        return root;
    }

    private void MoveGhost(Point pointerInLayer)
    {
        Canvas.SetLeft(_ghost, pointerInLayer.X + GhostOffset.X);
        Canvas.SetTop(_ghost, pointerInLayer.Y + GhostOffset.Y);
    }

    private void RemoveLater(Control control, TimeSpan delay)
    {
        DispatcherTimer.RunOnce(() => _layer.Children.Remove(control), delay);
    }

    #endregion

    #region Pointer handling

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_ended || e.Pointer != _pointer) return;
        e.Handled = true;

        var p = e.GetPosition(_top);
        if (!e.GetCurrentPoint(_top).Properties.IsLeftButtonPressed)
        {
            // The button went up where the window didn't see it.
            Cancel();
            return;
        }

        // A couple of pixels in from the edge, so a fast flick out of the window still hands off.
        const double edge = 2;
        var size = _top.ClientSize;
        if (p.X < edge || p.Y < edge || p.X >= size.Width - edge || p.Y >= size.Height - edge)
        {
            HandOffToSystemDrag(e);
            return;
        }

        MoveGhost(e.GetPosition(_layer));
        UpdateTarget(p);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_ended || e.Pointer != _pointer) return;
        e.Handled = true;
        UpdateTarget(e.GetPosition(_top));
        _ = DropAsync();
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_ended || e.Pointer != _pointer) return;
        Cancel();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_ended || e.Key != Key.Escape) return;
        e.Handled = true;
        Cancel();
    }

    private void UpdateTarget(Point pointInTop)
    {
        Border? row = null;
        SetNodeViewModel? set = null;
        bool isRoot = false;

        var hit = _top.InputHitTest(pointInTop) as Visual;
        for (var v = hit; v != null; v = v.GetVisualParent())
        {
            if (v is Border b && b.Classes.Contains("set-node-row") && b.DataContext is SetNodeViewModel node)
            {
                row = b;
                set = node;
                break;
            }
            if (v is Control c && c.Name == "SetsTreeContainer")
            {
                isRoot = true;
                break;
            }
        }

        if (row != _targetRow)
        {
            _targetRow?.Classes.Remove("drop-target");
            row?.Classes.Add("drop-target");
            _targetRow = row;
        }
        _targetSet = set;
        _targetIsRoot = isRoot;
    }

    #endregion

    #region Ending

    private bool Detach()
    {
        if (_ended) return false;
        _ended = true;
        if (_current == this) _current = null;

        _top.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _top.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        _top.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        _top.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _top.Cursor = _oldCursor;
        _targetRow?.Classes.Remove("drop-target");
        if (_pointer.Captured == _top) _pointer.Capture(null);
        return true;
    }

    private void Undim()
    {
        foreach (var item in _items) item.IsDragging = false;
    }

    private void Cancel()
    {
        if (!Detach()) return;

        // Fly back to where the drag came from.
        var ghostPos = new Point(Canvas.GetLeft(_ghost), Canvas.GetTop(_ghost));
        var home = _source.IsEffectivelyVisible ? _source.TranslatePoint(new Point(0, 0), _layer) : null;
        var delta = home.HasValue ? home.Value - ghostPos : new Vector(0, 0);
        _ghost.RenderTransform = TransformOperations.Parse(
            FormattableString.Invariant($"translate({delta.X:0.##}px, {delta.Y:0.##}px) scale(0.8)"));
        _ghost.Opacity = 0;
        RemoveLater(_ghost, FlyDuration + TimeSpan.FromMilliseconds(40));
        Undim();
    }

    private async Task DropAsync()
    {
        var set = _targetSet;
        bool isRoot = _targetIsRoot;
        var row = _targetRow;
        if (set == null && !isRoot)
        {
            Cancel();
            return;
        }

        // Shrink into the set it was dropped on.
        Vector delta = new(0, 0);
        if (row != null && row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), _layer) is Point center)
        {
            delta = center - new Point(Canvas.GetLeft(_ghost), Canvas.GetTop(_ghost));
        }
        Detach();
        _ghost.Transitions = new Transitions
        {
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = EndDuration, Easing = new CubicEaseIn() },
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = EndDuration, Easing = new CubicEaseIn() }
        };
        _ghost.RenderTransform = TransformOperations.Parse(
            FormattableString.Invariant($"translate({delta.X:0.##}px, {delta.Y:0.##}px) scale(0.3)"));
        _ghost.Opacity = 0;
        RemoveLater(_ghost, EndDuration + TimeSpan.FromMilliseconds(40));

        try
        {
            if (_vm != null)
            {
                if (set?.IsFavorites == true) await _vm.FavoriteSelectedItemsAsync(true);
                else await _vm.MoveSelectedToSetAsync(set?.Entity);
            }
        }
        finally
        {
            Undim();
        }
    }

    private async void HandOffToSystemDrag(PointerEventArgs e)
    {
        if (!Detach()) return;
        _ghost.Opacity = 0;
        RemoveLater(_ghost, FlyDuration);
        try
        {
            await ItemDragDropHelper.StartSystemDragAsync(_source, e, _items);
        }
        catch
        {
            // A failed system drag just ends the drag.
        }
        finally
        {
            Undim();
        }
    }

    #endregion
}
