using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TicketingLifecycleRecoveryInvariantTests(
    PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow =
        new(2026, 8, 29, 14, 45, 0, DateTimeKind.Utc);

    [Test]
    public async Task ManifestRejectsMissingKeyCursorFenceIdempotencyAndMixedRevision()
    {
        await Assert.That(() => CreateManifest(retainedKeyVersion: 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateManifest(providerCursor: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateManifest(idempotencyFloor: -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateManifest(workerFence: -1))
            .Throws<ArgumentOutOfRangeException>();

        TicketingRecoveryCheckpoint checkpoint =
            TicketingRecoveryCheckpoint.Begin(
                CreateManifest(),
                UtcNow);
        await Assert.That(checkpoint.Validate(
                runningReleaseRevision: "release-other",
                runningSchemaRevision: "schema-7",
                minimumRetainedKeyVersion: 3,
                minimumAuthorityFloor: 80,
                minimumProviderCursor: 90,
                minimumIdempotencyFloor: 100,
                minimumWorkerFence: 110,
                validatedAtUtc: UtcNow.AddMinutes(1)))
            .IsEqualTo(
                TicketingRecoveryValidationOutcome.ReleaseMismatch);
        await Assert.That(checkpoint.Status)
            .IsEqualTo(TicketingRecoveryStatus.RecoveryOnly);
    }

    [Test]
    public async Task PreRevocationRestoreCannotReopenBeforeEveryBearerGenerationRotates()
    {
        TicketingRecoveryCheckpoint checkpoint =
            TicketingRecoveryCheckpoint.Begin(
                CreateManifest(
                    capabilityGeneration: 5,
                    credentialGeneration: 8,
                    workerFence: 110),
                UtcNow);
        await Assert.That(checkpoint.Validate(
                "release-8",
                "schema-7",
                3,
                80,
                90,
                100,
                110,
                UtcNow.AddMinutes(1)))
            .IsEqualTo(
                TicketingRecoveryValidationOutcome.Validated);
        await Assert.That(checkpoint.TryOpenWorkers(
                111,
                UtcNow.AddMinutes(2)))
            .IsFalse();
        await Assert.That(checkpoint.TryOpenSales(
                UtcNow.AddMinutes(2)))
            .IsFalse();
        await Assert.That(
                checkpoint.TryRotateBearerAuthority(
                    capabilityGeneration: 5,
                    credentialGeneration: 9,
                    workerFence: 111,
                    rotatedAtUtc: UtcNow.AddMinutes(2)))
            .IsFalse();
        await Assert.That(
                checkpoint.TryRotateBearerAuthority(
                    capabilityGeneration: 6,
                    credentialGeneration: 9,
                    workerFence: 111,
                    rotatedAtUtc: UtcNow.AddMinutes(2)))
            .IsTrue();
        await Assert.That(checkpoint.TryOpenWorkers(
                110,
                UtcNow.AddMinutes(3)))
            .IsFalse();
        await Assert.That(checkpoint.TryOpenWorkers(
                111,
                UtcNow.AddMinutes(3)))
            .IsTrue();
        await Assert.That(checkpoint.TryOpenSales(
                UtcNow.AddMinutes(4)))
            .IsTrue();
        await Assert.That(checkpoint.Status)
            .IsEqualTo(TicketingRecoveryStatus.SalesOpen);
    }

    [Test]
    public async Task PersistenceModelEnforcesTenantManifestReplayAndUniqueReissue()
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType? checkpoint =
            model.FindEntityType(
                typeof(TicketingRecoveryCheckpoint));
        IEntityType? reissue =
            model.FindEntityType(
                typeof(TicketingRecoveryReissueIntent));

        await Assert.That(checkpoint).IsNotNull();
        await Assert.That(reissue).IsNotNull();
        if (checkpoint is null || reissue is null)
        {
            return;
        }

        await Assert.That(checkpoint.FindDeclaredQueryFilter(QueryFilterNames.Tenant))
            .IsNotNull();
        await Assert.That(reissue.FindDeclaredQueryFilter(QueryFilterNames.Tenant))
            .IsNotNull();
        await Assert.That(checkpoint.FindProperty(
                nameof(TicketingRecoveryCheckpoint.ConcurrencyStamp))!
                .IsConcurrencyToken)
            .IsTrue();
        await Assert.That(HasUniqueIndex(
                checkpoint,
                nameof(TicketingRecoveryCheckpoint.TenantId),
                nameof(TicketingRecoveryCheckpoint.RecoveryOperationId)))
            .IsTrue();
        await Assert.That(HasUniqueIndex(
                reissue,
                nameof(TicketingRecoveryReissueIntent.TenantId),
                nameof(TicketingRecoveryReissueIntent.RecoveryOperationId),
                nameof(TicketingRecoveryReissueIntent.AdmissionTicketId)))
            .IsTrue();
        await Assert.That(reissue.GetProperties().Any(property =>
                property.Name.Contains("CredentialDigest", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Capability", StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
    }

    [Test]
    public async Task RepositoryReplayIsTenantQualifiedAndCreatesNoDuplicateEffects()
    {
        await fixture.ResetAsync();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        Guid operationId = Guid.CreateVersion7();
        TicketingRecoveryManifest manifestA = CreateManifest(
            operationId: operationId,
            tenantId: tenantA);
        TicketingRecoveryManifest manifestB = CreateManifest(
            operationId: operationId,
            tenantId: tenantB);

        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository =
            new TicketingRecoveryRepository(context);
        TicketingRecoveryCheckpoint first =
            await repository.BeginRecoveryAsync(
            manifestA,
            UtcNow,
            CancellationToken.None);
        TicketingRecoveryCheckpoint replay =
            await repository.BeginRecoveryAsync(
            manifestA,
            UtcNow,
            CancellationToken.None);
        TicketingRecoveryCheckpoint otherTenant =
            await repository.BeginRecoveryAsync(
            manifestB,
            UtcNow,
            CancellationToken.None);

        context.EnableTenantFilterBypass(
            "Phase 6 recovery invariant verification reads exact tenant-qualified rows.");
        await Assert.That(first.Id)
            .IsEqualTo(replay.Id);
        await Assert.That(otherTenant.Id)
            .IsNotEqualTo(first.Id);
        await Assert.That(
                await context.TicketingRecoveryCheckpoints
                    .CountAsync())
            .IsEqualTo(2);
        await Assert.That(
                await context.TicketingRecoveryReissueIntents
                    .CountAsync())
            .IsEqualTo(0);
    }

    private static TicketingRecoveryManifest CreateManifest(
        Guid? operationId = null,
        Guid? tenantId = null,
        int retainedKeyVersion = 3,
        long providerCursor = 90,
        long idempotencyFloor = 100,
        long workerFence = 110,
        int capabilityGeneration = 5,
        int credentialGeneration = 8) =>
        TicketingRecoveryManifest.Create(
            operationId ?? Guid.CreateVersion7(),
            tenantId ?? Guid.CreateVersion7(),
            "release-8",
            "schema-7",
            70,
            UtcNow,
            retainedKeyVersion,
            80,
            providerCursor,
            idempotencyFloor,
            workerFence,
            capabilityGeneration,
            credentialGeneration,
            new string('a', 64));

    private static bool HasUniqueIndex(IEntityType entity, params string[] names) =>
        entity.GetIndexes().Any(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(names));
}
