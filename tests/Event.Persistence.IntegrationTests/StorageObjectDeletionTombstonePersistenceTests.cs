using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class StorageObjectDeletionTombstonePersistenceTests(EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PhysicalTenantRemovalPreservesIndependentDeletionAuthorityAndItsBinding()
    {
        Guid tenantId = Guid.CreateVersion7();
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var work = StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), tenantId,
            StorageProviders.Local, binding.Id, $"objects/{Guid.CreateVersion7():N}", null, true, Now);
        await using (var context = database.CreateContext())
        {
            context.Tenants.Add(new Tenant
            {
                Id = tenantId, FullName = "Retiring resource tenant", Slug = $"retiring-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!, CreatedAt = Now
            });
            context.Set<StorageProviderBinding>().Add(binding);
            context.Set<StorageObjectDeletionTombstone>().Add(work);
            await context.SaveChangesAsync();
            await Assert.That(await context.Tenants.Where(row => row.Id == tenantId).ExecuteDeleteAsync()).IsEqualTo(1);
        }
        await using var verify = database.CreateContext();
        var retained = await verify.Set<StorageObjectDeletionTombstone>().AsNoTracking()
            .SingleAsync(row => row.Id == work.Id);
        await Assert.That(retained.TenantId).IsEqualTo(tenantId);
        await Assert.That(retained.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(retained.ObjectKey).IsEqualTo(work.ObjectKey);
        await Assert.That(await verify.Set<StorageProviderBinding>().AnyAsync(row => row.Id == binding.Id)).IsTrue();
    }

    [Test]
    public async Task CompetingClaimsAndReclaimedLeasesCannotSettleAnotherWorkersDeletion()
    {
        var work = await SeedAsync(producerSettled: true);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var first = new StorageObjectDeletionTombstoneRepository(firstContext);
        var second = new StorageObjectDeletionTombstoneRepository(secondContext);
        // Both claim the same observed version, regardless of execution ordering.
        var claims = await Task.WhenAll(
            first.TryClaimAsync(work.Id, work.ConcurrencyStamp, Now, Now.AddMinutes(1), default),
            second.TryClaimAsync(work.Id, work.ConcurrencyStamp, Now, Now.AddMinutes(1), default));
        var winner = claims.OfType<StorageObjectDeletionTombstone>().Single();
        var reclaimed = await second.TryClaimAsync(work.Id, winner.ConcurrencyStamp,
            Now.AddMinutes(1), Now.AddMinutes(2), default);
        await Assert.That(reclaimed).IsNotNull();
        await Assert.That(await first.TryRecordAbsenceAsync(work.Id, winner.ConcurrencyStamp,
            Now.AddMinutes(1), default)).IsFalse();
        await Assert.That(await first.TryScheduleRetryAsync(work.Id, winner.ConcurrencyStamp,
            Now.AddMinutes(1), Now.AddMinutes(3), default)).IsFalse();
        await Assert.That(await second.TryRecordAbsenceAsync(work.Id, reclaimed!.ConcurrencyStamp,
            Now.AddMinutes(1), default)).IsTrue();
        await Assert.That(await first.GetByIdAsync(work.Id, default)).IsNull();
    }

    [Test]
    public async Task ProducerAcknowledgementRequiresItsOriginalBindingAndKey()
    {
        var work = await SeedAsync(producerSettled: false);
        await using var context = database.CreateContext();
        var repository = new StorageObjectDeletionTombstoneRepository(context);
        await Assert.That((await repository.ListDueAsync(Now.AddYears(1), 100, default))
            .Any(row => row.Id == work.Id)).IsFalse();
        await Assert.That(await repository.TrySettleProducerAsync(work.Id, Guid.CreateVersion7(),
            work.ObjectKey, null, Now, default)).IsFalse();
        await Assert.That(await repository.TrySettleProducerAsync(work.Id, work.ProviderBindingId,
            $"objects/{Guid.CreateVersion7():N}", null, Now, default)).IsFalse();
        await Assert.That(await repository.TrySettleProducerAsync(work.Id, work.ProviderBindingId,
            work.ObjectKey, null, Now, default)).IsTrue();
        var ready = await repository.GetByIdAsync(work.Id, default);
        await Assert.That(ready!.State).IsEqualTo(StorageObjectDeletionState.Ready);
    }

    private async Task<StorageObjectDeletionTombstone> SeedAsync(bool producerSettled)
    {
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var work = StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            StorageProviders.Local, binding.Id, $"objects/{Guid.CreateVersion7():N}", null, producerSettled, Now);
        await using var context = database.CreateContext();
        context.AddRange(binding, work);
        await context.SaveChangesAsync();
        return work;
    }
}
