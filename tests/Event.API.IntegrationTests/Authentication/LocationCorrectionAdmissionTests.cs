using Explore.Persistence.Database.ProviderPrimitives;
using System.Text.Json;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Messaging;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Api.IntegrationTests.Authentication;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class LocationCorrectionAdmissionTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("original")]
    [Arguments("source-deleted")]
    [Arguments("terminal")]
    [Arguments("tenant")]
    [Arguments("event")]
    [Arguments("policy")]
    [Arguments("message")]
    [Arguments("version")]
    [Arguments("expires-after-read")]
    public async Task GovernanceAdmissionUsesOriginalRetainedReceiptNotCurrentPlacement(string variant)
    {
        await using var rows = await LocationSettingsNativeFixture.CreateAsync(fixture, instance: true);
        var produced = await rows.ApplyAsync();
        await Assert.That(produced.Accepted).IsTrue();
        await rows.AssertStateAsync([rows.A.Id, rows.B.Id], committed: true);
        await using var observer = fixture.CreateDbContext();
        OutboxMessage message = await observer.OutboxMessages.AsNoTracking().SingleAsync(row =>
            row.AggregateId == rows.A.Id && row.EventType == LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType);
        if (variant == "source-deleted")
            await observer.EventLocations.Where(row => row.Id == rows.A.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
        if (variant == "terminal")
            await observer.OutboxMessages.Where(row => row.Id == message.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, OutboxMessageStatus.DeadLettered)
                    .SetProperty(row => row.DeadLetteredAt, DateTime.UtcNow)
                    .SetProperty(row => row.NextRetryAt, DateTime.UtcNow.AddMinutes(-1)));
        if (variant is "tenant" or "event" or "policy" or "version")
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message.Payload!)!;
            if (variant == "tenant") fields["TenantId"] = JsonSerializer.SerializeToElement(rows.B.TenantId);
            if (variant == "event") fields["EventId"] = JsonSerializer.SerializeToElement(rows.B.EventId);
            if (variant == "policy") fields["PolicyVersion"] = JsonSerializer.SerializeToElement(999);
            if (variant == "version") fields["SchemaVersion"] = JsonSerializer.SerializeToElement(2);
            string altered = JsonSerializer.Serialize(fields);
            await observer.OutboxMessages.Where(row => row.Id == message.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Payload, altered));
        }
        if (variant == "message")
        {
            var forged = new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                AggregateId = message.AggregateId,
                AggregateType = message.AggregateType,
                EventType = message.EventType,
                Payload = message.Payload,
                CreatedAt = message.CreatedAt,
                Status = OutboxMessageStatus.Pending
            };
            observer.OutboxMessages.Add(forged);
            await observer.SaveChangesAsync();
            message = forged;
        }
        var admission = await rows.CreateOutboxAdmissionAsync();
        var claims = await admission.ClaimDueAsync(1000, DateTime.UtcNow, default);
        var claim = claims.SingleOrDefault(row => row.MessageId == message.Id);
        if (variant is not "original" and not "source-deleted" and not "terminal" and not "expires-after-read")
        {
            await Assert.That(claim).IsNull();
            await Assert.That((await observer.OutboxMessages.AsNoTracking()
                .SingleAsync(row => row.Id == message.Id)).Status).IsEqualTo(OutboxMessageStatus.Failed);
            return;
        }
        await Assert.That(claim).IsNotNull();
        await Assert.That(claim!.TenantId).IsEqualTo(rows.A.TenantId);
        await Assert.That(claim.SourceId).IsEqualTo(message.Id);
        var expiryClock = new ReceiptClock(claim.LeaseExpiresAt.AddTicks(-1));
        var expiryRead = new ExpireReceiptRead(expiryClock, claim.LeaseExpiresAt);
        await using var runtime = rows.Ordinary(rows.A.TenantId, expiryRead);
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            runtime.Database.SqlQueryRaw<GenericOutboxClaim>(
                $"SELECT * FROM {PostgresGenericOutboxAdmissionContract.ReadFunctionSql(runtime)}({{0}}, {{1}}, {{2}})",
                claim.MessageId, claim.LeaseExpiresAt, (int)claim.Status).ToArrayAsync());
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        var authority = new GenericOutboxExecutionAuthority(runtime, admission);
        await authority.BindAsync(claim, default);
        OutboxMessage reloaded = (await new OutboxRepository(runtime)
            .GetClaimedGenericAsync(claim.MessageId, claim.LeaseExpiresAt, claim.Status, DateTime.UtcNow))!;
        if (variant == "terminal")
            await authority.RequireDeadLetterAsync(reloaded, default);
        else
            await authority.RequireAsync(reloaded, default);
        var planner = new RecordingPlanner();
        var dispatcher = new LocationPrivacyCorrectionDispatcher(
            rows.Services.GetRequiredService<HybridCache>(), planner,
            rows.Services.GetRequiredService<EventLocationPrivacyMetrics>(),
            new LocationPrivacyCorrectionReceiptRepository(runtime,
                variant == "expires-after-read" ? expiryClock : TimeProvider.System));
        if (variant == "expires-after-read")
        {
            expiryRead.Armed = true;
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.DispatchAsync(reloaded));
            await Assert.That(expiryRead.Fired).IsTrue();
            await Assert.That(planner.Inputs.Count).IsEqualTo(0);
            return;
        }
        if (variant == "terminal")
            await dispatcher.ReconcileDeadLetterAsync(reloaded);
        else
            await dispatcher.DispatchAsync(reloaded);
        await Assert.That(planner.Inputs.Single().CorrectionId).IsEqualTo(message.Id);
        await Assert.That(planner.Inputs.Single().TenantId).IsEqualTo(rows.A.TenantId);
        await Assert.That(planner.Inputs.Single().EventId).IsEqualTo(rows.A.EventId);
        if (variant == "terminal")
            await authority.RequireDeadLetterAsync(reloaded, default);
        else
            await authority.RequireAsync(reloaded, default);
    }

    private sealed class ReceiptClock(DateTime initial) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = initial;
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class ExpireReceiptRead(ReceiptClock clock, DateTime expiry) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public bool Fired { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                clock.UtcNow = expiry;
                Armed = false;
                Fired = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingPlanner : IAtprotoLocationPrivacyCorrectionPlanner
    {
        public List<AtprotoLocationPrivacyCorrectionInput> Inputs { get; } = [];
        public Task<AtprotoPublicationPlanningResult> PlanLocationPrivacyCorrectionAsync(
            AtprotoLocationPrivacyCorrectionInput input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Add(input);
            return Task.FromResult(AtprotoPublicationPlanningResult.Skipped("correction_already_planned"));
        }
    }

}
