using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

/// <summary>Selects only the configured identity writer while preserving the original Global erasure owner.</summary>
internal sealed class ConfiguredLocalIdentityEligibilityRevocation : ILocalIdentityEligibilityRevocation, IAsyncDisposable
{
    private readonly ExploreDbContext application;
    private readonly LocalIdentityExecutionContextCustody applicationCustody;
    private readonly IdentityDatabaseTopology topology;
    private readonly LocalIdentityWriterDbContextFactory? externalWriter;
    private readonly IPrivacyErasureAuthority authority;
    private readonly TimeProvider timeProvider;
    private readonly object lifecycle = new();
    private readonly CancellationTokenSource abandonment = new();
    private Attempt? active;
    private Task? disposal;
    private bool disposed;

    public ConfiguredLocalIdentityEligibilityRevocation(ExploreDbContext application,
        IdentityDatabaseTopology topology, LocalIdentityWriterDbContextFactory? externalWriter,
        IPrivacyErasureAuthority authority, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (!Enum.IsDefined(topology) || topology == IdentityDatabaseTopology.Colocated && externalWriter is not null
            || topology == IdentityDatabaseTopology.External && externalWriter?.Topology != IdentityDatabaseTopology.External)
            throw new ArgumentException("Local revocation requires its exact configured identity topology.", nameof(externalWriter));
        this.application = application;
        applicationCustody = LocalIdentityExecutionContextCustody.Capture(application);
        this.topology = topology;
        this.externalWriter = externalWriter;
        this.authority = authority;
        this.timeProvider = timeProvider;
    }

    public Task<IAsyncDisposable> AcquireAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active is not null)
                throw new InvalidOperationException("The original Local revocation attempt is already retained.");
            application.RequireBoundTenantLease();
            applicationCustody.RequireCurrent(application);
            if (application.TenantFilterTenantId is not null || application.IsTenantFilterBypassed
                || application.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Local revocation requires the original idle Global owner.");
            var attempt = new Attempt(this, intent, cancellationToken);
            active = attempt;
            return attempt.Start();
        }
    }

    public async Task InvalidateInCurrentTransactionAsync(PrivacyErasureIntent intent, CancellationToken cancellationToken)
    {
        Attempt attempt;
        lock (lifecycle)
        {
            if (disposed || active is not { } current || !current.IsAcquired || !ReferenceEquals(current.Intent, intent))
                throw new UnauthorizedAccessException("The original configured Local revocation lease is required.");
            attempt = current;
        }
        attempt.RequireCurrent();
        await attempt.Revocation!.InvalidateInCurrentTransactionAsync(intent, cancellationToken).ConfigureAwait(false);
        attempt.RequireCurrent();
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
            if (attempt is not null)
            {
                try { await attempt.Acquisition.ConfigureAwait(false); }
                catch (Exception) { /* Acquisition reports its own outcome; cleanup must still be joined. */ }
                await attempt.CleanupAsync().ConfigureAwait(false);
            }
        }
        finally { abandonment.Dispose(); }
    }

    private sealed class Attempt : IAsyncDisposable
    {
        private readonly ConfiguredLocalIdentityEligibilityRevocation owner;
        private readonly CancellationTokenSource cancellation;
        private readonly object cleanupLock = new();
        private HumanSecuritySourceGate? human;
        private ExternalIdentityDbContext? ownedIdentity;
        private IAsyncDisposable? innerLease;
        private Task? cleanup;
        private bool acquired;

        internal Attempt(ConfiguredLocalIdentityEligibilityRevocation owner, PrivacyErasureIntent intent, CancellationToken token)
        {
            this.owner = owner;
            Intent = intent;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, owner.abandonment.Token);
        }

        internal PrivacyErasureIntent Intent { get; }
        internal Task<IAsyncDisposable> Acquisition { get; private set; } = null!;
        internal LocalIdentityEligibilityRevocation? Revocation { get; private set; }
        internal bool IsAcquired => acquired && cleanup is null;

        internal Task<IAsyncDisposable> Start() => Acquisition = AcquireCoreAsync();

        private async Task<IAsyncDisposable> AcquireCoreAsync()
        {
            try
            {
                CancellationToken token = cancellation.Token;
                token.ThrowIfCancellationRequested();
                if (!HumanSecuritySourceGate.TryRequireCurrent(owner.application))
                {
                    human = new HumanSecuritySourceGate(owner.application);
                    _ = await human.AcquireAsync(token).ConfigureAwait(false);
                }
                HumanSecuritySourceGate.RequireCurrent(owner.application);
                DbContext identity = owner.application;
                if (owner.topology == IdentityDatabaseTopology.External)
                {
                    ownedIdentity = await owner.externalWriter!.CreateExternalIdentityAsync(token).ConfigureAwait(false);
                    identity = ownedIdentity;
                    HumanSecuritySourceGate.RequireCurrent(owner.application);
                }
                Revocation = new(identity, owner.application, owner.authority, owner.timeProvider);
                innerLease = await Revocation.AcquireAsync(Intent, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                HumanSecuritySourceGate.RequireCurrent(owner.application);
                lock (owner.lifecycle)
                {
                    ObjectDisposedException.ThrowIf(owner.disposed, owner);
                    if (!ReferenceEquals(owner.active, this))
                        throw new UnauthorizedAccessException("The original Local revocation issuer changed.");
                    acquired = true;
                }
                return new Lease(this);
            }
            catch
            {
                await CleanupAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal void RequireCurrent()
        {
            lock (owner.lifecycle)
                if (owner.disposed || !ReferenceEquals(owner.active, this) || !IsAcquired)
                    throw new UnauthorizedAccessException("The configured Local revocation lease is inactive.");
            HumanSecuritySourceGate.RequireCurrent(owner.application);
        }

        internal Task CleanupAsync()
        {
            lock (cleanupLock) return cleanup ??= CleanupCoreAsync();
        }

        public ValueTask DisposeAsync() => new(CleanupAsync());

        private async Task CleanupCoreAsync()
        {
            Exception? firstFailure = null;
            try
            {
                foreach (IAsyncDisposable? resource in new IAsyncDisposable?[] { innerLease, Revocation, ownedIdentity, human })
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

    private sealed class Lease(Attempt attempt) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(attempt.CleanupAsync());
    }
}
