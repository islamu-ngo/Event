using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("IntegrationSyncOutboxRepository")]
public sealed class IntegrationSyncOutboxRepositoryTests
{
    [Test]
    public async Task StaleClaimIsReclaimedAndOnlyTheExactNewOwnerCanSettle()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await InitializeAsync(connection);
        await using ExploreDbContext context = CreateContext(connection, Guid.CreateVersion7());
        DateTime now = DateTime.UtcNow;
        IntegrationSyncOutbox outbox = await SeedAsync(context, now.AddMinutes(-10));
        var repository = new IntegrationSyncOutboxRepository(context);
        Guid staleToken = outbox.ProcessingLeaseToken!.Value;
        DateTime staleStartedAt = outbox.ProcessingStartedAt!.Value;

        IReadOnlyList<IntegrationSyncOutbox> candidates = await repository.GetPendingBatch(
            10,
            now,
            now.AddMinutes(-5),
            CancellationToken.None);
        await Assert.That(candidates.Select(candidate => candidate.Id)).Contains(outbox.Id);

        Guid newToken = Guid.CreateVersion7();
        DateTime newStartedAt = now;
        bool reclaimed = await repository.TryClaimAsync(
            new IntegrationSyncClaimRequest(outbox.TenantId, outbox.Id, newToken, newStartedAt, now.AddMinutes(-5)),
            CancellationToken.None);
        await Assert.That(reclaimed).IsTrue();

