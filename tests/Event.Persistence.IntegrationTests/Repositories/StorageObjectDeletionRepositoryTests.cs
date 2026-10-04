using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class StorageObjectDeletionRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ListDeleteRequestedForResourceAsync_ReturnsOnlyMatchingDeleteRequestedRows()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            FullName = "Storage Delete Retry Tenant",
            Slug = "storage-delete-retry-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var eventId = Guid.CreateVersion7();
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        context.Add(binding);
        var matching = CreateStorageObject(tenant.Id, eventId, StorageObjectLifecycleStates.DeleteRequested, binding);
        var missingKey = CreateStorageObject(tenant.Id, eventId, StorageObjectLifecycleStates.DeleteRequested, binding);
        missingKey.ObjectKey = null;
        var active = CreateStorageObject(tenant.Id, eventId, StorageObjectLifecycleStates.Active, binding);
        var otherEvent = CreateStorageObject(tenant.Id, Guid.CreateVersion7(), StorageObjectLifecycleStates.DeleteRequested, binding);
        var alreadyDeleted = CreateStorageObject(tenant.Id, eventId, StorageObjectLifecycleStates.DeleteRequested, binding);
        alreadyDeleted.MarkDeleted(null, new DateTime(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc));
        context.StorageObjects.AddRange(matching, active, otherEvent, alreadyDeleted);
        await context.SaveChangesAsync();
        var repository = new StorageObjectRepository(context);

        var results = await repository.ListDeleteRequestedForResourceAsync(
            tenant.Id,
            ResourceKinds.Event,
            eventId,
            limit: 10,
            CancellationToken.None);

        await Assert.That(results.Select(storageObject => storageObject.Id)).IsEquivalentTo([matching.Id]);
        await using var invalid = fixture.CreateDbContext();
        invalid.StorageObjects.Add(missingKey);
        await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
    }

    private static StorageObject CreateStorageObject(Guid tenantId, Guid eventId, string lifecycleState, StorageProviderBinding binding) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        FileTypeId = (int)FileTypeEnum.Image,
        FileType = null!,
        Provider = StorageProviders.Local,
        StorageProviderBindingId = binding.Id,
        ObjectKey = $"tenants/{tenantId:N}/{Guid.CreateVersion7():N}.png",
        FullName = "redacted.png",
        SafeDisplayName = "redacted.png",
        Extension = ".png",
        Size = 100,
        Visibility = StorageObjectVisibilities.PublicImage,
        Purpose = StorageObjectPurposes.EventImage,
        LifecycleState = lifecycleState,
        OwningResourceKind = ResourceKinds.Event,
        OwningResourceId = eventId,
        CreatedAt = new DateTime(2026, 6, 23, 9, 0, 0, DateTimeKind.Utc)
    };
}
