using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Views.Modals;

public partial class TaskProgressModalView : UserControl
{
    private TaskProgressModalViewModel? _currentVm;

    public TaskProgressModalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        if (StepsScrollViewer != null)
        {
            StepsScrollViewer.PropertyChanged += (s, e) =>
            {
                if (e.Property == ScrollViewer.ExtentProperty)
                {
                    ScrollToBottom();
                }
            };
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.Steps.CollectionChanged -= OnStepsCollectionChanged;
        }

        _currentVm = DataContext as TaskProgressModalViewModel;

        if (_currentVm != null)
        {
            _currentVm.Steps.CollectionChanged += OnStepsCollectionChanged;
            ScrollToBottom();
        }
    }

    private void OnStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                StepsScrollViewer?.ScrollToEnd();
            }
            catch { }
        }, DispatcherPriority.Background);
    }
}
