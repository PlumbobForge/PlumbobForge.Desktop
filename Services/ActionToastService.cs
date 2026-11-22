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
public partial class ActionToastService : ObservableObject
{
    public static TimeSpan ShowFor { get; set; } = TimeSpan.FromSeconds(7);

    private Func<Task>? _undo;
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

    public void Show(string message, Func<Task>? undo = null, bool isError = false)
    {
        if (IsUndoing) return;
        _undo = undo;
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

    [RelayCommand]
    public void Dismiss() => Hide();

    [RelayCommand]
    public async Task UndoAsync()
    {
        var undo = _undo;
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

    private void Hide()
    {
        _hideCts?.Cancel();
        _hideCts = null;
        _undo = null;
        CanUndo = false;
        IsVisible = false;
    }
}
