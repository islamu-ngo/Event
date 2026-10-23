using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

/// <summary>Provides nonsecret comparison evidence under the configured identity lifecycle fence.</summary>
internal sealed class ManagedTenantLocalEligibilitySource(DbContext identityContext, TimeProvider timeProvider)
    : IManagedTenantLocalEligibilitySource, IAsyncDisposable
{
    private readonly DbContext identityDb = identityContext;
    private LocalIdentityLifecycleGateCustody? custody;
    private Lease? lease;
    private int disposed;

    public async Task<IManagedTenantLocalEligibilityLease> AcquireAsync(Guid exactSubject, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (exactSubject == Guid.Empty || exactSubject.Version != 7)
            throw new ArgumentException("An exact UUID version seven Local subject is required.", nameof(exactSubject));
        if (custody is not null) throw new InvalidOperationException("The Local eligibility source is already leased.");
        var original = LocalIdentityLifecycleGateCustody.Create(identityDb);
        custody = original;
        try
        {
            await original.AcquireAsync(cancellationToken).ConfigureAwait(false);
            original.RequireCurrent();
            var row = await identityDb.Set<LocalIdentityEligibility>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.LocalSubjectId == exactSubject, cancellationToken).ConfigureAwait(false);
            original.RequireCurrent();
            if (row is null || !row.IsEligibleAt(timeProvider.GetUtcNow().UtcDateTime))
                throw new UnauthorizedAccessException("The Local subject has no current eligible binding.");
            var image = new ManagedTenantLocalEligibility(row.LocalSubjectId, row.CurrentOperationId,
                row.PersonalActorId, row.ExternalLoginId, row.State, row.Generation);
            lease = new Lease(this, original, image);
            return lease;
        }
        catch
        {
            try { await original.DisposeAsync().ConfigureAwait(false); }
            finally { if (ReferenceEquals(custody, original)) custody = null; }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (lease is { } active) await active.DisposeAsync().ConfigureAwait(false);
        else if (custody is { } pending) await pending.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class Lease(ManagedTenantLocalEligibilitySource owner,
        LocalIdentityLifecycleGateCustody original, ManagedTenantLocalEligibility image)
        : IManagedTenantLocalEligibilityLease
    {
        private int released;
        public ManagedTenantLocalEligibility Eligibility => image;

        public async Task RequireCurrentAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref released) != 0 || Volatile.Read(ref owner.disposed) != 0
                || !ReferenceEquals(owner.lease, this) || !ReferenceEquals(owner.custody, original))
                throw new UnauthorizedAccessException("The original Local eligibility lease is inactive.");
            cancellationToken.ThrowIfCancellationRequested();
            original.RequireCurrent();
            var row = await owner.identityDb.Set<LocalIdentityEligibility>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.LocalSubjectId == image.LocalSubjectId, cancellationToken).ConfigureAwait(false);
            original.RequireCurrent();
            if (row is null || !row.IsEligibleAt(owner.ObservedAt) || !row.IsCurrent(image.LocalSubjectId, image.CurrentOperationId,
                image.PersonalActorId, image.ExternalLoginId, image.State, image.Generation))
                throw new UnauthorizedAccessException("The current Local eligibility image has changed.");
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;
            try { await original.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                if (ReferenceEquals(owner.lease, this)) owner.lease = null;
                if (ReferenceEquals(owner.custody, original)) owner.custody = null;
            }
        }
    }

    private DateTime ObservedAt => timeProvider.GetUtcNow().UtcDateTime;
}
