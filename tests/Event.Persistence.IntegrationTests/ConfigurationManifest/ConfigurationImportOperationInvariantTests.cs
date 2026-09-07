namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ConfigurationImportAtomicityTests(
    PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ConcurrentSameSession_CommitsOneReceiptOnly()
    {
        await fixture.ResetAsync();
        Guid sessionId = Guid.CreateVersion7();
        await using ExploreDbContext first = fixture.CreateDbContext();
        await using ExploreDbContext second = fixture.CreateDbContext();
        await first.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var firstTransaction =
                await first.Database.BeginTransactionAsync();
            first.ConfigurationImportOperations.Add(
                ConfigurationImportOperationTestData.Applied(sessionId));
            await first.SaveChangesAsync();

            second.ConfigurationImportOperations.Add(
                ConfigurationImportOperationTestData.Applied(sessionId));
            Task secondWrite = second.SaveChangesAsync();
            await firstTransaction.CommitAsync();

            await Assert.That(async () => await secondWrite)
                .Throws<DbUpdateException>();
        });
        await using ExploreDbContext verification = fixture.CreateDbContext();
        await Assert.That(await verification.ConfigurationImportOperations
                .CountAsync(operation => operation.SessionId == sessionId))
            .IsEqualTo(1);
    }

    [Test]
    public async Task RolledBackTransaction_LeavesNoReceiptOrSnapshotAuthority()
    {
        await fixture.ResetAsync();
        Guid operationId = Guid.CreateVersion7();
        await using ExploreDbContext context = fixture.CreateDbContext();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction =
                await context.Database.BeginTransactionAsync();
            context.ConfigurationImportOperations.Add(
                ConfigurationImportOperationTestData.Applied(
                    Guid.CreateVersion7(),
                    operationId));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        });
        context.ChangeTracker.Clear();

        await Assert.That(await context.ConfigurationImportOperations
                .AnyAsync(operation => operation.Id == operationId))
            .IsFalse();
    }
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ConfigurationImportRecoveryTests(
    PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ForwardRollback_AppendsLinkedHistoryWithoutChangingSourceReceipt()
    {
        await fixture.ResetAsync();
        Guid sourceId = Guid.CreateVersion7();
        ConfigurationImportOperation source =
            ConfigurationImportOperationTestData.Applied(
                Guid.CreateVersion7(),
                sourceId);
        ConfigurationImportOperation rollback =
            ConfigurationImportOperationTestData.Applied(
                Guid.CreateVersion7(),
                sourceOperationId: sourceId);
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new ConfigurationImportOperationRepository(context);

        await repository.AddAsync(source, CancellationToken.None);
        await repository.AddAsync(rollback, CancellationToken.None);
        IReadOnlyList<ConfigurationImportOperation> history =
            await repository.ListAsync("instance", 10, CancellationToken.None);

        await Assert.That(history).HasCount(2);
        await Assert.That(history.Single(operation => operation.Id == sourceId).Status)
            .IsEqualTo(ConfigurationImportOperationStatus.Applied);
        ConfigurationImportOperation recovered = history.Single(operation =>
            operation.SourceOperationId == sourceId);
        await Assert.That(recovered.Kind)
            .IsEqualTo(ConfigurationImportOperationKind.ForwardRollback);
        await Assert.That(recovered.Status)
            .IsEqualTo(ConfigurationImportOperationStatus.RolledBack);
        await Assert.That(recovered.SnapshotArtifactHandleId).IsNotNull();
    }

    [Test]
    public async Task HistoryAndReceipt_AreIsolatedByTrustedTargetAuthority()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        string tenantAuthority =
            ConfigurationImportTarget.ForTenant(tenantId).AuthorityKey;
        ConfigurationImportOperation tenantOperation =
            ConfigurationImportOperationTestData.Applied(
                Guid.CreateVersion7(),
                targetAuthorityKey: tenantAuthority,
                targetTenantId: tenantId);
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new ConfigurationImportOperationRepository(context);
        await repository.AddAsync(tenantOperation, CancellationToken.None);

        ConfigurationImportOperation? matching = await repository.GetByIdAsync(
            tenantOperation.Id,
            tenantAuthority,
            CancellationToken.None);
        ConfigurationImportOperation? crossTenant = await repository.GetByIdAsync(
            tenantOperation.Id,
            ConfigurationImportTarget.ForTenant(Guid.CreateVersion7()).AuthorityKey,
            CancellationToken.None);

        await Assert.That(matching?.Id).IsEqualTo(tenantOperation.Id);
        await Assert.That(crossTenant).IsNull();
    }
}

internal static class ConfigurationImportOperationTestData
{
    private static readonly DateTime Now =
        new(2026, 8, 30, 20, 0, 0, DateTimeKind.Utc);

    public static ConfigurationImportOperation Applied(
        Guid sessionId,
        Guid? operationId = null,
        Guid? sourceOperationId = null,
        string targetAuthorityKey = "instance",
        Guid? targetTenantId = null) =>
        ConfigurationImportOperation.CreateApplied(
            operationId ?? Guid.CreateVersion7(),
            sessionId,
            targetAuthorityKey,
            targetTenantId,
            Guid.CreateVersion7(),
            sourceOperationId,
            Digest("artifact"),
            Digest("target"),
            Digest("sections"),
            Digest("mapping"),
            Digest("approval"),
            applyMode: 3,
            [targetTenantId.HasValue ? "tenant.settings" : "instance.settings"],
            Guid.CreateVersion7(),
            Digest("snapshot"),
            Now.AddDays(7),
            Guid.CreateVersion7(),
            fidelityVerified: true,
            Digest("fidelity"),
            ["excluded.secrets"],
            Now,
            Now.AddMinutes(1));

    private static string Digest(string value) =>
        ConfigurationImportDigest.Compute([value]);
}
