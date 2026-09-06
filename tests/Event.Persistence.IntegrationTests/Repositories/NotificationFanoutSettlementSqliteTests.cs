// ABOUTME: File-backed SQLite contract for tenant-safe superseded notification fanout run settlement.
// ABOUTME: Proves mapped tables, terminal evidence, lease clearing, timestamps, and affected-row counts.

using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteNotificationFanoutSettlement")]
public sealed class NotificationFanoutSettlementSqliteTests
{
    [Test]
    public async Task SupersededRunSettlement_PreservesTerminalEvidenceAndClearsLeases()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using ExploreDbContext context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
        (Guid tenantId, Guid eventId, Guid actorId) = await SeedAuthorityAsync(context);
        DateTime settledAt = DateTime.SpecifyKind(new DateTime(2026, 8, 27, 12, 0, 0), DateTimeKind.Utc);
        NotificationFanoutOccurrence superseded = CreateOccurrence(
            tenantId,
            eventId,
            settledAt.AddMinutes(-5),
            "superseded");
        NotificationFanoutOccurrence replacement = CreateOccurrence(
            tenantId,
            eventId,
            settledAt.AddMinutes(-4),
            "replacement");
        superseded.Supersede(replacement.Id, "newer-authority", settledAt.AddMinutes(-3));

        NotificationFanoutRun processing = CreateRun(
            tenantId,
            actorId,
            superseded.Id,
            "processing",
            settledAt.AddMinutes(-2),
            settledAt.AddMinutes(1),
            settledAt.AddMinutes(2));
        NotificationFanoutRun completed = CreateRun(
            tenantId,
            actorId,
            replacement.Id,
            "completed",
            settledAt.AddMinutes(-5),
            settledAt.AddMinutes(-4),
            settledAt.AddMinutes(-3));
        completed.CompletedAt = settledAt.AddMinutes(-3);
        completed.ProcessedCount = 17;
        context.AddRange(replacement, superseded, processing, completed);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        NotificationFanoutOccurrence persistedOccurrence = await context.NotificationFanoutOccurrences
            .IgnoreQueryFilters()
            .SingleAsync(occurrence => occurrence.Id == superseded.Id);
        NotificationFanoutRun seededProcessing = await context.NotificationFanoutRuns
            .IgnoreQueryFilters()
            .SingleAsync(run => run.Id == processing.Id);
        await Assert.That(persistedOccurrence.State).IsEqualTo(NotificationFanoutOccurrenceState.Superseded);
        await Assert.That(seededProcessing.TenantId).IsEqualTo(tenantId);
        await Assert.That(seededProcessing.FanoutOccurrenceId).IsEqualTo(superseded.Id);
        await Assert.That(seededProcessing.Status).IsEqualTo("processing");
        context.ChangeTracker.Clear();

        var repository = new NotificationFanoutOccurrenceRepository(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        int wrongTenant = await repository.SettleNonTerminalRunsForSupersededOccurrenceAsync(
            Guid.CreateVersion7(),
            superseded.Id,
            settledAt,
            CancellationToken.None);
        int settled = await repository.SettleNonTerminalRunsForSupersededOccurrenceAsync(
            tenantId,
            superseded.Id,
            settledAt,
            CancellationToken.None);
        await transaction.CommitAsync();

        NotificationFanoutRun[] runs = await context.NotificationFanoutRuns
            .IgnoreQueryFilters()
            .Where(run => run.Id == processing.Id || run.Id == completed.Id)
            .ToArrayAsync();
        NotificationFanoutRun persistedProcessing = runs.Single(run => run.Id == processing.Id);
        NotificationFanoutRun persistedCompleted = runs.Single(run => run.Id == completed.Id);
        await Assert.That(wrongTenant).IsEqualTo(0);
        await Assert.That(settled).IsEqualTo(1);
        await Assert.That(persistedProcessing.Status).IsEqualTo("completed");
        await Assert.That(persistedProcessing.ProcessingLeaseOwner).IsNull();
        await Assert.That(persistedProcessing.ProcessingLeaseToken).IsNull();
        await Assert.That(persistedProcessing.ProcessingLeaseExpiresAt).IsNull();
        await Assert.That(persistedProcessing.CompletedAt).IsEqualTo(settledAt.AddMinutes(2));
        await Assert.That(persistedProcessing.UpdatedAt).IsEqualTo(settledAt.AddMinutes(2));
        await Assert.That(persistedCompleted.Status).IsEqualTo("completed");
        await Assert.That(persistedCompleted.CompletedAt).IsEqualTo(settledAt.AddMinutes(-3));
        await Assert.That(persistedCompleted.ProcessedCount).IsEqualTo(17);
    }

    private static ExploreDbContext CreateContext(SqliteConnection connection) =>
        new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options);

    private static async Task<(Guid TenantId, Guid EventId, Guid ActorId)> SeedAuthorityAsync(
        ExploreDbContext context)
    {
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "SQLite fanout settlement",
            Slug = $"sqlite-fanout-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var principal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = $"sqlite-fanout-{Guid.CreateVersion7():N}",
            DisplayName = "SQLite fanout worker",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Bot,
            ActorType = null!,
            ServicePrincipalId = principal.Id,
            ServicePrincipal = principal,
            Pii = new ActorPii { DisplayName = "SQLite fanout worker" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            Title = "SQLite fanout event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.AddRange(tenant, actor, @event);
        await context.SaveChangesAsync();
        return (tenant.Id, @event.Id, actor.Id);
    }

    private static NotificationFanoutOccurrence CreateOccurrence(
        Guid tenantId,
        Guid eventId,
        DateTime occurredAt,
        string sourceType) =>
        NotificationFanoutOccurrence.Create(
            Guid.CreateVersion7(),
            tenantId,
            eventId,
            sessionId: null,
            occurredAt,
            audienceCutoffAt: occurredAt,
            Guid.CreateVersion7(),
            changeSetJson: "{}",
            safeBeforeSnapshotJson: "{}",
            safeAfterSnapshotJson: "{}",
            templateKey: "event_update",
            templateVersion: 1,
            deliveryPolicyId: (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            policyVersion: 1,
            priority: 1,
            notBefore: occurredAt,
            sourceType,
            Guid.CreateVersion7(),
            coalescingKey: $"{sourceType}:{eventId:N}",
            coalescingWindowEndsAt: occurredAt.AddMinutes(5));

    private static NotificationFanoutRun CreateRun(
        Guid tenantId,
        Guid actorId,
        Guid occurrenceId,
        string status,
        DateTime createdAt,
        DateTime? startedAt,
        DateTime? updatedAt) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            FanoutKind = "event-update",
            NotificationEntityTypeId = 1,
            NotificationEntityType = null!,
            EntityId = Guid.CreateVersion7(),
            SourceActorId = actorId,
            SourceActor = null!,
            Status = status,
            FanoutOccurrenceId = occurrenceId,
            ProcessingLeaseOwner = status == "processing" ? "sqlite-worker" : null,
            ProcessingLeaseToken = status == "processing" ? Guid.CreateVersion7() : null,
            ProcessingLeaseExpiresAt = status == "processing" ? createdAt.AddMinutes(10) : null,
            ProcessingGeneration = 1,
            ProcessingFence = 1,
            StartedAt = startedAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
}
