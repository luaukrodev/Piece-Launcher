using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public sealed class LoadingService : ILoadingService
    {
        private const int ShowDelayMs = 150;
        private const int MinimumVisibleMs = 300;

        private readonly object _lock = new();
        private int _activeOperations;
        private bool _isLoading;
        private long _shownTimestamp;
        private CancellationTokenSource? _showCts;
        private CancellationTokenSource? _hideCts;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsLoading)));
            }
        }

        public IDisposable Begin()
        {
            lock (_lock)
            {
                _activeOperations++;

                if (_activeOperations == 1)
                {
                    _hideCts?.Cancel();
                    _hideCts = null;

                    if (!IsLoading)
                    {
                        _showCts?.Cancel();
                        _showCts = new CancellationTokenSource();
                        var token = _showCts.Token;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(ShowDelayMs, token);
                                lock (_lock)
                                {
                                    if (!token.IsCancellationRequested && _activeOperations > 0)
                                    {
                                        _shownTimestamp = Stopwatch.GetTimestamp();
                                        IsLoading = true;
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Operation completed before delay elapsed
                            }
                        }, token);
                    }
                }

                return new OperationToken(this);
            }
        }

        private void EndOperation()
        {
            lock (_lock)
            {
                _activeOperations = Math.Max(0, _activeOperations - 1);

                if (_activeOperations == 0)
                {
                    _showCts?.Cancel();
                    _showCts = null;

                    if (IsLoading)
                    {
                        var elapsedMs = (Stopwatch.GetTimestamp() - _shownTimestamp) * 1000.0 / Stopwatch.Frequency;
                        var remainingMs = Math.Max(0, (int)(MinimumVisibleMs - elapsedMs));

                        _hideCts?.Cancel();
                        _hideCts = new CancellationTokenSource();
                        var token = _hideCts.Token;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (remainingMs > 0)
                                    await Task.Delay(remainingMs, token);

                                lock (_lock)
                                {
                                    if (!token.IsCancellationRequested && _activeOperations == 0)
                                    {
                                        IsLoading = false;
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Another operation began
                            }
                        }, token);
                    }
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private sealed class OperationToken : IDisposable
        {
            private LoadingService? _service;

            public OperationToken(LoadingService service)
            {
                _service = service;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _service, null)?.EndOperation();
            }
        }
    }
}
