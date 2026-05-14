using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace Moonrise.Services
{
    public class TaskService : IDisposable
    {
        public static TaskService Instance { get; private set; } = null!;

        private readonly Channel<IAppCommand> _channel;
        private readonly CancellationTokenSource _cts;

        public DispatcherQueue Dispatcher { get; }

        public static void Initialize(DispatcherQueue dispatcher)
        {
            if (Instance == null) Instance = new TaskService(dispatcher);
        }

        private TaskService(DispatcherQueue dispatcher)
        {
            Dispatcher = dispatcher;
            _cts = new CancellationTokenSource();
            _channel = Channel.CreateUnbounded<IAppCommand>();

            Task.Run(() => ProcessQueueAsync(_cts.Token));
        }

        public void Enqueue(IAppCommand command)
        {
            if (command != null) _channel.Writer.TryWrite(command);
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            try
            {
                await foreach (var command in _channel.Reader.ReadAllAsync(token))
                {
                    try
                    {
                        await command.ExecuteAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"task failed: {ex.Message}");
                        try
                        {
                            await command.FailedAsync(ex);
                        }
                        catch (Exception exFallback)
                        {
                            Debug.WriteLine($"task fail logic ironically failed: {exFallback.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("TaskService queue processing cancelled :shock: (this is good)");
            }
        }

        public void Dispose()
        {
            _channel.Writer.TryComplete();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
    public interface IAppCommand
    {
        Task ExecuteAsync();
        Task FailedAsync(Exception ex) => Task.CompletedTask;
    }
}
