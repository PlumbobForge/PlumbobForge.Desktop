using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PlumbobForge.Backend.Services;

namespace PlumbobForge.Desktop.ViewModels;

public partial class TaskProgressStepItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private string? _badge;

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    [ObservableProperty]
    private double _progress = 0.0; // 0.0 to 1.0

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    private TaskStepState _state = TaskStepState.Pending;

    [ObservableProperty]
    private bool _isExpanded = false;

    public ObservableCollection<string> Details { get; } = new();

    public bool HasDetails => Details.Count > 0;

    public bool IsPending => State == TaskStepState.Pending;
    public bool IsRunning => State == TaskStepState.Running;
    public bool IsCompleted => State == TaskStepState.Completed;
    public bool IsWarning => State == TaskStepState.Warning;
    public bool IsError => State == TaskStepState.Error;

    public double ProgressPercentage => Math.Clamp(Progress * 100.0, 0.0, 100.0);
}
