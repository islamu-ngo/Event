using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class StorageObjectRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ListDeleteRequestedForResourceAsync_WithAmbientTenant_ReturnsOnlyExplicitTenantResourceRows()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("storage-a");
        var tenantB = CreateTenant("storage-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var resourceId = Guid.CreateVersion7();
        var matching = CreateStorageObject(tenantA.Id, resourceId, StorageObjectLifecycleStates.DeleteRequested);
        var matchingWithoutObjectKey = CreateStorageObject(tenantA.Id, resourceId, StorageObjectLifecycleStates.DeleteRequested);
        matchingWithoutObjectKey.ObjectKey = null;
        var active = CreateStorageObject(tenantA.Id, resourceId, StorageObjectLifecycleStates.Active);
        var otherResource = CreateStorageObject(tenantA.Id, Guid.CreateVersion7(), StorageObjectLifecycleStates.DeleteRequested);
        var unsupportedProvider = CreateStorageObject(tenantA.Id, resourceId, StorageObjectLifecycleStates.DeleteRequested);
        unsupportedProvider.Provider = StorageProviders.LegacyExternal;
        var deleted = CreateStorageObject(tenantA.Id, resourceId, StorageObjectLifecycleStates.DeleteRequested);
        deleted.MarkDeleted(null, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        var ambientTenantMatch = CreateStorageObject(tenantB.Id, resourceId, StorageObjectLifecycleStates.DeleteRequested);
        seedContext.StorageObjects.AddRange(
            matching,
            matchingWithoutObjectKey,
            active,
            otherResource,
            unsupportedProvider,
            deleted,
            ambientTenantMatch);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.StorageObjects
            .AsNoTracking()
            .Select(storageObject => storageObject.Id)
            .ToListAsync();

        var repository = new StorageObjectRepository(tenantBContext);
        var deleteRequestedForTenantA = await repository.ListDeleteRequestedForResourceAsync(
            tenantA.Id,
            ResourceKinds.Event,
            resourceId,
            limit: 10,
            CancellationToken.None);

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([ambientTenantMatch.Id]);
        await Assert.That(deleteRequestedForTenantA.Select(storageObject => storageObject.Id))
            .IsEquivalentTo([matching.Id, matchingWithoutObjectKey.Id]);
        await Assert.That(deleteRequestedForTenantA.Select(storageObject => storageObject.TenantId))
            .IsEquivalentTo([tenantA.Id, tenantA.Id]);
    }

    [Test]
    public async Task ReconciliationBypassQueries_WithInvalidBounds_ReturnEmptyResults()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenant = CreateTenant("storage-guards");
        seedContext.Tenants.Add(tenant);
        await seedContext.SaveChangesAsync();

        var resourceId = Guid.CreateVersion7();
        var active = CreateStorageObject(tenant.Id, resourceId, StorageObjectLifecycleStates.Active);
        var deleteEligible = CreateStorageObject(tenant.Id, resourceId, StorageObjectLifecycleStates.DeleteRequested);
        deleteEligible.UpdatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        seedContext.StorageObjects.AddRange(active, deleteEligible);
        await seedContext.SaveChangesAsync();

        await using var tenantContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenant.Id));
        var repository = new StorageObjectRepository(tenantContext);

        var activeWithZeroLimit = await repository.ListActiveForReconciliationAsync(
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            limit: 0,
            CancellationToken.None);
        var deleteEligibleWithNegativeLimit = await repository.ListDeleteEligibleForReconciliationAsync(
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            limit: -1,
            CancellationToken.None);
        var knownKeysWithBlankProvider = await repository.ListKnownObjectKeysAsync(
            " ",
            [active.ObjectKey!],
            CancellationToken.None);
        var knownKeysWithEmptyKeySet = await repository.ListKnownObjectKeysAsync(
            StorageProviders.Local,
            [],
            CancellationToken.None);

        await Assert.That(activeWithZeroLimit).IsEmpty();
        await Assert.That(deleteEligibleWithNegativeLimit).IsEmpty();
        await Assert.That(knownKeysWithBlankProvider).IsEmpty();
        await Assert.That(knownKeysWithEmptyKeySet).IsEmpty();
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Storage Bypass {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static StorageObject CreateStorageObject(Guid tenantId, Guid resourceId, string lifecycleState)
    {
        var objectId = Guid.CreateVersion7();
        return new StorageObject
        {
            Id = objectId,
            TenantId = tenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Provider = StorageProviders.Local,
            ObjectKey = $"tenants/{tenantId:N}/{objectId:N}.png",
            Uri = $"/storage/{objectId:N}.png",
            FullName = "storage-bypass.png",
            SafeDisplayName = "storage-bypass.png",
            Extension = ".png",
            Size = 100,
            Visibility = StorageObjectVisibilities.PublicImage,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = lifecycleState,
            OwningResourceKind = ResourceKinds.Event,
            OwningResourceId = resourceId,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
