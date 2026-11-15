using System.Data.Common;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed partial class LocalIdentityWriterPermissionsNativeTests
{
    [Test]
    public Task CredentialReplacementExcludesDispatchBeforeItsSerializableSnapshotAndThroughCommit() =>
        ExerciseConfiguredAuthenticationAsync(failAfterBookkeeping: false, dispatchSource: new());

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task FailedCredentialSettlementReleasesDispatchWithoutChangingTheSelectedCredential(bool beforeCommit) =>
        ExerciseConfiguredAuthenticationAsync(failAfterBookkeeping: false, dispatchSource: new()
        {
            Failure = new OperationCanceledException("The original credential transaction boundary was canceled."),
            FailBeforeCommit = beforeCommit
        });

    private sealed class DispatchSourceBeforeBeginProbe : DbTransactionInterceptor
    {
        private PrimaryDatabaseConnectionOptions options = null!;
        private ExploreDbContext? owner;
        internal bool BeforeBeginObserved { get; private set; }
        internal bool BeforeCommitObserved { get; private set; }
        internal OperationCanceledException? Failure { get; init; }
        internal bool FailBeforeCommit { get; init; }

        internal void Configure(PrimaryDatabaseConnectionOptions runtime) => options = runtime;
        internal void Arm(ExploreDbContext original) => owner = original;

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (owner is not null && ReferenceEquals(eventData.Context, owner))
            {
                await Assert.That(await TryAcquireAndReleaseAsync(cancellationToken)).IsFalse()
                    .Because("Credential replacement must exclude dispatch before establishing its Serializable snapshot.");
                BeforeBeginObserved = true;
                if (Failure is not null && !FailBeforeCommit) throw Failure;
            }
            return result;
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (owner is not null && ReferenceEquals(eventData.Context, owner))
            {
                await Assert.That(await TryAcquireAndReleaseAsync(cancellationToken)).IsFalse()
                    .Because("Dispatch exclusion must remain held through the original credential commit.");
                BeforeCommitObserved = true;
                if (Failure is not null && FailBeforeCommit) throw Failure;
            }
            return result;
        }

        internal async Task RequireAvailableAsync(CancellationToken token) =>
            await Assert.That(await TryAcquireAndReleaseAsync(token)).IsTrue()
                .Because("The settled native mutation must release dispatch exclusion without retiring its selected owner.");

        private async Task<bool> TryAcquireAndReleaseAsync(CancellationToken token)
        {
            await using var observer = CreateContext(options, LocalIdentityWriterInventory.Store.Application);
            await observer.Database.OpenConnectionAsync(token);
            await RequireSessionAsync(observer, options.Username!);
            long key = RelationalNamedLock.ComputeStableKey(EmailDispatchOutboxRepository.ClaimAdvisoryLockName);
            await using var acquire = Command(observer, "SELECT pg_catalog.pg_try_advisory_lock(@key)");
            var parameter = acquire.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = key;
            acquire.Parameters.Add(parameter);
            bool acquired = (bool)(await acquire.ExecuteScalarAsync(token))!;
            if (acquired)
            {
                await using var release = Command(observer, "SELECT pg_catalog.pg_advisory_unlock(@key)");
                var releasedKey = release.CreateParameter();
                releasedKey.ParameterName = "key";
                releasedKey.Value = key;
                release.Parameters.Add(releasedKey);
                await Assert.That((bool)(await release.ExecuteScalarAsync(token))!).IsTrue();
            }
            return acquired;
        }
    }
}
