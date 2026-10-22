using System.Runtime.ExceptionServices;
using System.Transactions;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

/// <summary>Selects nonsecret eligibility evidence without replacing or disposing the original application owner.</summary>
internal sealed class ConfiguredManagedTenantLocalEligibilitySource : IManagedTenantLocalEligibilitySource, IAsyncDisposable
{
    private readonly ExploreDbContext application;
    private readonly LocalIdentityExecutionContextCustody applicationCustody;
    private readonly IdentityDatabaseTopology topology;
    private readonly LocalIdentityWriterDbContextFactory? externalWriter;
    private readonly TimeProvider clock;
    private readonly object lifecycle = new();
    private readonly CancellationTokenSource abandonment = new();
    private Attempt? active;
    private Task? disposal;
    private bool disposed;

    internal ConfiguredManagedTenantLocalEligibilitySource(ExploreDbContext originalGlobal,
        IdentityDatabaseTopology topology, LocalIdentityWriterDbContextFactory? externalWriter, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(originalGlobal);
        ArgumentNullException.ThrowIfNull(clock);
        if (!Enum.IsDefined(topology) || topology == IdentityDatabaseTopology.Colocated && externalWriter is not null
            || topology == IdentityDatabaseTopology.External && externalWriter?.Topology != IdentityDatabaseTopology.External)
            throw new ArgumentException("Local observation requires its exact configured identity topology.", nameof(externalWriter));
        application = originalGlobal;
        applicationCustody = LocalIdentityExecutionContextCustody.Capture(originalGlobal);
        this.topology = topology;
        this.externalWriter = externalWriter;
        this.clock = clock;
    }

    public Task<IManagedTenantLocalEligibilityLease> AcquireAsync(Guid exactSubject, CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (exactSubject == Guid.Empty || exactSubject.Version != 7)
                throw new ArgumentException("An exact UUID version seven Local subject is required.", nameof(exactSubject));
            if (active is not null) throw new InvalidOperationException("The configured Local observation is already retained.");
            RequireApplication();
            if (application.Database.CurrentTransaction is not null || Transaction.Current is not null)
                throw new InvalidOperationException("Local observation must precede the original transaction.");
            var attempt = new Attempt(this, exactSubject, cancellationToken);
            active = attempt;
            return attempt.Start();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            if (disposal is not null) return new(disposal);
            disposed = true;
            abandonment.Cancel();
            return new(disposal = DisposeCoreAsync(active));
        }
    }

    private async Task DisposeCoreAsync(Attempt? attempt)
    {
        try
        {
            if (attempt is null) return;
            try { await attempt.Acquisition.ConfigureAwait(false); }
            catch (Exception) { /* The acquiring caller observes its outcome; disposal still joins cleanup. */ }
            await attempt.DisposeAsync().ConfigureAwait(false);
        }
        finally { abandonment.Dispose(); }
    }

    private void RequireApplication()
    {
        application.RequireBoundTenantLease();
        applicationCustody.RequireCurrent(application);
        if (application.IsTenantFilterBypassed || application.TenantFilterTenantId is not null)
            throw new UnauthorizedAccessException("The original instance application owner changed.");
    }

    private sealed class Attempt : IAsyncDisposable
    {
        private readonly ConfiguredManagedTenantLocalEligibilitySource owner;
        private readonly Guid subject;
        private readonly CancellationTokenSource cancellation;
        private readonly object cleanupLock = new();
        private ExternalIdentityDbContext? ownedIdentity;
        private ManagedTenantLocalEligibilitySource? inner;
        private IManagedTenantLocalEligibilityLease? innerLease;
        private Task? cleanup;
        private bool acquired;

        internal Attempt(ConfiguredManagedTenantLocalEligibilitySource owner, Guid subject, CancellationToken token)
        {
            this.owner = owner;
            this.subject = subject;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, owner.abandonment.Token);
        }

        internal Task<IManagedTenantLocalEligibilityLease> Acquisition { get; private set; } = null!;
        internal Task<IManagedTenantLocalEligibilityLease> Start() => Acquisition = AcquireCoreAsync();

        private async Task<IManagedTenantLocalEligibilityLease> AcquireCoreAsync()
        {
            try
            {
                var token = cancellation.Token;
                token.ThrowIfCancellationRequested();
                owner.RequireApplication();
                DbContext identity = owner.application;
                if (owner.topology == IdentityDatabaseTopology.External)
                {
                    ownedIdentity = await owner.externalWriter!.CreateExternalIdentityAsync(token).ConfigureAwait(false);
                    identity = ownedIdentity;
                    owner.RequireApplication();
                }
                inner = new(identity, owner.clock);
                innerLease = await inner.AcquireAsync(subject, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                owner.RequireApplication();
                lock (owner.lifecycle)
                {
                    ObjectDisposedException.ThrowIf(owner.disposed, owner);
                    if (!ReferenceEquals(owner.active, this))
                        throw new UnauthorizedAccessException("The original Local observation issuer changed.");
                    acquired = true;
                }
                return new Lease(this, innerLease.Eligibility);
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal async Task RequireCurrentAsync(CancellationToken token)
        {
            RequireCurrent();
            await innerLease!.RequireCurrentAsync(token).ConfigureAwait(false);
            RequireCurrent();
        }

        private void RequireCurrent()
        {
            lock (owner.lifecycle)
                if (owner.disposed || !acquired || cleanup is not null || !ReferenceEquals(owner.active, this))
                    throw new UnauthorizedAccessException("The configured Local observation lease is inactive.");
            owner.RequireApplication();
        }

        public ValueTask DisposeAsync()
        {
            lock (cleanupLock) return new(cleanup ??= CleanupCoreAsync());
        }

        private async Task CleanupCoreAsync()
        {
            Exception? firstFailure = null;
            try
            {
                foreach (IAsyncDisposable? resource in new IAsyncDisposable?[] { innerLease, inner, ownedIdentity })
                {
                    if (resource is null) continue;
                    try { await resource.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception failure) { firstFailure ??= failure; }
                }
            }
            finally
            {
                cancellation.Dispose();
                lock (owner.lifecycle)
                    if (ReferenceEquals(owner.active, this)) owner.active = null;
            }
            if (firstFailure is not null) ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    private sealed class Lease(Attempt attempt, ManagedTenantLocalEligibility image) : IManagedTenantLocalEligibilityLease
    {
        public ManagedTenantLocalEligibility Eligibility => image;
        public Task RequireCurrentAsync(CancellationToken cancellationToken) => attempt.RequireCurrentAsync(cancellationToken);
        public ValueTask DisposeAsync() => attempt.DisposeAsync();
    }
}
