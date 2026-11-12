using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlumbobForge.Desktop.ViewModels;

namespace PlumbobForge.Desktop.Services;

internal static class ThumbnailLoadQueue
{
    private const int MaxWorkers = 3;

    private sealed class Request
    {
        public required ItemViewModel Item { get; init; }
        public bool Force { get; set; }
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly object _lock = new();
    private static readonly LinkedList<Request> _pending = new();
    private static readonly Dictionary<ItemViewModel, LinkedListNode<Request>> _byItem = new();
    private static int _workers;

    public static Task Enqueue(ItemViewModel item, bool force)
    {
        Task done;
        bool startWorker = false;
        lock (_lock)
        {
            if (_byItem.TryGetValue(item, out var node))
            {
                node.Value.Force |= force;
                _pending.Remove(node);
                _pending.AddFirst(node);
            }
            else
            {
                node = _pending.AddFirst(new Request { Item = item, Force = force });
                _byItem[item] = node;
            }
            done = node.Value.Done.Task;

            if (_workers < MaxWorkers)
            {
                _workers++;
                startWorker = true;
            }
        }

        if (startWorker) _ = Task.Run(WorkAsync);
        return done;
    }

    private static async Task WorkAsync()
    {
        while (true)
        {
            Request request;
            lock (_lock)
            {
                var node = _pending.First;
                if (node == null)
                {
                    _workers--;
                    return;
                }
                _pending.RemoveFirst();
                _byItem.Remove(node.Value.Item);
                request = node.Value;
            }

            try
            {
                if (request.Force || request.Item.IsOnScreen)
                {
                    await request.Item.LoadThumbnailCoreAsync();
                }
                else
                {
                    request.Item.OnThumbnailLoadSkipped();
                }
            }
            catch (Exception)
            {
                request.Item.OnThumbnailLoadSkipped();
            }
            request.Done.TrySetResult();
        }
    }
}
