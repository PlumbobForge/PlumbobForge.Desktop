using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.Services;

// One toast for "what just happened" after a library action, with Undo when the action can be taken back.
// A newer action replaces the toast (and its Undo); actions run by Undo don't raise toasts of their own.
//
// A delete is held back while its toast is up: the toast carries the delete itself ("commit"), which runs when the
// toast goes away (timeout, dismissed, replaced by a newer toast) or when CommitPendingAsync is called before
// something that needs the real state (Play, a rebuild, closing the app). Undo drops it, so nothing was deleted.
public partial class ActionToastService : ObservableObject
{
    public static TimeSpan ShowFor { get; set; } = TimeSpan.FromSeconds(7);

    private Func<Task>? _undo;
    private Func<Task>? _commit;
    private Task _committing = Task.CompletedTask;
    private CancellationTokenSource? _hideCts;
    private int _undoDepth;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _isError;

    public bool IsUndoing => _undoDepth > 0;

    public bool HasPendingCommit => _commit != null;

    public void Show(string message, Func<Task>? undo = null, bool isError = false, Func<Task>? commit = null)
    {
        if (IsUndoing) return;
        // The toast being replaced takes its delete with it: that delete happens now.
        StartCommit();
        _undo = undo;
        _commit = commit;
        Message = message;
        CanUndo = undo != null;
        IsError = isError;
        IsVisible = true;

        _hideCts?.Cancel();
        var cts = _hideCts = new CancellationTokenSource();
        _ = Task.Delay(ShowFor, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_hideCts, cts)) Hide(); });
        }, TaskScheduler.Default);
    }

    // Runs a held-back delete now and waits for it (and any still running). The toast stays, without Undo.
    public async Task CommitPendingAsync()
    {
        if (_commit != null)
        {
            _undo = null;
            CanUndo = false;
        }
        StartCommit();
        await _committing;
    }

    [RelayCommand]
    public void Dismiss() => Hide();

    [RelayCommand]
    public async Task UndoAsync()
    {
        var undo = _undo;
        // Undo takes the action back, so a held-back delete never happens.
        _commit = null;
        Hide();
        if (undo == null) return;
        _undoDepth++;
        try
        {
            await undo();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Undo failed", ex, "ActionToastService");
        }
        finally
        {
            _undoDepth--;
        }
    }

    private void StartCommit()
    {
        var commit = _commit;
        _commit = null;
        if (commit == null) return;
        var before = _committing;
        _committing = RunCommitAsync(before, commit);
    }

    // Deletes run one after another, never two at once on the same data.
    private static async Task RunCommitAsync(Task before, Func<Task> commit)
    {
        await before;
        try
        {
            await commit();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("A delete held back for Undo failed", ex, "ActionToastService");
        }
    }

    private void Hide()
    {
        _hideCts?.Cancel();
        _hideCts = null;
        StartCommit();
        _undo = null;
        CanUndo = false;
        IsVisible = false;
    }
}