        bool staleSettled = await repository.CompleteAsync(
            new IntegrationSyncClaimIdentity(outbox.TenantId, outbox.Id, staleToken, staleStartedAt),
            now,
            CancellationToken.None);
        bool currentSettled = await repository.CompleteAsync(
            new IntegrationSyncClaimIdentity(outbox.TenantId, outbox.Id, newToken, newStartedAt),
            now,
            CancellationToken.None);
        await Assert.That(staleSettled).IsFalse();
        await Assert.That(currentSettled).IsTrue();
    }

    [Test]
    public async Task ProviderHandoffBarrierIsSelectedOnlyForAmbiguousParking()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await InitializeAsync(connection);
        await using ExploreDbContext context = CreateContext(connection, Guid.CreateVersion7());
        DateTime now = DateTime.UtcNow;
        IntegrationSyncOutbox outbox = await SeedAsync(context, now);
        var repository = new IntegrationSyncOutboxRepository(context);
        var claim = new IntegrationSyncClaimIdentity(
            outbox.TenantId,
            outbox.Id,
            outbox.ProcessingLeaseToken!.Value,
            outbox.ProcessingStartedAt!.Value);

        await Assert.That(await repository.MarkProviderHandoffStartedAsync(claim, now, CancellationToken.None)).IsTrue();
        IReadOnlyList<IntegrationSyncOutbox> candidates = await repository.GetPendingBatch(
            10,
            now.AddMinutes(10),
            now.AddMinutes(5),
            CancellationToken.None);

        await Assert.That(candidates.Select(candidate => candidate.Id)).Contains(outbox.Id);
        await Assert.That(await repository.ParkAmbiguousAsync(claim, now.AddMinutes(10), CancellationToken.None)).IsTrue();
        context.ChangeTracker.Clear();
        IntegrationSyncOutbox persisted = await context.IntegrationSyncOutbox.IgnoreQueryFilters().SingleAsync();
        await Assert.That(persisted.Status).IsEqualTo(IntegrationSyncStatus.DeadLettered);
        await Assert.That(persisted.EvidenceKind).IsEqualTo(IntegrationSyncEvidenceKind.ProviderOutcomeAmbiguous);
        await Assert.That(persisted.LastError).IsNull();

        IntegrationSyncOutbox? resolved = await repository.ResolveAmbiguousAsync(
            new IntegrationSyncRecoveryRequest(
                outbox.TenantId,
                outbox.Id,
                IntegrationSyncRecoveryDecision.RetryDefinitelyNotAccepted,
                "incident-verified-not-accepted",
                Guid.CreateVersion7(),
                now.AddMinutes(11)),
            CancellationToken.None);
        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.Status).IsEqualTo(IntegrationSyncStatus.RetryScheduled);
        await Assert.That(resolved.EvidenceKind).IsEqualTo(IntegrationSyncEvidenceKind.OperatorRetryDefinitelyNotAccepted);
        await Assert.That(resolved.LastError).IsNull();
    }

    [Test]
    public async Task ProviderDiagnosticCannotAuthorizeAmbiguityRecovery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await InitializeAsync(connection);

        DateTime now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Integration diagnostic authority test",
            Slug = $"integration-diagnostic-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = now
        };
        await using ExploreDbContext context = CreateContext(connection, tenant.Id);
        IntegrationSyncOutbox control = ProcessingRow();
        IntegrationSyncOutbox diagnostic = ProcessingRow();
        context.AddRange(tenant, control, diagnostic);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new IntegrationSyncOutboxRepository(context);
        var controlClaim = new IntegrationSyncClaimIdentity(
            tenant.Id, control.Id, control.ProcessingLeaseToken!.Value, now);
        var diagnosticClaim = new IntegrationSyncClaimIdentity(
            tenant.Id, diagnostic.Id, diagnostic.ProcessingLeaseToken!.Value, now);

        await Assert.That(await repository.ParkAmbiguousAsync(
            controlClaim with { TenantId = Guid.CreateVersion7() }, now, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.ParkAmbiguousAsync(
            controlClaim with { LeaseToken = Guid.CreateVersion7() }, now, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.ParkAmbiguousAsync(controlClaim, now, CancellationToken.None)).IsTrue();
        Guid actorId = Guid.CreateVersion7();
        IntegrationSyncOutbox? resolvedControl = await repository.ResolveAmbiguousAsync(
            new IntegrationSyncRecoveryRequest(tenant.Id, control.Id,
                IntegrationSyncRecoveryDecision.ConfirmAccepted, "verified-provider-receipt", actorId, now),
            CancellationToken.None);
        await Assert.That(resolvedControl).IsNotNull();
        await Assert.That(resolvedControl!.Status).IsEqualTo(IntegrationSyncStatus.Completed);
        await Assert.That(resolvedControl.UpdatedBy).IsEqualTo(actorId);
        await Assert.That(resolvedControl.CorrelationId).IsEqualTo("verified-provider-receipt");

        const string diagnosticText = "provider_outcome_ambiguous";
        await Assert.That(await repository.FailAsync(
            diagnosticClaim with { LeaseToken = Guid.CreateVersion7() }, diagnosticText,
            false, TimeSpan.Zero, 1, now, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.FailAsync(
            diagnosticClaim, diagnosticText, false, TimeSpan.Zero, 1, now, CancellationToken.None)).IsTrue();
        IntegrationSyncOutbox persisted = await context.IntegrationSyncOutbox.AsNoTracking()
            .SingleAsync(row => row.Id == diagnostic.Id);
        await Assert.That(persisted.Status).IsEqualTo(IntegrationSyncStatus.DeadLettered);
        await Assert.That(persisted.AttemptCount).IsEqualTo(persisted.MaxAttempts);
        await Assert.That(persisted.DeadLetteredAt).IsEqualTo(now);
        await Assert.That(persisted.NextAttemptAt).IsNull();
        await Assert.That(persisted.ProcessingLeaseToken).IsNull();
        await Assert.That(persisted.LastError).IsEqualTo(diagnosticText);

        var globalFactory = new QueueHealthGlobalFactory(
            (DbContextOptions<ExploreDbContext>)context.GetService<IDbContextOptions>());
        QueueDrainHealthSnapshot snapshot = await new QueueDrainHealthRepository(globalFactory).GetSnapshotAsync(
            now, now.AddMinutes(-5), CancellationToken.None);
        await Assert.That(snapshot.IntegrationAmbiguous).IsEqualTo(0);

        IntegrationSyncOutbox? resolvedDiagnostic = await repository.ResolveAmbiguousAsync(
            new IntegrationSyncRecoveryRequest(tenant.Id, diagnostic.Id,
                IntegrationSyncRecoveryDecision.ConfirmAccepted, "diagnostic-is-not-evidence", actorId, now),
            CancellationToken.None);
        await Assert.That(resolvedDiagnostic).IsNull();
        IntegrationSyncOutbox unchanged = await context.IntegrationSyncOutbox.AsNoTracking()
            .SingleAsync(row => row.Id == diagnostic.Id);
        await Assert.That(unchanged.Status).IsEqualTo(persisted.Status);
        await Assert.That(unchanged.LastError).IsEqualTo(persisted.LastError);
        await Assert.That(unchanged.UpdatedBy).IsEqualTo(persisted.UpdatedBy);
        await Assert.That(unchanged.CorrelationId).IsEqualTo(persisted.CorrelationId);

        IntegrationSyncOutbox ProcessingRow() => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Kind = IntegrationKind.Listmonk,
            SourceType = "test",
            SourceId = Guid.CreateVersion7(),
            SubscriberEmail = "integration-sync@example.test",
            SubscriberPayloadJson = "{}",
            ListmonkListId = 1,
            Status = IntegrationSyncStatus.Processing,
            AttemptCount = 1,
            MaxAttempts = 1,
            ProcessingStartedAt = now,
            ProcessingLeaseToken = Guid.CreateVersion7(),
            CreatedAt = now
        };
    }

    [Test]
    public async Task DirectTrackedWriteRejectsIncompatibleEvidenceWithoutPersistingTheRow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await InitializeAsync(connection);

        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Integration evidence persistence test",
            Slug = $"integration-evidence-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        };
        await using ExploreDbContext context = CreateContext(connection, tenant.Id);
        IntegrationSyncOutbox baseline = PendingRow();
        context.Add(tenant);
        context.IntegrationSyncOutbox.Add(baseline);
        await context.SaveChangesAsync();

        IntegrationSyncOutbox invalid = PendingRow();
        invalid.EvidenceKind = IntegrationSyncEvidenceKind.ProviderOutcomeAmbiguous;
        context.IntegrationSyncOutbox.Add(invalid);
        Exception? failure = null;
        try
        {
            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await using ExploreDbContext observer = CreateContext(connection, tenant.Id);
        IntegrationSyncOutbox persisted = await observer.IntegrationSyncOutbox.AsNoTracking()
            .SingleAsync(row => row.Id == baseline.Id);
        await Assert.That(persisted.Status).IsEqualTo(IntegrationSyncStatus.Pending);
        await Assert.That(persisted.EvidenceKind).IsEqualTo(IntegrationSyncEvidenceKind.None);
        await Assert.That(await observer.IntegrationSyncOutbox.AnyAsync(row => row.Id == invalid.Id)).IsFalse();
        await Assert.That(failure?.GetType()).IsEqualTo(typeof(InvalidOperationException));
        await Assert.That(failure!.Message).IsEqualTo("integration_sync_evidence_invalid");
        await Assert.That(context.Entry(invalid).State).IsEqualTo(EntityState.Added);

        IntegrationSyncOutbox PendingRow() => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Kind = IntegrationKind.Listmonk,
            SourceType = "test",
            SourceId = Guid.CreateVersion7(),
            SubscriberEmail = "integration-sync@example.test",
            SubscriberPayloadJson = "{}",
            ListmonkListId = 1,
            Status = IntegrationSyncStatus.Pending,
            CreatedAt = tenant.CreatedAt
        };
    }

    [Test]
    public async Task MalformedProcessingEvidenceIsParkedInsteadOfReplayed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await InitializeAsync(connection);
        await using ExploreDbContext context = CreateContext(connection, Guid.CreateVersion7());
        DateTime now = DateTime.UtcNow;
        IntegrationSyncOutbox outbox = await SeedAsync(context, now.AddMinutes(-10));
        outbox.ProcessingLeaseToken = null;
        await context.SaveChangesAsync();
        var repository = new IntegrationSyncOutboxRepository(context);

        IReadOnlyList<IntegrationSyncOutbox> candidates = await repository.GetPendingBatch(
            10,
            now,
            now.AddMinutes(-5),
            CancellationToken.None);
        bool parked = await repository.ParkMalformedProcessingAsync(
            outbox.TenantId,
            outbox.Id,
            now,
            CancellationToken.None);

        await Assert.That(candidates.Select(candidate => candidate.Id)).Contains(outbox.Id);
        await Assert.That(parked).IsTrue();
    }

    [Test]
    public async Task QueueHealthSnapshotReportsOnlyBoundedCrossTenantCounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await InitializeAsync(connection);
        await using ExploreDbContext context = CreateContext(connection, Guid.CreateVersion7());
        DateTime now = DateTime.UtcNow;
        await SeedAsync(context, now.AddMinutes(-10));

        var globalFactory = new QueueHealthGlobalFactory(
            (DbContextOptions<ExploreDbContext>)context.GetService<IDbContextOptions>());
        QueueDrainHealthSnapshot snapshot = await new QueueDrainHealthRepository(globalFactory).GetSnapshotAsync(
            now,
            now.AddMinutes(-5),
            CancellationToken.None);

        await Assert.That(snapshot.IntegrationStale).IsEqualTo(1);
        await Assert.That(snapshot.IntegrationDue).IsEqualTo(0);
        await Assert.That(snapshot.IncomingDue).IsEqualTo(0);
        await Assert.That(snapshot.PdsDue).IsEqualTo(0);
    }

    private static ExploreDbContext CreateContext(SqliteConnection connection, Guid? tenantId = null)
    {
        var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options)
        {
            TenantContext = tenantId is { } selectedTenant ? new FixedTenant(selectedTenant) : null
        };
        context.BindTenantLease();
        return context;
    }

    private static async Task InitializeAsync(SqliteConnection connection)
    {
        await using ExploreDbContext setup = CreateContext(connection);
        await setup.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(setup, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(setup, CancellationToken.None);
    }

    private static async Task<IntegrationSyncOutbox> SeedAsync(ExploreDbContext context, DateTime startedAt)
    {
        var tenant = new Tenant
        {
            Id = context.TenantFilterTenantId
                ?? throw new InvalidOperationException("The integration seed requires its original tenant lease."),
            FullName = "Integration sync repository test",
            Slug = $"integration-sync-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = startedAt
        };
        var outbox = new IntegrationSyncOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Kind = IntegrationKind.Listmonk,
            SourceType = "test",
            SourceId = Guid.CreateVersion7(),
            SubscriberEmail = "integration-sync@example.test",
            SubscriberPayloadJson = "{}",
            ListmonkListId = 1,
            Status = IntegrationSyncStatus.Processing,
            AttemptCount = 1,
            MaxAttempts = 5,
            ProcessingStartedAt = startedAt,
            ProcessingLeaseToken = Guid.CreateVersion7(),
            CreatedAt = startedAt
        };
        context.AddRange(tenant, outbox);
        await context.SaveChangesAsync();
        return outbox;
    }

    private sealed record FixedTenant(Guid TenantId) : ITenantContext;

    private sealed class QueueHealthGlobalFactory(DbContextOptions<ExploreDbContext> options)
        : PooledDbContextFactory<ExploreDbContext>(options, poolSize: 1), IGlobalExploreDbContextFactory;
}
