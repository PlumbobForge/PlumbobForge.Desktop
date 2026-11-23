using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

// Collection Creator in a wide window: the collections on the left, the open one in a one-column
// editor on the right. Picking another collection while the open one has unsaved changes asks first,
// in the editor's header, so there is only ever one Save button.
public partial class HealthViewModel
{
    [ObservableProperty]
    private CollectionItemViewModel? _selectedCollection;

    [ObservableProperty]
    private bool _isIconPickerExpanded;

    [ObservableProperty]
    private string? _pendingCollectionMessage;

    private sealed record CollectionState(string Name, ulong Icon, bool Hide, string Sets, string Items);

    // What the open collection looked like when it was opened; null while it is still loading.
    private CollectionState? _collectionSnapshot;
    private Func<Task>? _pendingCollectionAction;
    private bool _syncingCollectionSelection;
    private int _collectionLoadVersion;
    private ulong? _collectionBeforeNewId;

    private CollectionState CurrentCollectionState() => new(
        EditingCollectionName.Trim(),
        SelectedIcon?.Key.Instance ?? 0,
        EditingHideFromCatalog,
        string.Join(",", AssignedSets.Select(s => s.SetId).OrderBy(id => id)),
        string.Join(",", IncludedItems.Select(i => i.ItemId).OrderBy(id => id)));

    public bool IsCollectionDirty
    {
        get
        {
            if (!IsCollectionEditorOpen || _collectionSnapshot == null) return false;
            // A new collection only counts once something was put in it.
            if (!IsEditingExistingCollection)
                return !string.IsNullOrWhiteSpace(EditingCollectionName) || AssignedSets.Count > 0 || IncludedItems.Count > 0;
            return CurrentCollectionState() != _collectionSnapshot;
        }
    }

    public bool ShowCollectionCards => IsNarrowLayout && !IsCollectionEditorOpen;
    public bool ShowCollectionPlaceholder => IsWideLayout && !IsCollectionEditorOpen && !IsLoadingCollections;
    public bool ShowIconPicker => IsNarrowLayout || IsIconPickerExpanded;
    public ScrollBarVisibility CollectionEditorScrollBar => IsWideLayout ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    public bool ShowCollectionDelete => IsWideLayout && IsEditingExistingCollection;

    // Narrow: Cancel closes the editor. Wide: Discard, only when there is something to discard.
    // Either way, with a switch waiting, it is Discard and goes on to it.
    public bool ShowCollectionCancel => IsNarrowLayout || IsCollectionDirty || !IsEditingExistingCollection;
    public string CollectionCancelLabel => LocalizationManager.Instance.GetString(IsWideLayout || HasPendingCollectionAction ? "tools.collections.discard" : "common.cancel");
    public int IconGridColumns => IsWideLayout ? 10 : 6;
    public string IconPickerToggleLabel => LocalizationManager.Instance.GetString(IsIconPickerExpanded ? "tools.collections.done" : "tools.collections.change_icon");

    public bool HasPendingCollectionAction => PendingCollectionMessage != null;

    public string CollectionEditorStatus => IsWideLayout && IsCollectionDirty
        ? LocalizationManager.Instance.GetString("tools.collections.unsaved")
        : LocalizationManager.Instance.GetString("tools.collections.editor_desc");

    public bool IsCollectionStatusUnsaved => IsWideLayout && IsCollectionDirty;

    public CollectionItemViewModel? EditingCollectionItem =>
        IsEditingExistingCollection ? AllCollections.FirstOrDefault(c => c.Id == EditingCollectionId) : null;

