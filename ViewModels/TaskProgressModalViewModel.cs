using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlumbobForge.Backend.Services;

using PlumbobForge.Desktop.Services.Localization;

namespace PlumbobForge.Desktop.ViewModels;

public partial class TaskProgressModalViewModel : ObservableObject, ITaskProgressReporter
{
    private readonly Stopwatch _stopwatch = new();
    private DispatcherTimer? _timer;
    private DispatcherTimer? _uiFlushTimer;

    // Thread-safe queue: background thread writes, UI thread reads
    private readonly ConcurrentQueue<Action> _pendingUiActions = new();

    [ObservableProperty]
    private string _title = LocalizationManager.Instance.GetString("progress.processing");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    private bool _isRunning = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    private bool _isCompleted = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    private bool _hasError = false;

    [ObservableProperty]
    private string _elapsedTimeText = LocalizationManager.Instance.GetString("progress.elapsed_time", "00:00");

    [ObservableProperty]
    private bool _closeAppOnFinish = false;

    [ObservableProperty]
    private bool _showCloseAppCheckbox = true;

    public bool CanClose => !IsRunning || IsCompleted || HasError;

    public ObservableCollection<TaskProgressStepItemViewModel> Steps { get; } = new();

    public event Action? Closed;

    public void Start(string title)
    {
        Title = title;
        IsRunning = true;
        IsCompleted = false;
        HasError = false;
        Steps.Clear();
        _stopwatch.Restart();
        ElapsedTimeText = LocalizationManager.Instance.GetString("progress.elapsed_time", "00:00");

        _timer?.Stop();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (s, e) =>
        {
            var ts = _stopwatch.Elapsed;
            ElapsedTimeText = LocalizationManager.Instance.GetString("progress.elapsed_time", $"{ts.Minutes:D2}:{ts.Seconds:D2}");
        };
        _timer.Start();

        // UI flush timer: drain the pending queue every ~100ms on the UI thread
        _uiFlushTimer?.Stop();
        _uiFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _uiFlushTimer.Tick += (s, e) => FlushPendingUiActions();
        _uiFlushTimer.Start();
    }

    public void Finish(bool success)
    {
        _timer?.Stop();
        _uiFlushTimer?.Stop();
        _stopwatch.Stop();

        // Drain any remaining actions
        Dispatcher.UIThread.Post(() =>
        {
            FlushPendingUiActions();
            IsRunning = false;
            IsCompleted = success;
            HasError = !success;
        });
    }

    [RelayCommand]
    public void Close()
    {
        if (!CanClose) return;
        _timer?.Stop();
        _uiFlushTimer?.Stop();
        Closed?.Invoke();
    }

    private void FlushPendingUiActions()
    {
        // Drain all pending actions in one batch on the UI thread
        while (_pendingUiActions.TryDequeue(out var action))
        {
            action();
        }
    }

    #region ITaskProgressReporter Implementation

    public void StartStep(string stepId, string title, string? badge = null, double progress = 0.0)
    {
        // StartStep adds to the collection, so use Dispatcher.Post for immediate visibility
        Dispatcher.UIThread.Post(() =>
        {
            var existing = Steps.FirstOrDefault(s => s.Id == stepId);
            if (existing != null)
            {
                existing.Title = title;
                existing.Badge = badge;
                existing.Progress = progress;
                existing.State = TaskStepState.Running;
            }
            else
            {
                Steps.Add(new TaskProgressStepItemViewModel
                {
                    Id = stepId,
                    Title = title,
                    Badge = badge,
                    Progress = progress,
                    State = TaskStepState.Running
                });
            }
        });
    }

    public void UpdateStep(string stepId, string? title = null, string? badge = null, double? progress = null, TaskStepState? state = null)
    {
        // Enqueue to the batch queue — will be flushed every ~100ms by the UI timer
        // This avoids flooding the dispatcher with hundreds of posts per second
        _pendingUiActions.Enqueue(() =>
        {
            var existing = Steps.FirstOrDefault(s => s.Id == stepId);
            if (existing != null)
            {
                if (title != null) existing.Title = title;
                if (badge != null) existing.Badge = badge;
                if (progress.HasValue) existing.Progress = progress.Value;
                if (state.HasValue) existing.State = state.Value;
            }
        });
    }

    public void CompleteStep(string stepId, string? finalBadge = null)
    {
        // CompleteStep is important state change — post directly for immediate feedback
        Dispatcher.UIThread.Post(() =>
        {
            // Also drain any pending updates for this step first
            FlushPendingUiActions();

            var existing = Steps.FirstOrDefault(s => s.Id == stepId);
            if (existing != null)
            {
                existing.State = TaskStepState.Completed;
                existing.Progress = 1.0;
                if (finalBadge != null) existing.Badge = finalBadge;
            }
        });
    }

    public void WarningStep(string stepId, string title, string badge, IEnumerable<string>? details = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var step = new TaskProgressStepItemViewModel
            {
                Id = stepId,
                Title = title,
                Badge = badge,
                Progress = 1.0,
                State = TaskStepState.Warning
            };
            if (details != null)
            {
                foreach (var d in details)
                {
                    step.Details.Add(d);
                }
            }
            Steps.Add(step);
        });
    }

    public void ErrorStep(string stepId, string title, string? errorMessage = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var step = new TaskProgressStepItemViewModel
            {
                Id = stepId,
                Title = title,
                Progress = 1.0,
                State = TaskStepState.Error
            };
            if (!string.IsNullOrEmpty(errorMessage))
            {
                step.Details.Add(errorMessage);
            }
            Steps.Add(step);
            HasError = true;
        });
    }

    #endregion
}
