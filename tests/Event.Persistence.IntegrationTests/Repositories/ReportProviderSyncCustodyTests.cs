using System.Diagnostics.Metrics;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ReportProviderSyncCustodyTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime SubmittedAt = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedSubmission_RetainsOriginalVersionAndProviderReplayMarker(bool updateCase)
    {
        await fixture.ResetAsync();
        var source = await CreateSubmissionAsync();
        if (updateCase)
        {
            await using var update = fixture.CreateTenantFilteredDbContext(new SelectedTenant(source.TenantId));
            var reportCase = await update.EventReportCases.SingleAsync(row => row.Id == source.CaseId);
            reportCase.Triage("escalated", EventReportPriority.High, SubmittedAt.AddHours(1));
            await update.SaveChangesAsync();
            await Assert.That(reportCase.ConcurrencyStamp).IsNotEqualTo(source.CaseVersion);
        }

        await using var context = fixture.CreateTenantFilteredDbContext(new SelectedTenant(source.TenantId));
        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == source.MessageId);
        var updatedAt = await context.EventReportCases.Where(row => row.Id == source.CaseId)
            .Select(row => row.UpdatedAt).SingleAsync();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var provider = new RecordingProvider(context);
        var dispatcher = CreateDispatcher(context, provider, services);

        await dispatcher.DispatchAsync(message);
        context.ChangeTracker.Clear();
        await dispatcher.DispatchAsync(message);

        var envelope = provider.Envelopes.Single();
        await Assert.That(envelope.TenantId).IsEqualTo(source.TenantId);
        await Assert.That(envelope.ReportId).IsEqualTo(source.ReportId);
        await Assert.That(envelope.EventId).IsEqualTo(source.EventId);
        await Assert.That(envelope.CaseId).IsEqualTo(source.CaseId);
        await Assert.That(envelope.CaseConcurrencyStamp).IsEqualTo(source.CaseVersion);
        await Assert.That(envelope.SubmittedAtUtc).IsEqualTo(SubmittedAt);
        await Assert.That(envelope.LastUpdatedAtUtc).IsEqualTo(updatedAt);
        await Assert.That(envelope.QueueCode).IsEqualTo(updateCase ? "escalated" : "safety");
        await Assert.That(envelope.IdempotencyKey).IsEqualTo($"event-report-provider-sync:{source.MessageId:N}");
        await Assert.That(envelope.CorrelationId).IsEqualTo("native-submission");
        await Assert.That(provider.CalledInsideTransaction).IsFalse();

        await using var verify = fixture.CreateTenantFilteredDbContext(new SelectedTenant(source.TenantId));
        var persisted = await new EventReportRepository(verify)
            .GetByIdForProviderSyncAsync(source.TenantId, source.ReportId, CancellationToken.None);
        await Assert.That(persisted).IsNotNull();
        await Assert.That(persisted!.ProviderSyncCaseConcurrencyStamp).IsEqualTo(source.CaseVersion);
        await Assert.That(persisted.ProviderSyncOutboxId).IsEqualTo(source.MessageId);
        await Assert.That(persisted.ExternalLinks.Single().SyncState).IsEqualTo(EventReportSyncState.Synced);
        await Assert.That(persisted.ExternalLinks.Single().ProviderTargetScope).IsEqualTo(EventReportProviderTargetScope.Instance);
        await Assert.That(persisted.ExternalLinks.Single().ProviderTargetId).IsEqualTo("instance");
    }

    [Test]
    [Arguments("message-id")]
    [Arguments("aggregate-type")]
    [Arguments("aggregate-id")]
    [Arguments("event-type")]
    [Arguments("tenant")]
    [Arguments("report")]
    [Arguments("event")]
    [Arguments("case")]
    [Arguments("case-version")]
    [Arguments("current-case-version")]
    [Arguments("submitted-time")]
    [Arguments("envelope-time")]
    [Arguments("reason")]
    [Arguments("queue")]
    [Arguments("correlation")]
    [Arguments("native-created-time")]
    public async Task AlteredSubmission_DoesNotCallProviderOrPersistMarker(string mutation)
    {
        await fixture.ResetAsync();
        var source = await CreateSubmissionAsync();
        await using var context = fixture.CreateTenantFilteredDbContext(new SelectedTenant(source.TenantId));
        var message = await context.OutboxMessages.SingleAsync(row => row.Id == source.MessageId);
        var payload = JsonSerializer.Deserialize<EventReportProviderSyncRequested>(message.Payload!, JsonOptions)!;
        switch (mutation)
        {
            case "message-id":
                context.Entry(message).State = EntityState.Detached;
                message.Id = Guid.CreateVersion7();
                break;
            case "aggregate-type": message.AggregateType = "Event"; break;
            case "aggregate-id": message.AggregateId = Guid.CreateVersion7(); break;
            case "event-type": message.EventType = "other"; break;
            case "envelope-time": message.CreatedAt = SubmittedAt.AddSeconds(1); break;
            case "native-created-time":
                var report = await context.EventReports.SingleAsync(row => row.Id == source.ReportId);
                report.CreatedAt = SubmittedAt.AddSeconds(1);
                break;
            case "current-case-version":
                var reportCase = await context.EventReportCases.SingleAsync(row => row.Id == source.CaseId);
                reportCase.MarkWaitingExternal(SubmittedAt.AddHours(1));
                await context.SaveChangesAsync();
                message.Payload = JsonSerializer.Serialize(payload with { CaseConcurrencyStamp = reportCase.ConcurrencyStamp });
                break;
            default:
                payload = mutation switch
                {
                    "tenant" => payload with { TenantId = Guid.CreateVersion7() },
                    "report" => payload with { ReportId = Guid.CreateVersion7() },
                    "event" => payload with { EventId = Guid.CreateVersion7() },
                    "case" => payload with { CaseId = source.OtherCaseId },
                    "case-version" => payload with { CaseConcurrencyStamp = Guid.CreateVersion7() },
                    "submitted-time" => payload with { SubmittedAtUtc = SubmittedAt.AddSeconds(1) },
                    "reason" => payload with { ReasonCode = "different" },
                    "queue" => payload with { QueueCode = "different" },
                    "correlation" => payload with { CorrelationId = "different" },
                    _ => throw new ArgumentOutOfRangeException(nameof(mutation))
                };
                message.Payload = JsonSerializer.Serialize(payload);
                break;
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var provider = new RecordingProvider(context);

        await Assert.That(() => CreateDispatcher(context, provider, services).DispatchAsync(message))
            .Throws<InvalidOperationException>();

        await Assert.That(provider.Envelopes.Count).IsEqualTo(0);
        await Assert.That(await context.EventReportExternalLinks.CountAsync(row => row.ReportId == source.ReportId)).IsEqualTo(0);
        await Assert.That(await context.EventReportSignals.CountAsync(row => row.ReportId == source.ReportId)).IsEqualTo(0);
    }

    [Test]
    [Arguments("foreign")]
    [Arguments("empty")]
    [Arguments("bypass")]
    public async Task UnselectedTenant_CannotReadSubmissionOrCallProvider(string scope)
    {
        await fixture.ResetAsync();
        var source = await CreateSubmissionAsync();
        OutboxMessage message;
        await using (var owner = fixture.CreateTenantFilteredDbContext(new SelectedTenant(source.TenantId)))
            message = await owner.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == source.MessageId);

        await using var context = scope == "bypass"
            ? fixture.CreateDbContext()
            : fixture.CreateTenantFilteredDbContext(new SelectedTenant(scope == "empty" ? Guid.Empty : Guid.CreateVersion7()));
        var repository = new EventReportRepository(context);
        await Assert.That(await repository.GetByIdForProviderSyncAsync(source.TenantId, source.ReportId, CancellationToken.None))
            .IsNull();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var provider = new RecordingProvider(context);

        await Assert.That(() => CreateDispatcher(context, provider, services).DispatchAsync(message))
            .Throws<InvalidOperationException>();
        await Assert.That(provider.Envelopes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MissingSubmissionReceipt_CannotBeInferredFromMatchingJson()
    {
        await fixture.ResetAsync();
        var source = await CreateSubmissionAsync(retainReceipt: false);
        await using var context = fixture.CreateTenantFilteredDbContext(new SelectedTenant(source.TenantId));
        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == source.MessageId);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var provider = new RecordingProvider(context);

        await Assert.That(() => CreateDispatcher(context, provider, services).DispatchAsync(message))
            .Throws<InvalidOperationException>();
        await Assert.That(provider.Envelopes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SubMicrosecondSubmission_RoundTripsWithoutChangingProviderTime()
    {
        await fixture.ResetAsync();
        var source = await CreateSubmissionAsync(createdAt: SubmittedAt.AddTicks(7));
        await using var context = fixture.CreateTenantFilteredDbContext(new SelectedTenant(source.TenantId));
        var message = await context.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == source.MessageId);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var provider = new RecordingProvider(context);

        await CreateDispatcher(context, provider, services).DispatchAsync(message);

        await Assert.That(provider.Envelopes.Single().SubmittedAtUtc).IsEqualTo(SubmittedAt);
        await Assert.That(message.CreatedAt).IsEqualTo(SubmittedAt);
    }

    [Test]
    public async Task CommittedReceipt_CannotBeRecapturedOrChangedThroughTrackedPersistence()
    {
        await fixture.ResetAsync();
        var source = await CreateSubmissionAsync();
        await using var context = fixture.CreateTenantFilteredDbContext(new SelectedTenant(source.TenantId));
        var report = await context.EventReports.Include(row => row.Cases).SingleAsync(row => row.Id == source.ReportId);
        var reportCase = report.Cases.Single(row => row.Id == source.CaseId);

        await Assert.That(() => report.RetainProviderSubmission(Guid.CreateVersion7(), reportCase, new string('A', 64)))
            .Throws<InvalidOperationException>();
        context.Entry(report).Property(row => row.ProviderSyncPayloadDigest).CurrentValue = new string('A', 64);
        await Assert.That(() => context.SaveChangesAsync()).Throws<InvalidOperationException>();

        await using var verify = fixture.CreateTenantFilteredDbContext(new SelectedTenant(source.TenantId));
        var persisted = await verify.EventReports.SingleAsync(row => row.Id == source.ReportId);
        await Assert.That(persisted.ProviderSyncPayloadDigest).IsNotEqualTo(new string('A', 64));
        await Assert.That(persisted.ProviderSyncCaseConcurrencyStamp).IsEqualTo(source.CaseVersion);
    }

    private async Task<Submission> CreateSubmissionAsync(bool retainReceipt = true, DateTime? createdAt = null)
    {
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            FullName = "Report custody tenant",
            Slug = $"report-custody-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"report-{Guid.CreateVersion7():N}@example.com",
                FirstName = "Report",
                LastName = "Reporter"
            }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Report custody actor" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        var sourceEvent = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = null!,
            Title = "Report custody Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!
        };
        context.Events.Add(sourceEvent);
        await context.SaveChangesAsync();
        var report = EventReport.Create(tenant.Id, sourceEvent.Id, user.Id, actor.Id,
            EventReporterKind.AuthenticatedUser, EventReportSourceKind.UserReport, "spam", null,
            EventReportPriority.Normal, null, false, false, "en", null, null, createdAt ?? SubmittedAt);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "safety", EventReportPriority.Normal, null, createdAt ?? SubmittedAt);
        var otherCase = EventReportCase.Create(tenant.Id, report.Id, "appeal", EventReportPriority.Normal, null, SubmittedAt);
        OutboxMessage message;
        if (retainReceipt)
            message = EventReportOutboxMessageFactory.CreateProviderSyncRequestedMessage(report, reportCase, "native-submission");
        else
            message = new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                AggregateType = "EventReport",
                AggregateId = report.Id,
                EventType = EventReportOutboxMessageFactory.EventReportProviderSyncRequestedEventType,
                CreatedAt = SubmittedAt,
                Status = OutboxMessageStatus.Pending,
                Payload = JsonSerializer.Serialize(new EventReportProviderSyncRequested
                {
                    TenantId = tenant.Id,
                    ReportId = report.Id,
                    EventId = sourceEvent.Id,
                    CaseId = reportCase.Id,
                    CaseConcurrencyStamp = reportCase.ConcurrencyStamp,
                    ReasonCode = report.ReasonCode,
                    QueueCode = reportCase.QueueCode,
                    SubmittedAtUtc = SubmittedAt,
                    CorrelationId = "native-submission"
                })
            };
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.AddRange(report, reportCase, otherCase, message);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(tenant.Id, sourceEvent.Id, report.Id, reportCase.Id, otherCase.Id, reportCase.ConcurrencyStamp, message.Id);
    }

    private static ReportProviderSyncDispatcher CreateDispatcher(
        ExploreDbContext context, RecordingProvider provider, IServiceProvider services) =>
        new(provider, new EventReportRepository(context),
            new StaticOptions(new ModerationProviderOptions { Mode = ModerationProviderOptions.ModeCoop, MirrorReviewQueue = true }),
            new BusinessMetrics(services.GetRequiredService<IMeterFactory>()),
            NullLogger<ReportProviderSyncDispatcher>.Instance);

    private sealed class RecordingProvider(ExploreDbContext context) : IEventReportProvider
    {
        public List<EventReportProviderEnvelope> Envelopes { get; } = [];
        public bool CalledInsideTransaction { get; private set; }
        public Task<EventReportProviderSyncResult> SyncReportAsync(EventReportProviderEnvelope envelope, CancellationToken cancellationToken = default)
        {
            CalledInsideTransaction |= context.Database.CurrentTransaction is not null;
            Envelopes.Add(envelope);
            return Task.FromResult(EventReportProviderSyncResult.Success(providerCaseId: "native-case"));
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    private sealed record Submission(Guid TenantId, Guid EventId, Guid ReportId, Guid CaseId, Guid OtherCaseId, Guid CaseVersion, Guid MessageId);
    private sealed class StaticOptions(ModerationProviderOptions options) : IOptionsMonitor<ModerationProviderOptions>
    {
        public ModerationProviderOptions CurrentValue => options;
        public ModerationProviderOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<ModerationProviderOptions, string?> listener) => null;
    }
}