    partial void OnPendingCollectionMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPendingCollectionAction));
        OnPropertyChanged(nameof(CollectionCancelLabel));
    }

    partial void OnIsIconPickerExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowIconPicker));
        OnPropertyChanged(nameof(IconPickerToggleLabel));
    }

    partial void OnIsCollectionEditorOpenChanged(bool value) => NotifyCollectionLayout();
    partial void OnEditingCollectionIdChanged(ulong? value) => NotifyCollectionLayout();
    partial void OnIsLoadingCollectionsChanged(bool value) => OnPropertyChanged(nameof(ShowCollectionPlaceholder));
    partial void OnEditingCollectionNameChanged(string value) => NotifyCollectionDirty();
    partial void OnEditingHideFromCatalogChanged(bool value) => NotifyCollectionDirty();

    private void NotifyCollectionDirty()
    {
        OnPropertyChanged(nameof(IsCollectionDirty));
        OnPropertyChanged(nameof(ShowCollectionCancel));
        OnPropertyChanged(nameof(CollectionEditorStatus));
        OnPropertyChanged(nameof(IsCollectionStatusUnsaved));
    }

    private void NotifyCollectionLayout()
    {
        OnPropertyChanged(nameof(ShowCollectionCards));
        OnPropertyChanged(nameof(ShowCollectionPlaceholder));
        OnPropertyChanged(nameof(ShowIconPicker));
        OnPropertyChanged(nameof(CollectionEditorScrollBar));
        OnPropertyChanged(nameof(IconGridColumns));
        OnPropertyChanged(nameof(ShowCollectionDelete));
        OnPropertyChanged(nameof(CollectionCancelLabel));
        OnPropertyChanged(nameof(EditingCollectionItem));
        NotifyCollectionDirty();
    }

    private void OnCollectionLayoutWidthChanged(bool wide)
    {
        NotifyCollectionLayout();
        if (!wide) return;
        if (IsCollectionEditorOpen) SyncCollectionSelection();
        else if (IsCollectionCreatorActive && HasLoadedCollections) OpenFirstCollection();
    }

    partial void OnSelectedCollectionChanged(CollectionItemViewModel? value)
    {
        // The list clears its selection while it is refilled; that is not a pick.
        if (_syncingCollectionSelection || value == null || !IsWideLayout) return;
        if (IsCollectionEditorOpen && IsEditingExistingCollection && value.Id == EditingCollectionId) return;
        RunCollectionAction(() => EditCollectionAsync(value), value.Name);
    }

    // Runs now, or, with unsaved changes, waits for Save or Discard in the editor's header.
    private void RunCollectionAction(Func<Task> action, string? targetName)
    {
        if (IsCollectionEditorOpen && IsCollectionDirty)
        {
            _pendingCollectionAction = action;
            string current = string.IsNullOrWhiteSpace(EditingCollectionName)
                ? LocalizationManager.Instance.GetString("tools.collections.unnamed")
                : EditingCollectionName.Trim();
            PendingCollectionMessage = targetName == null
                ? string.Format(LocalizationManager.Instance.GetString("tools.collections.pending_new"), current)
                : string.Format(LocalizationManager.Instance.GetString("tools.collections.pending_switch"), current, targetName);
            // The list keeps showing the collection that is still open.
            Dispatcher.UIThread.Post(SyncCollectionSelection);
            return;
        }
        _pendingCollectionAction = null;
        PendingCollectionMessage = null;
        _ = action();
    }

    private void SyncCollectionSelection()
    {
        _syncingCollectionSelection = true;
        try
        {
            SelectedCollection = IsCollectionEditorOpen && IsEditingExistingCollection
                ? FilteredCollections.FirstOrDefault(c => c.Id == EditingCollectionId)
                : null;
        }
        finally
        {
            _syncingCollectionSelection = false;
        }
    }

    private void OpenFirstCollection()
    {
        if (FilteredCollections.FirstOrDefault() is { } first) _ = EditCollectionAsync(first);
    }

    // After the list is (re)loaded: keep the open collection selected, or open the first one.
    private void AfterCollectionsLoaded()
    {
        if (!IsWideLayout) return;
        if (IsCollectionEditorOpen)
        {
            if (!IsEditingExistingCollection || EditingCollectionItem != null)
            {
                SyncCollectionSelection();
                return;
            }
            // The open collection is gone (deleted).
            IsCollectionEditorOpen = false;
        }
        OpenFirstCollection();
    }

    // The top bar's New Collection.
    [RelayCommand]
    public void RequestNewCollection() => RunCollectionAction(CreateNewCollectionAsync, null);

    [RelayCommand]
    public void KeepEditingCollection()
    {
        _pendingCollectionAction = null;
        PendingCollectionMessage = null;
    }

    [RelayCommand]
    public void ToggleIconPicker() => IsIconPickerExpanded = !IsIconPickerExpanded;

    private async Task RunPendingCollectionActionAsync()
    {
        var action = _pendingCollectionAction;
        _pendingCollectionAction = null;
        PendingCollectionMessage = null;
        if (action != null) await action();
    }

    // Wide: goes on to the collection that was waiting, or reopens this one as it was saved.
    private async Task DiscardCollectionChangesAsync()
    {
        if (_pendingCollectionAction != null)
        {
            await RunPendingCollectionActionAsync();
            return;
        }
        var reopen = EditingCollectionItem
            ?? AllCollections.FirstOrDefault(c => c.Id == _collectionBeforeNewId)
            ?? FilteredCollections.FirstOrDefault();
        if (reopen != null) await EditCollectionAsync(reopen);
        else IsCollectionEditorOpen = false;
    }
}
