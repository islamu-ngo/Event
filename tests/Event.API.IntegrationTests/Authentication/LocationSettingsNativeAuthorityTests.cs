using Event.Persistence.IntegrationTests.Fixtures;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Messaging;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Application.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Api.IntegrationTests.Authentication;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class LocationSettingsNativeAuthorityTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    [Arguments("native")]
    [Arguments("foreign")]
    [Arguments("forged")]
    [Arguments("stale")]
    [Arguments("expired")]
    [Arguments("replaced")]
    [Arguments("old_snapshot")]
    [Arguments("missing")]
    [Arguments("erased_source")]
    public async Task ExternalCorrectionRequiresRetainedNativeSourceAndExactClaim(string disposition)
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(postgres, instance: true);
        await Assert.That((await rows.ApplyAsync()).Accepted).IsTrue();
        await using var observer = rows.Observer();
        var message = await observer.OutboxMessages.AsNoTracking().SingleAsync(row => row.AggregateId == rows.A.Id
            && row.EventType == LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType);
        var outbox = new OutboxRepository(observer);
        DateTime lease = (await outbox.TryClaimForProcessing(message.Id, DateTime.UtcNow))!.Value;
        message = await observer.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        var retained = await observer.Set<LocationPrivacyCorrectionReceipt>().AsNoTracking().SingleAsync(row => row.Id == message.Id);
        await Assert.That(retained.EventId).IsEqualTo(rows.A.EventId);
        await Assert.That(retained.DisclosureAuditId).IsNotNull();
        await Assert.That((await observer.EventLocationDisclosureAudits.AsNoTracking()
            .SingleAsync(row => row.Id == retained.DisclosureAuditId)).CorrectionMessageId).IsEqualTo(message.Id);
        if (disposition == "stale") await outbox.TryClaimForProcessing(message.Id, lease);
        if (disposition is "forged" or "missing")
        {
            message.Id = Guid.CreateVersion7();
            if (disposition == "missing")
            {
                message.Status = OutboxMessageStatus.Pending;
                message.NextRetryAt = null;
                await outbox.Create(message);
                await outbox.TryClaimForProcessing(message.Id, DateTime.UtcNow);
                message = await observer.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
            }
        }
        if (disposition is "replaced" or "old_snapshot")
        {
            string replacement = message.Payload!.Replace(rows.A.EventId.ToString(), rows.B.EventId.ToString(), StringComparison.Ordinal);
            await outbox.TryReplaceProcessingPayloadAsync(message.Id, lease, message.Payload!, replacement);
            if (disposition == "replaced")
                message = await observer.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        }
        if (disposition == "erased_source")
        {
            await observer.Set<LocationPii>().Where(row => row.LocationId == rows.A.LocationId).ExecuteDeleteAsync();
            observer.ChangeTracker.Clear();
        }
        await using var consumer = rows.Ordinary(disposition == "foreign" ? rows.TenantB : rows.TenantA);
        if (disposition == "missing")
        {
            await using var transaction = await consumer.Database.BeginTransactionAsync();
            var clone = await Assert.ThrowsAsync<PostgresException>(() => consumer.Database.ExecuteSqlRawAsync(
                $"SELECT {PostgresLocationPrivacyGovernanceContract.RetainReceiptFunctionSql(consumer)}({{0}}, {{1}}, {{2}})",
                rows.TenantA, message.Id, retained.DisclosureAuditId!.Value));
            await Assert.That(clone!.SqlState).IsEqualTo("42501");
            await transaction.RollbackAsync();
        }
        var planner = new CorrectionObserver();
        var dispatcher = new LocationPrivacyCorrectionDispatcher(rows.Services.GetRequiredService<HybridCache>(),
            planner, rows.Services.GetRequiredService<EventLocationPrivacyMetrics>(), new LocationPrivacyCorrectionReceiptRepository(
                consumer, disposition == "expired" ? new ClaimClock(lease) : TimeProvider.System));
        if (disposition is "native" or "erased_source")
        {
            await dispatcher.DispatchAsync(message);
            await dispatcher.DispatchAsync(message);
            await Assert.That(planner.Inputs.Select(input => input.EventId)).IsEquivalentTo([rows.A.EventId, rows.A.EventId]);
            await Assert.That(planner.Inputs.All(input => input.TenantId == rows.TenantA && input.CorrectionId == retained.Id)).IsTrue();
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(message));
            await Assert.That(planner.Inputs).IsEmpty();
        }
    }

    [Test]
    public async Task RetainedNativeReceiptCannotBeReplacedOrForkedEvenByTableAdministration()
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(postgres, instance: true);
        await Assert.That((await rows.ApplyAsync()).Accepted).IsTrue();
        await using var observer = rows.Observer();
        var original = await observer.Set<LocationPrivacyCorrectionReceipt>().AsNoTracking()
            .SingleAsync(row => row.EventLocationId == rows.A.Id);
        var replacement = await Assert.ThrowsAsync<PostgresException>(() =>
            observer.Set<LocationPrivacyCorrectionReceipt>().Where(row => row.Id == original.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.EventId, rows.B.EventId)));
        await Assert.That(replacement!.SqlState).IsEqualTo("42501");
        var audit = await observer.EventLocationDisclosureAudits.AsNoTracking().SingleAsync(row => row.Id == original.DisclosureAuditId);
        Guid forkId = Guid.CreateVersion7();
        await Assert.That(() => audit.BindCorrection(forkId)).Throws<InvalidOperationException>();
        var invented = EventLocationDisclosureAudit.Create(original.TenantId, original.EventLocationId, rows.Actor,
            EventLocationDisclosureFields.None, EventLocationDisclosureFields.None,
            LocationDisclosureAudienceEnum.Never, LocationDisclosureAudienceEnum.Never, null, null,
            original.PolicyVersion - 1, original.PolicyVersion, EventLocationDisclosureAuditReasonEnum.GovernanceTightening,
            original.CreatedAtUtc);
        invented.BindCorrection(forkId);
        observer.Add(LocationPrivacyCorrectionReceipt.ForGovernance(forkId, rows.A.EventId, invented, original.CreatedAtUtc));
        var fork = await Assert.ThrowsAsync<DbUpdateException>(() => observer.SaveChangesAsync());
        await Assert.That((fork!.InnerException as PostgresException)!.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstanceTighteningCorrectsBothTenantsWithoutWideningTheOrdinaryLease(bool foreignTenant)
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(postgres, instance: true);
        Guid? tenant = foreignTenant ? rows.TenantB : null;
        await using var ordinary = rows.Ordinary(tenant);
        await AssertOrdinaryAsync(ordinary);
        await Assert.That(await ordinary.EventLocations.IgnoreQueryFilters().AnyAsync(row => row.TenantId == rows.TenantA)).IsFalse();
        var denied = await Assert.ThrowsAsync<PostgresException>(() => rows.Owner.EventLocations.IgnoreQueryFilters().AnyAsync());
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        var result = await rows.ApplyAsync();
        await Assert.That(result.Accepted).IsTrue();
        await Assert.That(result.CorrectedProjections.Select(row => row.EventLocationId)).IsEquivalentTo([rows.A.Id, rows.B.Id]);
        await Assert.That(ordinary.TenantFilterTenantId).IsEqualTo(tenant);
        await rows.AssertStateAsync([rows.A.Id, rows.B.Id], committed: true);
        await Assert.That(await ordinary.EventLocations.IgnoreQueryFilters().AnyAsync(row => row.TenantId == rows.TenantA)).IsFalse();
    }

    [Test]
    public async Task TenantTighteningLeavesForeignPolicyAuditAndOutboxUntouched()
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(postgres, instance: false);
        await AssertOrdinaryAsync(rows.Owner);
        var result = await rows.ApplyAsync();
        await Assert.That(result.Accepted).IsTrue();
        await Assert.That(result.CorrectedProjections.Select(row => row.EventLocationId)).IsEquivalentTo([rows.A.Id]);
        await rows.AssertStateAsync([rows.A.Id], committed: true);
        await Assert.That(await rows.Owner.EventLocations.IgnoreQueryFilters().AnyAsync(row => row.TenantId == rows.TenantB)).IsFalse();
    }

    [Test]
    public async Task CallerRollbackRevertsSettingBothPoliciesAuditsAndOutboxInTheSameTransaction()
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(postgres, instance: true);
        await using var ordinary = rows.Ordinary(rows.TenantA);
        await AssertOrdinaryAsync(ordinary);
        LocationPrivacyGovernanceMutationResult? captured = null;
        await Assert.ThrowsAsync<CallerRollbackException>(() => rows.ApplyAsync(afterMutation: async (result, token) =>
        {
            captured = result;
            await Assert.That(result.Accepted).IsTrue();
            await Assert.That(await rows.Owner.SystemSettings.Where(row => row.SettingKey == LocationSettingsNativeFixture.Key)
                .Select(row => row.Value).SingleAsync(token)).IsEqualTo("false");
            await rows.AssertStateAsync([], committed: false);
            throw new CallerRollbackException();
        }));
        await rows.AssertStateAsync([], committed: false);
        await Assert.That(rows.Owner.Database.CurrentTransaction).IsNull();
        await Assert.That(rows.Owner.ChangeTracker.Entries()).IsEmpty();
        await Assert.That(ordinary.Database.CurrentTransaction).IsNull();
        await Assert.That(ordinary.ChangeTracker.Entries()).IsEmpty();
        await Assert.That(captured!.CorrectedProjections.Select(row => row.EventLocationId)).IsEquivalentTo([rows.A.Id, rows.B.Id]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RejectedMutationCannotCommitItsSettingWithEitherTransactionOwner(bool callerOwned)
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(postgres, instance: true);
        await using var ordinary = rows.Ordinary(rows.TenantA);
        await AssertOrdinaryAsync(ordinary);
        bool persisted = false;
        LocationPrivacyGovernanceMutationResult? escaped = null;
        var failure = new CallerRollbackException();
        async Task Fail(CancellationToken token)
        {
            await Assert.That(await rows.Owner.SystemSettings.Where(row => row.SettingKey == LocationSettingsNativeFixture.Key)
                .Select(row => row.Value).SingleAsync(token)).IsEqualTo("false");
            persisted = true;
            throw failure;
        }
        // Both invocations retain one physical, non-replaying prepared owner.
        // One fails inside the service's persistence callback; the other fails
        // in its original caller after the complete coordinated mutation.
        var caught = await Assert.ThrowsAsync<CallerRollbackException>(async () =>
            escaped = callerOwned
                ? await rows.ApplyAsync(afterMutation: (_, token) => Fail(token))
                : await rows.ApplyAsync(afterPersist: Fail));
        await Assert.That(persisted).IsTrue();
        await Assert.That(caught).IsSameReferenceAs(failure);
        await Assert.That(escaped).IsNull();
        await rows.AssertStateAsync([], committed: false);
    }

    [Test]
    public async Task RejectedLateProjectionPreventsCallerFromCommittingItsSettingAndEarlierCorrection()
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(postgres, instance: true);
        bool staleRejected = false;
        Exception? ownerFailure = null;
        try
        {
            await rows.ApplyAsync(afterPersist: async token =>
            {
                var targets = (await rows.Services.GetRequiredService<ILocationPrivacyGovernanceStore>()
                    .ReadActiveProjectionsAsync(null, token))
                    .Where(state => state.EventLocationId == rows.A.Id || state.EventLocationId == rows.B.Id)
                    .OrderBy(state => state.TenantId).ThenBy(state => state.EventId).ThenBy(state => state.EventLocationId)
                    .ToArray();
                await Assert.That(targets.Length).IsEqualTo(2);
                targets[1] = targets[1] with { ConcurrencyStamp = Guid.CreateVersion7() };
                DateTime changed = DateTime.UtcNow;
                var corrections = targets.Select(state =>
                {
                    var audit = state.CreateTighteningAudit(rows.Actor, changed);
                    var message = LocationPrivacyOutboxMessageFactory.CreateProjectionCorrection(Guid.CreateVersion7(),
                        state.EventLocationId, state.TenantId, state.EventId, audit.NewPolicyVersion, changed);
                    audit.BindCorrection(message.Id);
                    return new LocationPrivacyGovernanceCorrection(state, audit, message);
                }).ToArray();
                // This is deliberately a native protocol attack, not an invented
                // application-store admission. The real human producer has already
                // persisted its exact setting in this same physical transaction.
                await using var command = rows.Owner.Database.GetDbConnection().CreateCommand();
                command.Transaction = rows.Owner.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText =
                    $"SELECT * FROM {PostgresLocationPrivacyGovernanceContract.CorrectionsFunctionSql(rows.Owner)}" +
                    "(@key, @tenant, @actor, CAST(@changed AS timestamp with time zone), CAST(@targets AS jsonb))";
                command.Parameters.Add(new NpgsqlParameter("key", LocationSettingsNativeFixture.Key));
                command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = DBNull.Value });
                command.Parameters.Add(new NpgsqlParameter("actor", rows.Actor));
                command.Parameters.Add(new NpgsqlParameter("changed", changed.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
                command.Parameters.Add(new NpgsqlParameter("targets", JsonSerializer.Serialize(corrections.Select(item => new
                {
                    item.Before,
                    item.Audit,
                    Message = new
                    {
                        item.Message.Id,
                        item.Message.AggregateType,
                        item.Message.AggregateId,
                        item.Message.EventType,
                        Payload = JsonSerializer.Deserialize<JsonElement>(item.Message.Payload!),
                        item.Message.Status,
                        item.Message.CreatedAt,
                        item.Message.MaxRetries
                    },
                    Stamp = Guid.CreateVersion7()
                }))));
                try
                {
                    await using var reader = await command.ExecuteReaderAsync(token);
                    while (await reader.ReadAsync(token)) { }
                }
                catch (PostgresException exception)
                {
                    await Assert.That(exception.MessageText).IsEqualTo("location_governance_target_stale");
                    staleRejected = true;
                }
                // Returning after the caught native error must not rescue this owner.
            });
        }
        catch (Exception exception) { ownerFailure = exception; }
        await Assert.That(staleRejected).IsTrue();
        await Assert.That(ownerFailure).IsNotNull();
        await rows.AssertStateAsync([], committed: false);
    }

    private static async Task AssertOrdinaryAsync(ExploreDbContext context)
    {
        await Assert.That(await context.Database.SqlQueryRaw<bool>(
            "SELECT rolsuper OR rolbypassrls AS \"Value\" FROM pg_catalog.pg_roles WHERE rolname = current_user").SingleAsync()).IsFalse();
    }

    private sealed class CallerRollbackException : Exception;
    private sealed class ClaimClock(DateTime instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(instant);
    }
    private sealed class CorrectionObserver : IAtprotoLocationPrivacyCorrectionPlanner
    {
        internal List<AtprotoLocationPrivacyCorrectionInput> Inputs { get; } = [];
        public Task<AtprotoPublicationPlanningResult> PlanLocationPrivacyCorrectionAsync(
            AtprotoLocationPrivacyCorrectionInput input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Add(input);
            return Task.FromResult(AtprotoPublicationPlanningResult.Skipped("correction_already_planned"));
        }
    }
}
