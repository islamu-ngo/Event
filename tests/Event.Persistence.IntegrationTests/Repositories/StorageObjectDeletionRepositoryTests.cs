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
    public async Task FinalCustodyReadRequiresExactPersistedClaimWithoutRenewal()
    {
        await fixture.ResetAsync();
        var now = new DateTime(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var work = StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            StorageProviders.Local, binding.Id, $"objects/{Guid.CreateVersion7():N}", "original-version", true, now);
        await using var context = fixture.CreateDbContext();
        context.AddRange(binding, work);
        await context.SaveChangesAsync();
        var repository = new StorageObjectDeletionTombstoneRepository(context);
        var claim = (await repository.TryClaimAsync(work.Id, work.ConcurrencyStamp, now, now.AddMinutes(5), default))!;
        await Assert.That(await repository.HasLiveClaimAsync(claim, now.AddMinutes(1), default)).IsTrue();

        foreach (string mismatch in new[] { "object", "tenant", "provider", "binding", "key", "version", "stamp", "expiry" })
        {
            var forged = StorageObjectDeletionTombstone.Create(
                mismatch == "object" ? Guid.CreateVersion7() : claim.Id,
                mismatch == "tenant" ? Guid.CreateVersion7() : claim.TenantId,
                mismatch == "provider" ? StorageProviders.S3Compatible : claim.Provider,
                mismatch == "binding" ? Guid.CreateVersion7() : claim.ProviderBindingId,
                mismatch == "key" ? "objects/different" : claim.ObjectKey,
                mismatch == "version" ? "different-version" : claim.ProviderObjectVersion, true, now);
            forged.TryClaim(forged.ConcurrencyStamp,
                mismatch == "stamp" ? Guid.CreateVersion7() : claim.ConcurrencyStamp,
                now, mismatch == "expiry" ? now.AddMinutes(6) : claim.LeaseExpiresAtUtc!.Value);
            await Assert.That(await repository.HasLiveClaimAsync(forged, now.AddMinutes(1), default)).IsFalse();
        }
        await Assert.That(await repository.HasLiveClaimAsync(claim, now.AddMinutes(5), default)).IsFalse();
        var unchanged = (await repository.GetByIdAsync(work.Id, default))!;
        await Assert.That(unchanged.ConcurrencyStamp).IsEqualTo(claim.ConcurrencyStamp);
        await Assert.That(unchanged.LeaseExpiresAtUtc).IsEqualTo(claim.LeaseExpiresAtUtc);

        await using var competingContext = fixture.CreateDbContext();
        var competing = new StorageObjectDeletionTombstoneRepository(competingContext);
        await Assert.That(await competing.TryScheduleRetryAsync(claim.Id, claim.ConcurrencyStamp,
            now.AddMinutes(1), now.AddMinutes(2), default)).IsTrue();
        var ready = (await competing.GetByIdAsync(work.Id, default))!;
        var replacement = (await competing.TryClaimAsync(work.Id, ready.ConcurrencyStamp,
            now.AddMinutes(2), now.AddMinutes(7), default))!;
        await Assert.That(await repository.HasLiveClaimAsync(claim, now.AddMinutes(3), default)).IsFalse();
        await Assert.That(await repository.HasLiveClaimAsync(replacement, now.AddMinutes(3), default)).IsTrue();
        var retained = (await competing.GetByIdAsync(work.Id, default))!;
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(replacement.ConcurrencyStamp);
        await Assert.That(retained.LeaseExpiresAtUtc).IsEqualTo(now.AddMinutes(7));
    }

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
