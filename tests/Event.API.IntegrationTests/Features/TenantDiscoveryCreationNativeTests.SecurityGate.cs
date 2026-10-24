using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class TenantDiscoveryCreationNativeTests
{
    [Test]
    public async Task OriginalHumanCreatorHoldsHumanCustodyBeforeEnrollmentAndThroughSettlement()
    {
        await database.ResetAsync();
        var probe = new HumanCreatorGateProbe();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, probe, services => services.AddScoped<IEventDiscoveryEnrollmentGate>(provider =>
                new ObservedCreatorEnrollment(provider.GetRequiredService<ExploreDbContext>(),
                    provider.GetRequiredService<ExploreDbContextExecutionLease>(), probe)));
        probe.Armed = true;

        _ = await CreateAndVerifyAsync(authentication, Request());

        await Assert.That(probe.ObservedBeforeEnrollment).IsTrue();
        await Assert.That(probe.ObservedBeforeBegin).IsTrue();
        await Assert.That(probe.ObservedCommit).IsTrue();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(probe.OriginalContext!)).IsFalse();
    }

    /// <summary>Observation only: the unchanged production enrollment gate owns acquisition and cleanup.</summary>
    private sealed class ObservedCreatorEnrollment(ExploreDbContext context, ExploreDbContextExecutionLease selection,
        HumanCreatorGateProbe observation)
        : IEventDiscoveryEnrollmentGate, IAsyncDisposable
    {
        private readonly EventDiscoveryEnrollmentGate original = new(context);

        public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            selection.RequireOriginalOwner(context);
            if (selection.PolicyPurpose != PolicyMutationPurpose.TenantCreation
                || selection.HumanScope?.Purpose != HumanAdministratorCommitPurpose.TenantCreation
                || selection.SelectedTenantId is not null || selection.HumanLifetime is null)
                throw new InvalidOperationException("human_creator_test_dedicated_purpose_missing");
            observation.BeforeEnrollment(context);
            return original.AcquireAsync(cancellationToken);
        }

        public void RequireCurrent() => original.RequireCurrent();

        public bool TryRequireCurrent() => original.TryRequireCurrent();

        public ValueTask DisposeAsync() => original.DisposeAsync();
    }

    private sealed class HumanCreatorGateProbe : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal bool ObservedBeforeEnrollment { get; private set; }
        internal bool ObservedBeforeBegin { get; private set; }
        internal bool ObservedCommit { get; private set; }
        internal ExploreDbContext? OriginalContext { get; private set; }
        private DbConnection? connector;

        internal void BeforeEnrollment(ExploreDbContext context)
        {
            if (!Armed) return;
            HumanSecuritySourceGate.RequireCurrent(context);
            if (!context.HasHumanCommitContextLease || context.TenantFilterTenantId is not null
                || context.IsTenantFilterBypassed || context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("human_creator_test_original_idle_global_missing");
            RequireOriginal(context, context.Database.GetDbConnection());
            ObservedBeforeEnrollment = true;
        }

        private void RequireOriginal(ExploreDbContext context, DbConnection connection)
        {
            HumanSecuritySourceGate.RequireCurrent(context);
            if (OriginalContext is not null && (!ReferenceEquals(OriginalContext, context)
                    || !ReferenceEquals(connector, connection)))
                throw new InvalidOperationException("human_creator_test_gate_owner_changed");
            OriginalContext ??= context;
            connector ??= connection;
        }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, OriginalContext))
            {
                RequireOriginal(OriginalContext!, connection);
                if (!ObservedBeforeEnrollment)
                    throw new InvalidOperationException("human_creator_test_gate_observed_after_enrollment");
                ObservedBeforeBegin = true;
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, OriginalContext))
            {
                RequireOriginal(OriginalContext!, transaction.Connection!);
                if (!ObservedBeforeBegin
                    || !ReferenceEquals(OriginalContext!.Database.CurrentTransaction?.GetDbTransaction(), transaction))
                    throw new InvalidOperationException("human_creator_test_original_settlement_changed");
                ObservedCommit = true;
            }
            return ValueTask.FromResult(result);
        }
    }
}
