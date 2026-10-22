using System.Data;
using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    internal IExecutionStrategy CreateAuthenticationExecutionStrategy() =>
        identityDbContext.Database.CreateExecutionStrategy();

    internal async Task<AuthenticationAttempt> BeginAuthenticationAsync(CancellationToken token)
    {
        EnsureReconciliationContextsIdle();
        var lifecycle = await EnterLifecycleAsync(identityDbContext, applicationDbContext, token).ConfigureAwait(false);
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await identityDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token)
                .ConfigureAwait(false);
            lifecycle.RequireCurrent();
            return new(this, lifecycle, transaction);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (transaction is not null)
                try { await transaction.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { failures.Add(cleanup); }
            try { await lifecycle.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count > 1)
                throw new AggregateException("Authentication startup and its original owner cleanup failed.", failures);
            throw;
        }
    }

    internal async Task PublishAuthenticationLockoutAsync(LocalIdentityUser user, CancellationToken token)
    {
        RequireEligibilityOwner();
        LocalCredentialStateMetadata? metadata = await ReadAsync(user.Id, user.SecurityStamp!, token).ConfigureAwait(false);
        if (metadata is null)
            throw new UnauthorizedAccessException("The authenticating Local credential has no current operation.");
        var operation = await identityDbContext.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == metadata.OperationId, token).ConfigureAwait(false);
        RequireEligibilityOwner();
        if (operation is null || operation.LocalSubjectId != user.Id || !IsCurrentMetadata(operation, metadata))
            throw new UnauthorizedAccessException("The authenticating Local credential operation changed.");
        DateTime? deadline = user.LockoutEnabled ? user.LockoutEnd?.UtcDateTime : null;
        await ChangeEligibilityAsync(user.Id, operation.Id, row =>
        {
            LocalIdentityEligibilityState expectedState = metadata.State switch
            {
                LocalCredentialState.ProvisioningPending => LocalIdentityEligibilityState.ProvisioningPending,
                LocalCredentialState.ChangeRequired => LocalIdentityEligibilityState.ChangeRequired,
                LocalCredentialState.Ready => LocalIdentityEligibilityState.Ready,
                _ => throw new UnauthorizedAccessException("The authenticating Local credential state is unsupported.")
            };
            if (row.State != expectedState)
                throw new UnauthorizedAccessException("The current Local eligibility state differs from its credential.");
            if (row.PersonalActorId != operation.PersonalActorId || row.ExternalLoginId != operation.ExternalLoginId)
                throw new UnauthorizedAccessException("The current Local eligibility binding differs from its operation.");
            row.SetLockout(row.Generation, operation.Id, deadline, timeProvider.GetUtcNow().UtcDateTime);
        }, token).ConfigureAwait(false);
    }

    internal async Task<string?> ReadEmailDeliveryEnabledValueAsync(CancellationToken token)
    {
        applicationDbContext.RequireBoundTenantLease();
        return await applicationDbContext.SystemSettings.AsNoTracking()
            .Where(value => value.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)
            .Select(value => value.Value).SingleOrDefaultAsync(token).ConfigureAwait(false);
    }

    internal sealed class AuthenticationAttempt(LocalIdentityCredentialStateStore owner,
        LocalIdentityLifecycleExecutionCustody lifecycle, IDbContextTransaction transaction) : IAsyncDisposable
    {
        private int disposed;
        private readonly DbContextId identityContextId = owner.IdentityOwner.ContextId;
        private readonly LocalIdentityExecutionContextCustody identityCustody =
            LocalIdentityExecutionContextCustody.Capture(owner.IdentityOwner);
        private readonly DbContextId applicationContextId = owner.ApplicationOwner.ContextId;
        private readonly IDbContextTransaction? applicationTransaction = owner.ApplicationOwner.Database.CurrentTransaction;

        internal async Task<LocalCredentialReplacementSubject?> ReadReplacementSubjectAsync(
            Guid localSubjectId, string expectedSecurityStamp, CancellationToken token)
        {
            RequireOriginalRead(token);
            var subject = await owner.ReadReplacementSubjectCoreAsync(localSubjectId, expectedSecurityStamp, token)
                .ConfigureAwait(false);
            RequireOriginalRead(token);
            return subject;
        }

        private void RequireOriginalRead(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            lifecycle.RequireCurrent();
            owner.RequireEligibilityOwner();
            identityCustody.RequireCurrent(owner.IdentityOwner);
            if (owner.IdentityOwner.ContextId != identityContextId || owner.ApplicationOwner.ContextId != applicationContextId
                || !ReferenceEquals(owner.IdentityOwner.Database.CurrentTransaction, transaction)
                || !ReferenceEquals(owner.ApplicationOwner.Database.CurrentTransaction, applicationTransaction))
                throw new UnauthorizedAccessException("Authentication lost its original replacement-subject read owner.");
        }

        internal async Task CommitAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            owner.RequireEligibilityOwner();
            if (!ReferenceEquals(owner.IdentityOwner.Database.CurrentTransaction, transaction))
                throw new UnauthorizedAccessException("Authentication lost its original identity transaction.");
            await lifecycle.CommitAsync(transaction, token).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            var failures = new List<Exception>();
            try { await transaction.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { failures.Add(cleanup); }
            try { owner.IdentityOwner.ChangeTracker.Clear(); }
            catch (Exception cleanup) { failures.Add(cleanup); }
            try { await lifecycle.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("The original authentication owner's cleanup failed.", failures);
        }
    }
}
