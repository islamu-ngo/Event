using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class TenantDiscoveryCreationNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalHumanCreatorHoldsHumanCustodyBeforeEnrollmentAndThroughSettlement(bool assignAdministrator)
    {
        await database.ResetAsync();
        var probe = new HumanCreatorGateProbe();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, probe, services => services.AddScoped<IEventDiscoveryEnrollmentGate>(provider =>
                new ObservedCreatorEnrollment(provider.GetRequiredService<ExploreDbContext>(),
                    provider.GetRequiredService<ExploreDbContextExecutionLease>(), probe)));
        probe.Armed = true;

        _ = await CreateAndVerifyAsync(authentication, Request(assignAdministrator));

        await Assert.That(probe.ObservedBeforeEnrollment).IsTrue();
        await Assert.That(probe.ObservedBeforeBegin).IsTrue();
        await Assert.That(probe.ObservedCommit).IsTrue();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(probe.OriginalContext!)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EnrollmentAcquisitionFailureReleasesOriginalHumanCustodyWithoutCreatingATenant(bool disconnectBeforeRejection)
    {
        await database.ResetAsync();
        var probe = new HumanCreatorGateProbe();
        var failure = new CreatorCleanupFailureObserver(probe);
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, probe, services =>
            {
                services.AddScoped<IEventDiscoveryEnrollmentGate>(provider =>
                    new ObservedCreatorEnrollment(provider.GetRequiredService<ExploreDbContext>(),
                        provider.GetRequiredService<ExploreDbContextExecutionLease>(), probe,
                        rejectAcquisition: true, disconnectBeforeRejection: disconnectBeforeRejection));
                services.Insert(0, ServiceDescriptor.Singleton<IExceptionHandler>(failure));
            });
        var tenantsBefore = await ReadTenantsAsync();
        var revisionsBefore = await ReadRevisionsAsync();
        var effectsBefore = await ReadCreationEffectsAsync();
        probe.Armed = true;

        using var response = await authentication.CreateTenantViaApiAsync(Request(assignAdministrator: true), Token);

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.InternalServerError);
        await Assert.That(probe.ObservedBeforeEnrollment).IsTrue();
        await Assert.That(probe.ObservedBeforeBegin).IsFalse();
        await Assert.That(failure.Classification).IsEqualTo(disconnectBeforeRejection
            ? typeof(AggregateException) : typeof(InvalidOperationException));
        await Assert.That(failure.CompleteChain).IsTrue();
        await Assert.That(failure.SawAcquisitionFault).IsTrue();
        await Assert.That(failure.SawHumanCleanupFailure).IsEqualTo(disconnectBeforeRejection);
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(probe.OriginalContext!)).IsFalse();
        await Assert.That(await ReadTenantsAsync()).IsEquivalentTo(tenantsBefore);
        await Assert.That(await ReadRevisionsAsync()).IsEquivalentTo(revisionsBefore);
        await AssertExistingEffectsUnchangedAsync(effectsBefore);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PostCommitDisconnectPreservesSettlementClassificationAndEveryOwnedCleanupCause(bool throwAfterCommit)
    {
        await database.ResetAsync();
        var probe = new HumanCreatorGateProbe { DisconnectAfterCommit = true, ThrowAfterCommit = throwAfterCommit };
        var failure = new CreatorCleanupFailureObserver(probe);
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, probe, services =>
            {
                services.AddScoped<IEventDiscoveryEnrollmentGate>(provider =>
                    new ObservedCreatorEnrollment(provider.GetRequiredService<ExploreDbContext>(),
                        provider.GetRequiredService<ExploreDbContextExecutionLease>(), probe));
                services.Insert(0, ServiceDescriptor.Singleton<IExceptionHandler>(failure));
            });
        var request = Request(assignAdministrator: true);
        var tenantsBefore = await ReadTenantsAsync();
        var revisionsBefore = await ReadRevisionsAsync();
        var effectsBefore = await ReadCreationEffectsAsync();
        probe.Armed = true;

        using var response = await authentication.CreateTenantViaApiAsync(request, Token);

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.InternalServerError);
        await Assert.That(probe.PostCommitNotifications).IsEqualTo(1);
        await Assert.That(failure.Classification).IsEqualTo(throwAfterCommit
            ? typeof(HumanMutationOutcomeUnknownException) : typeof(TransactionCommittedCleanupException));
        await Assert.That(failure.CompleteChain).IsTrue();
        await Assert.That(failure.SawCommitFault).IsEqualTo(throwAfterCommit);
        await Assert.That(failure.SawDiscoveryCleanupFailure).IsTrue();
        await Assert.That(failure.SawHumanCleanupFailure).IsTrue();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(probe.OriginalContext!)).IsFalse();
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(probe.OriginalContext!)).IsFalse();

        // The response failed after a real COMMIT; a fresh authority must still see the entire graph.
        await using var observer = OpenMigrator();
        var committed = await observer.Tenants.AsNoTracking().SingleAsync(row => row.Slug == request.Slug, Token);
        await Assert.That(committed.FullName).IsEqualTo(request.FullName);
        await Assert.That(committed.CreatedBy).IsEqualTo(authentication.ActorId);
        await Assert.That((await ReadTenantsAsync()).Where(row => row.Id != committed.Id)).IsEquivalentTo(tenantsBefore);
        var revisionsAfter = await ReadRevisionsAsync();
        var revision = revisionsAfter.Single(row => row.TenantId == committed.Id);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(0L);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(1L);
        await Assert.That(revisionsAfter.Where(row => row.TenantId != committed.Id)).IsEquivalentTo(revisionsBefore);
        await VerifyCreationEffectsAsync(authentication, request, committed.Id);
        await AssertExistingEffectsUnchangedAsync(effectsBefore, committed.Id);
    }

    /// <summary>Records only known fault identities and custody codes; production still owns the HTTP response.</summary>
    private sealed class CreatorCleanupFailureObserver(HumanCreatorGateProbe probe) : IExceptionHandler
    {
        internal Type? Classification { get; private set; }
        internal bool CompleteChain { get; private set; }
        internal bool SawCommitFault { get; private set; }
        internal bool SawAcquisitionFault { get; private set; }
        internal bool SawDiscoveryCleanupFailure { get; private set; }
        internal bool SawHumanCleanupFailure { get; private set; }

        public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            if (!probe.Armed || probe.OriginalContext is null) return ValueTask.FromResult(false);
            Classification = exception.GetType();
            var pending = new Stack<Exception>();
            var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            pending.Push(exception);
            while (pending.Count != 0 && seen.Count < 64)
            {
                var cause = pending.Pop();
                if (!seen.Add(cause)) continue;
                SawCommitFault |= ReferenceEquals(cause, probe.CommitFault);
                SawAcquisitionFault |= ReferenceEquals(cause, probe.AcquisitionFault);
                SawDiscoveryCleanupFailure |= cause.Message == "discovery_enrollment_cleanup_lost_custody";
                SawHumanCleanupFailure |= cause.Message == "human_security_source_cleanup_lost_custody";
                if (cause is AggregateException aggregate)
                    foreach (var inner in aggregate.InnerExceptions) pending.Push(inner);
                else if (cause.InnerException is { } nested) pending.Push(nested);
            }
            CompleteChain = pending.Count == 0;
            return ValueTask.FromResult(false);
        }
    }

    /// <summary>Observation only: the unchanged production enrollment gate owns acquisition and cleanup.</summary>
    private sealed class ObservedCreatorEnrollment(ExploreDbContext context, ExploreDbContextExecutionLease selection,
        HumanCreatorGateProbe observation, bool rejectAcquisition = false, bool disconnectBeforeRejection = false)
        : IEventDiscoveryEnrollmentGate, IAsyncDisposable
    {
        private readonly EventDiscoveryEnrollmentGate original = new(context);

        public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            selection.RequireOriginalOwner(context);
            if (selection.PolicyPurpose != PolicyMutationPurpose.TenantCreation
                || selection.HumanScope?.Purpose != HumanAdministratorCommitPurpose.TenantCreation
                || selection.SelectedTenantId is not null || selection.HumanLifetime is null)
                throw new InvalidOperationException("human_creator_test_dedicated_purpose_missing");
            observation.BeforeEnrollment(context);
            if (rejectAcquisition)
            {
                if (disconnectBeforeRejection) await context.Database.GetDbConnection().CloseAsync();
                throw observation.AcquisitionFault;
            }
            return await original.AcquireAsync(cancellationToken);
        }

        public void RequireCurrent() => original.RequireCurrent();

        public bool TryRequireCurrent() => original.TryRequireCurrent();

        public ValueTask DisposeAsync() => original.DisposeAsync();
    }

    private sealed class HumanCreatorGateProbe : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal bool DisconnectAfterCommit { get; init; }
        internal bool ThrowAfterCommit { get; init; }
        internal int PostCommitNotifications { get; private set; }
        internal InvalidOperationException AcquisitionFault { get; } = new("human_creator_test_enrollment_acquisition_rejected");
        internal Exception CommitFault { get; } = new CreatorCommitNotificationFailure();
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

        public override async Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(eventData.Context, OriginalContext) || OriginalContext is null) return;
            RequireOriginal(OriginalContext, OriginalContext.Database.GetDbConnection());
            EventDiscoveryEnrollmentGate.RequireCurrent(OriginalContext);
            PostCommitNotifications++;
            if (DisconnectAfterCommit) await OriginalContext.Database.GetDbConnection().CloseAsync();
            if (ThrowAfterCommit) throw CommitFault;
        }
    }

    private sealed class CreatorCommitNotificationFailure : Exception { }
}
