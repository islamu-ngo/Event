using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using System.Runtime.ExceptionServices;

namespace Explore.Persistence.Identity;

/// <summary>Owns credential receipt observation without moving the original Global completion owner.</summary>
internal sealed class ConfiguredLocalBootstrapReceiptSource : ILocalBootstrapReceiptSource, IAsyncDisposable
{
    private readonly ExploreDbContext application;
    private readonly Func<LocalIdentityWriterDbContextFactory> writers;
    private readonly LocalIdentityExecutionContextCustody applicationCustody;
    private readonly object lifecycle = new();
    private readonly CancellationTokenSource abandonment = new();
    private Attempt? active;
    private Task? disposal;
    private bool disposed;

    internal ConfiguredLocalBootstrapReceiptSource(
        ExploreDbContext originalGlobal, Func<LocalIdentityWriterDbContextFactory> writers)
    {
        application = originalGlobal;
        this.writers = writers;
        applicationCustody = LocalIdentityExecutionContextCustody.Capture(application);
    }

    public Task<ILocalBootstrapReceiptLease> AcquireAsync(
        LocalCredentialProvisioningSnapshot expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active is not null)
                throw new InvalidOperationException("The original bootstrap receipt is already retained.");
            RequireApplication();
            if (application.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("Bootstrap receipt custody must precede the original transaction.");
            if (expected.Receipt.Kind != LocalCredentialOperationKind.Create
                || expected.Receipt.Stage != LocalCredentialOperationStage.ProvisioningPending || !expected.EmailVerified)
                throw new UnauthorizedAccessException("A pending Local bootstrap creation receipt is required.");
            var attempt = new Attempt(this, expected, cancellationToken);
            active = attempt;
            return attempt.Start();
        }
    }

    public Task RequireCurrentAsync(ILocalBootstrapReceiptLease lease,
        LocalCredentialProvisioningSnapshot expected, CancellationToken cancellationToken) =>
        RequireIssued(lease, expected).RequireSnapshotAsync(cancellationToken);

    public void RequireHeld(ILocalBootstrapReceiptLease lease, LocalCredentialProvisioningSnapshot expected) =>
        RequireIssued(lease, expected).RequireHeld();

    private Attempt RequireIssued(ILocalBootstrapReceiptLease lease, LocalCredentialProvisioningSnapshot expected)
    {
        if (lease is not Lease issued || !ReferenceEquals(issued.Attempt.Owner, this)
            || !ReferenceEquals(issued.Attempt.Expected, expected))
            throw new UnauthorizedAccessException("The original issued bootstrap receipt lease is required.");
        return issued.Attempt;
    }

    private void RequireApplication()
    {
        application.RequireBoundTenantLease();
        applicationCustody.RequireCurrent(application);
        if (application.TenantFilterTenantId is not null || application.IsTenantFilterBypassed)
            throw new UnauthorizedAccessException("Bootstrap completion requires its original null-tenant owner.");
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
                catch (Exception) { /* Acquisition owns its outcome; disposal still joins cleanup. */ }
                await attempt.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally { abandonment.Dispose(); }
    }

    private sealed class Attempt(
        ConfiguredLocalBootstrapReceiptSource owner, LocalCredentialProvisioningSnapshot expected,
        CancellationToken token) : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, owner.abandonment.Token);
        private readonly object cleanupLock = new();
        private HumanSecuritySourceGate? human;
        private ExternalIdentityDbContext? external;
        private LocalIdentityWriterDbContextFactory.Contexts? pair;
        private DbContext? identity;
        private LocalIdentityLifecycleGateCustody? credentialGate;
        private Task? cleanup;
        private bool acquired;
        internal ConfiguredLocalBootstrapReceiptSource Owner => owner;
        internal LocalCredentialProvisioningSnapshot Expected => expected;
        internal Task<ILocalBootstrapReceiptLease> Acquisition { get; private set; } = null!;
        internal Task<ILocalBootstrapReceiptLease> Start() => Acquisition = AcquireCoreAsync();

        private async Task<ILocalBootstrapReceiptLease> AcquireCoreAsync()
        {
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!HumanSecuritySourceGate.TryRequireCurrent(owner.application))
                {
                    human = new HumanSecuritySourceGate(owner.application);
                    _ = await human.AcquireAsync(cancellation.Token).ConfigureAwait(false);
                }
                var writers = owner.writers();
                if (writers.Topology == IdentityDatabaseTopology.External)
                    identity = external = await writers.CreateExternalIdentityAsync(cancellation.Token).ConfigureAwait(false);
                else
                {
                    pair = await writers.CreateAsync(null, cancellation.Token).ConfigureAwait(false);
                    identity = pair.Identity;
                }
                owner.RequireApplication();
                HumanSecuritySourceGate.RequireCurrent(owner.application);
                credentialGate = await LocalIdentityLifecycleGateCustody.EnterAsync(identity, cancellation.Token).ConfigureAwait(false);
                lock (owner.lifecycle)
                {
                    ObjectDisposedException.ThrowIf(owner.disposed, owner);
                    if (!ReferenceEquals(owner.active, this))
                        throw new UnauthorizedAccessException("The original bootstrap issuer changed.");
                    acquired = true;
                }
                await RequireSnapshotAsync(cancellation.Token).ConfigureAwait(false);
                return new Lease(this);
            }
            catch
            {
                await CleanupAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal async Task RequireSnapshotAsync(CancellationToken token)
        {
            RequireCustody();
            token.ThrowIfCancellationRequested();
            cancellation.Token.ThrowIfCancellationRequested();
            var operation = await identity!.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .Where(candidate => candidate.Id == expected.Receipt.OperationId)
                .Select(candidate => new
                {
                    candidate.Id,
                    candidate.Kind,
                    candidate.Stage,
                    candidate.InitiatingApplicationUserId,
                    candidate.LocalSubjectId,
                    candidate.PersonalActorId,
                    candidate.ExternalLoginId,
                    candidate.CreatedAt,
                    candidate.ConcurrencyStamp
                }).SingleOrDefaultAsync(token).ConfigureAwait(false);
            if (operation is null)
                throw new UnauthorizedAccessException("The original Local bootstrap receipt is missing.");
            var user = await identity.Set<LocalIdentityUser>().AsNoTracking()
                .Where(candidate => candidate.Id == operation.LocalSubjectId)
                .Select(candidate => new { candidate.Email, candidate.FirstName, candidate.LastName, candidate.EmailConfirmed, candidate.UserName })
                .SingleOrDefaultAsync(token).ConfigureAwait(false);
            if (user is null || string.IsNullOrWhiteSpace(user.UserName) || string.IsNullOrWhiteSpace(user.FirstName))
                throw new UnauthorizedAccessException("The original Local bootstrap profile is missing.");
            var current = new LocalCredentialProvisioningSnapshot(
                new LocalCredentialOperationReceipt(operation.Id, operation.Kind, operation.Stage,
                    operation.InitiatingApplicationUserId, operation.LocalSubjectId, operation.PersonalActorId,
                    operation.ExternalLoginId, operation.CreatedAt),
                operation.ConcurrencyStamp, user.Email, user.FirstName, user.LastName, user.EmailConfirmed, user.UserName);
            RequireCustody();
            token.ThrowIfCancellationRequested();
            cancellation.Token.ThrowIfCancellationRequested();
            if (current != expected)
                throw new UnauthorizedAccessException("The original Local bootstrap receipt changed.");
        }

        private void RequireCustody()
        {
            lock (owner.lifecycle)
                if (owner.disposed || !ReferenceEquals(owner.active, this) || !acquired || cleanup is not null)
                    throw new UnauthorizedAccessException("The original bootstrap receipt lease is inactive.");
            owner.RequireApplication();
            HumanSecuritySourceGate.RequireCurrent(owner.application);
            credentialGate!.RequireCurrent();
        }

        internal void RequireHeld()
        {
            RequireCustody();
            cancellation.Token.ThrowIfCancellationRequested();
        }

        internal Task CleanupAsync()
        {
            lock (cleanupLock) return cleanup ??= CleanupCoreAsync();
        }

        public ValueTask DisposeAsync() => new(CleanupAsync());

        private async Task CleanupCoreAsync()
        {
            Exception? first = null;
            try
            {
                foreach (IAsyncDisposable? resource in new IAsyncDisposable?[] { credentialGate, external, pair, human })
                {
                    if (resource is null) continue;
                    try { await resource.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception failure) { first ??= failure; }
                }
            }
            finally
            {
                cancellation.Dispose();
                lock (owner.lifecycle)
                    if (ReferenceEquals(owner.active, this)) owner.active = null;
            }
            if (first is not null) ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    private sealed class Lease(Attempt attempt) : ILocalBootstrapReceiptLease
    {
        internal Attempt Attempt => attempt;
        public ValueTask DisposeAsync() => attempt.DisposeAsync();
    }
}
