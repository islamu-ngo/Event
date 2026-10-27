using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Privacy.ErasureAuthority.ProviderPrimitives;

/// <summary>Shares gate lifetime across inherited execution contexts without publishing uncommitted coordinates.</summary>
internal sealed class PrivacyErasureRecoveryGate(
    DbContext context, PrivacyErasureRecoveryState? committedState) : IDisposable
{
    private readonly IDbContextTransaction _transaction = context.Database.CurrentTransaction
        ?? throw new InvalidOperationException("privacy_erasure_recovery_gate_required");
    private volatile bool _active = true;

    internal void RequireActive()
    {
        if (!_active || !ReferenceEquals(context.Database.CurrentTransaction, _transaction))
            throw new InvalidOperationException("privacy_erasure_recovery_gate_required");
        if (committedState is null)
            throw new InvalidOperationException("privacy_erasure_recovery_state_uncommitted");
    }

    internal PrivacyErasureRecoveryState Acquire(PrivacyErasureRecoveryState current)
    {
        RequireActive();
        if (current != committedState)
            throw new InvalidOperationException("privacy_erasure_recovery_state_uncommitted");
        return current;
    }

    public void Dispose() => _active = false;
}
