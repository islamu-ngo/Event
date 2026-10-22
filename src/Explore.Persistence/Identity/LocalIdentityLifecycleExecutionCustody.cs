using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

/// <summary>Distinguishes an original independent lifecycle owner from a strictly selected borrowed fence.</summary>
internal sealed class LocalIdentityLifecycleExecutionCustody : IAsyncDisposable
{
    private readonly DbContext identity;
    private readonly ExploreDbContext application;
    private readonly LocalIdentityExecutionLease? executionLease;
    private readonly LocalIdentityLifecycleGateCustody? owned;
    private readonly object lifecycle = new();
    private Task? disposal;

    private LocalIdentityLifecycleExecutionCustody(DbContext identity, ExploreDbContext application,
        LocalIdentityExecutionLease? executionLease, LocalIdentityLifecycleGateCustody? owned)
    {
        this.identity = identity;
        this.application = application;
        this.executionLease = executionLease;
        this.owned = owned;
    }

    internal static async Task<LocalIdentityLifecycleExecutionCustody> AcquireAsync(DbContext identity,
        ExploreDbContext application, LocalIdentityExecutionLease? executionLease, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (executionLease is not null)
        {
            var borrowed = new LocalIdentityLifecycleExecutionCustody(identity, application, executionLease, null);
            borrowed.RequireCurrent();
            return borrowed;
        }
        var owned = await LocalIdentityLifecycleGateCustody.EnterAsync(identity, cancellationToken).ConfigureAwait(false);
        return new(identity, application, null, owned);
    }

    internal void RequireCurrent()
    {
        lock (lifecycle)
        {
            if (disposal is not null)
                throw new UnauthorizedAccessException("The lifecycle execution custody has been released.");
            if (executionLease is not null)
            {
                executionLease.RequireCurrent(application, identity);
                HumanSecuritySourceGate.RequireCurrent(application);
                LocalIdentityLifecycleGateCustody.RequireHeld(identity);
            }
            else (owned ?? throw new UnauthorizedAccessException("The independent lifecycle owner is missing.")).RequireCurrent();
        }
    }

    internal async Task CommitAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        RequireCurrent();
        if (!ReferenceEquals(identity.Database.CurrentTransaction, transaction)
            && !ReferenceEquals(application.Database.CurrentTransaction, transaction))
            throw new UnauthorizedAccessException("Lifecycle settlement requires its original selected transaction.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        // A lost acknowledgement or post-commit custody failure is not an uncommitted outcome.
        RequireCurrent();
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle) return new(disposal ??= owned?.DisposeAsync().AsTask() ?? Task.CompletedTask);
    }
}
