using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Moonrise.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Moonrise.Services
{
    public interface IToastHandle : IDisposable
    {
        void Update(string? message = null, double? progress = null, bool? isIndeterminate = null, string? title = null);
        void Complete(string message, TimeSpan? autoDismissAfter = null);
        void Fail(string errorMessage, TimeSpan? autoDismissAfter = null);
        void Dismiss();
    }

    public interface IToastService
    {
        ObservableCollection<ToastModel> ActiveToasts { get; }
        void Show(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational, TimeSpan? duration = null);
        IToastHandle ShowProgress(string title, string message, bool isIndeterminate = true, bool isClosable = true);
        void Dismiss(ToastModel toast);
    }

    public class ToastService : IToastService
    {
        private readonly DispatcherQueue _dispatcher;
        public ObservableCollection<ToastModel> ActiveToasts { get; } = new();

        public ToastService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Show(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational, TimeSpan? duration = null)
        {
            var toast = new ToastModel
            {
                Title = title,
                Message = message,
                Severity = severity,
                IsClosable = true,
                ShowProgressBar = false
            };

            EnqueueOnUI(() =>
            {
                ActiveToasts.Add(toast);
                ScheduleDismissal(toast, duration ?? TimeSpan.FromSeconds(3));
            });
        }

        public IToastHandle ShowProgress(string title, string message, bool isIndeterminate = true, bool isClosable = true)
        {
            var toast = new ToastModel
            {
                Title = title,
                Message = message,
                Severity = InfoBarSeverity.Informational,
                IsClosable = isClosable,
                ShowProgressBar = true,
                IsIndeterminate = isIndeterminate,
                ProgressBarOpacity = 1.0
            };

            EnqueueOnUI(() =>
            {
                ActiveToasts.Add(toast);
            });

            return new ToastHandle(this, toast);
        }

        public void Dismiss(ToastModel toast)
        {
            EnqueueOnUI(() =>
            {
                toast.IsOpen = false;
                ActiveToasts.Remove(toast);
            });
        }

        internal void ScheduleDismissal(ToastModel toast, TimeSpan delay)
        {
            Task.Run(async () =>
            {
                await Task.Delay(delay);
                Dismiss(toast);
            });
        }

        internal void EnqueueOnUI(Action action)
        {
            if (_dispatcher.HasThreadAccess)
            {
                action();
            }
            else
            {
                _dispatcher.TryEnqueue(() => action());
            }
        }

        private class ToastHandle : IToastHandle
        {
            private readonly ToastService _service;
            private readonly ToastModel _toast;
            private int _isDisposed;

            public ToastHandle(ToastService service, ToastModel toast)
            {
                _service = service;
                _toast = toast;
            }

            public void Update(string? message = null, double? progress = null, bool? isIndeterminate = null, string? title = null)
            {
                if (Volatile.Read(ref _isDisposed) != 0) return;

                _service.EnqueueOnUI(() =>
                {
                    if (message != null) _toast.Message = message;
                    if (title != null) _toast.Title = title;
                    if (progress.HasValue) _toast.Progress = progress.Value;
                    if (isIndeterminate.HasValue) _toast.IsIndeterminate = isIndeterminate.Value;
                });
            }

            public void Complete(string message, TimeSpan? autoDismissAfter = null)
            {
                if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

                _service.EnqueueOnUI(() =>
                {
                    _toast.Severity = InfoBarSeverity.Success;
                    _toast.Message = message;
                    _toast.ShowProgressBar = false;
                    _service.ScheduleDismissal(_toast, autoDismissAfter ?? TimeSpan.FromSeconds(3));
                });
            }

            public void Fail(string errorMessage, TimeSpan? autoDismissAfter = null)
            {
                if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

                _service.EnqueueOnUI(() =>
                {
                    _toast.Severity = InfoBarSeverity.Error;
                    _toast.Message = errorMessage;
                    _toast.ShowProgressBar = false;
                    _service.ScheduleDismissal(_toast, autoDismissAfter ?? TimeSpan.FromSeconds(4));
                });
            }

            public void Dismiss()
            {
                if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
                _service.Dismiss(_toast);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
                _service.Dismiss(_toast);
            }
        }
    }
}
