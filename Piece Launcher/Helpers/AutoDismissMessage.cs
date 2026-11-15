using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Helpers
{
    public sealed class AutoDismissMessage
    {
        private readonly object _gate = new();
        private CancellationTokenSource? _pendingDismissal;

        public void Cancel()
        {
            lock (_gate)
            {
                var pendingDismissal = _pendingDismissal;
                _pendingDismissal = null;
                pendingDismissal?.Cancel();
            }
        }

        public void Schedule(
            string? expectedMessage,
            Func<string?> getCurrentMessage,
            Action clearMessage,
            TimeSpan? duration = null)
        {
            if (string.IsNullOrWhiteSpace(expectedMessage))
            {
                Cancel();
                return;
            }

            var cancellation = new CancellationTokenSource();
            lock (_gate)
            {
                var previousDismissal = _pendingDismissal;
                _pendingDismissal = cancellation;
                previousDismissal?.Cancel();
            }
            _ = DismissAsync(
                expectedMessage,
                getCurrentMessage,
                clearMessage,
                duration ?? TimeSpan.FromSeconds(5),
                cancellation);
        }

        private async Task DismissAsync(
            string expectedMessage,
            Func<string?> getCurrentMessage,
            Action clearMessage,
            TimeSpan duration,
            CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(duration, cancellation.Token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ReferenceEquals(_pendingDismissal, cancellation)
                        && !cancellation.IsCancellationRequested
                        && string.Equals(getCurrentMessage(), expectedMessage, StringComparison.Ordinal))
                    {
                        clearMessage();
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_pendingDismissal, cancellation))
                        _pendingDismissal = null;
                    cancellation.Dispose();
                }
            }
        }
    }
}
