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

        private Channel<IAppCommand> _channel;
        private readonly CancellationTokenSource _cts;
        private CancellationTokenSource _operationCts;

        public DispatcherQueue Dispatcher { get; }

        public static void Initialize(DispatcherQueue dispatcher)
        {
            if (Instance == null) Instance = new TaskService(dispatcher);
        }

        private TaskService(DispatcherQueue dispatcher)
        {
            Dispatcher = dispatcher;
            _cts = new CancellationTokenSource();
            _operationCts = new CancellationTokenSource();
            _channel = Channel.CreateUnbounded<IAppCommand>();

            Task.Run(() => ProcessQueueAsync(_channel, _operationCts.Token, _cts.Token));
        }

        public void Enqueue(IAppCommand command)
        {
            if (command != null) _channel.Writer.TryWrite(command);
        }

        // cancels whatever is currently running and resets the queue
        public CancellationToken ClearAndReset()
        {
            _operationCts.Cancel();
            _operationCts.Dispose();

            _channel.Writer.TryComplete();

            _operationCts = new CancellationTokenSource();
            _channel = Channel.CreateUnbounded<IAppCommand>();

            Task.Run(() => ProcessQueueAsync(_channel, _operationCts.Token, _cts.Token));

            return _operationCts.Token;
        }

        private async Task ProcessQueueAsync(
            Channel<IAppCommand> channel,
            CancellationToken operationToken,
            CancellationToken serviceToken)
        {
            try
            {
                await foreach (var command in channel.Reader.ReadAllAsync(serviceToken))
                {
                    if (operationToken.IsCancellationRequested)
                        continue;

                    try
                    {
                        await command.ExecuteAsync(operationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine("task cancelled mid-flight");
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
            _operationCts.Dispose();
        }
    }

    public interface IAppCommand
    {
        Task ExecuteAsync(CancellationToken token = default);
        Task FailedAsync(Exception ex) => Task.CompletedTask;
    }

    public class RelayAppCommand : IAppCommand
    {
        private readonly Func<CancellationToken, Task> _execute;
        private readonly Func<Exception, Task>? _onFailed;

        public RelayAppCommand(Func<CancellationToken, Task> execute, Func<Exception, Task>? onFailed = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _onFailed = onFailed;
        }

        public Task ExecuteAsync(CancellationToken token = default) => _execute(token);

        public Task FailedAsync(Exception ex)
        {
            if (_onFailed != null) return _onFailed(ex);
            return Task.CompletedTask;
        }
    }
}