namespace Explore.ServiceDefaults.Secrets;

public sealed partial class SecretAuthorityAcquisition
{
    /// <summary>
    /// Reserves borrowed native work until its cancellation has unwound. Retirement
    /// cancels the lease and drains it before disposing the selected client.
    /// </summary>
    public OperationLease BeginOperation(CancellationToken cancellationToken = default)
    {
        lock (_operationGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var timeout = new CancellationTokenSource(ReadTimeout, _clock);
            var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token, timeout.Token);
            _activePreparations++;
            return new OperationLease(this, timeout, linked);
        }
    }

    public sealed class OperationLease : IDisposable
    {
        private SecretAuthorityAcquisition? _owner;
        private readonly CancellationTokenSource _timeout;
        private readonly CancellationTokenSource _linked;

        internal OperationLease(SecretAuthorityAcquisition owner,
            CancellationTokenSource timeout, CancellationTokenSource linked)
        {
            _owner = owner;
            _timeout = timeout;
            _linked = linked;
        }

        public CancellationToken Token => _linked.Token;

        public void Dispose()
        {
            SecretAuthorityAcquisition? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
                return;
            _linked.Dispose();
            _timeout.Dispose();
            TaskCompletionSource? drained = null;
            lock (owner._operationGate)
            {
                owner._activePreparations--;
                if (owner._activePreparations == 0 && owner._disposed != 0)
                    drained = owner._preparationsDrained;
            }
            drained?.TrySetResult();
        }
    }
}
