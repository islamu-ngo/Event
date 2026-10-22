using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class EventDiscoveryEnrollmentProducerTests
{
    [Test]
    public async Task ExplicitSaveCreatesCanonicalMetadataOnTheOriginalTransactionBeforeSettlement()
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        await using var writer = store.Open(tenant.Id);
        await using var gate = await new EventDiscoveryEnrollmentGate(writer).AcquireAsync();
        var connector = writer.Database.GetDbConnection();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        writer.Tenants.Add(tenant);

        await writer.SaveChangesAsync();

        await RequireOriginalOwnerAsync(writer, connector, transaction);
        var revision = await writer.Set<EventDiscoveryRevision>().AsNoTracking().SingleAsync();
        await RequireCanonicalAsync(revision, tenant.Id);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task ImplicitSaveWithoutAcceptingChangesPersistsTheNewGraphExactlyOnce()
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        await using var writer = store.Open(tenant.Id);
        writer.Tenants.Add(tenant);

        await writer.SaveChangesAsync(acceptAllChangesOnSuccess: false);

        await Assert.That(writer.Entry(tenant).State).IsEqualTo(EntityState.Added);
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
        await using var reader = store.Open(tenant.Id);
        await Assert.That(await reader.Tenants.CountAsync(row => row.Id == tenant.Id)).IsEqualTo(1);
        await RequireCanonicalAsync(await reader.Set<EventDiscoveryRevision>().SingleAsync(), tenant.Id);
    }

    [Test]
    public async Task LaterRealTenantDisclosureEffectCoalescesWithItsInitialCreationEpoch()
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        await using var writer = store.Open(tenant.Id);
        await using var gate = await new EventDiscoveryEnrollmentGate(writer).AcquireAsync();
        await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
        {
            writer.Tenants.Add(tenant);
            await writer.SaveChangesAsync(token);
            await RequireCanonicalAsync(await writer.Set<EventDiscoveryRevision>().SingleAsync(token), tenant.Id);
            tenant.FullName = "Changed within the original creation transaction";
            await writer.SaveChangesAsync(token);
            return true;
        }, CancellationToken.None);

        await using var reader = store.Open(tenant.Id);
        await Assert.That((await reader.Tenants.SingleAsync(row => row.Id == tenant.Id)).FullName)
            .IsEqualTo("Changed within the original creation transaction");
        await RequireCanonicalAsync(await reader.Set<EventDiscoveryRevision>().SingleAsync(), tenant.Id);
    }

    [Test]
    public async Task ChangedSynthesizedMetadataCannotCompleteItsOriginalCreation()
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        await using var writer = store.Open(tenant.Id);
        await using var gate = await new EventDiscoveryEnrollmentGate(writer).AcquireAsync();
        bool observedCanonicalCreation = false;
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
            {
                writer.Tenants.Add(tenant);
                await writer.SaveChangesAsync(token);
                var revision = await writer.Set<EventDiscoveryRevision>().SingleAsync(token);
                await RequireCanonicalAsync(revision, tenant.Id);
                observedCanonicalCreation = true;
                revision.DisclosureEpoch = 99;
                return true;
            }, CancellationToken.None));

        await Assert.That(observedCanonicalCreation).IsTrue();
        await Assert.That(refusal.Message).IsEqualTo("discovery_enrollment_creation_image_changed");
        await using var reader = store.Open(tenant.Id);
        await Assert.That(await reader.Tenants.AnyAsync(row => row.Id == tenant.Id)).IsFalse();
        await Assert.That(await reader.Set<EventDiscoveryRevision>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ExplicitRevisionSeedRetainsItsIdentityAndEpochsWithoutNormalization()
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        var revision = new EventDiscoveryRevision
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            IdentityEpoch = 3,
            DisclosureEpoch = 5
        };
        await using var writer = store.Open(tenant.Id);
        await using var gate = await new EventDiscoveryEnrollmentGate(writer).AcquireAsync();
        await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
        {
            writer.AddRange(tenant, revision);
            await writer.SaveChangesAsync(token);
            return true;
        }, CancellationToken.None);

        await using var reader = store.Open(tenant.Id);
        var stored = await reader.Set<EventDiscoveryRevision>().SingleAsync();
        await Assert.That(stored.Id).IsEqualTo(revision.Id);
        await Assert.That(stored.IdentityEpoch).IsEqualTo(3);
        await Assert.That(stored.DisclosureEpoch).IsEqualTo(5);
    }

    [Test]
    public async Task FailureAfterSuccessfulCanonicalSaveRollsBackBothParentAndMetadata()
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        await using var writer = store.Open(tenant.Id);
        await using var gate = await new EventDiscoveryEnrollmentGate(writer).AcquireAsync();
        bool observedCanonicalCreation = false;
        await Assert.ThrowsAsync<CreationRefusedException>(() =>
            new EfCoreUnitOfWork(writer).ExecuteSerializableAsync<bool>(async token =>
            {
                writer.Tenants.Add(tenant);
                await writer.SaveChangesAsync(token);
                await RequireCanonicalAsync(await writer.Set<EventDiscoveryRevision>().SingleAsync(token), tenant.Id);
                observedCanonicalCreation = true;
                throw new CreationRefusedException();
            }, CancellationToken.None));

        await Assert.That(observedCanonicalCreation).IsTrue();
        await using var reader = store.Open(tenant.Id);
        await Assert.That(await reader.Tenants.AnyAsync(row => row.Id == tenant.Id)).IsFalse();
        await Assert.That(await reader.Set<EventDiscoveryRevision>().AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SavepointRollbackCannotReuseACompletedCreationReceipt(bool flushBeforeRollback)
    {
        await using var store = await Store.CreateAsync();
        var tenant = NewTenant();
        await using var writer = store.Open(tenant.Id);
        await using var gate = await new EventDiscoveryEnrollmentGate(writer).AcquireAsync();
        bool observedCanonicalCreation = false;
        bool refusedReusedReceipt = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
            {
                var transaction = writer.Database.CurrentTransaction!;
                await transaction.CreateSavepointAsync("before_enrollment", token);
                writer.Tenants.Add(tenant);
                await writer.SaveChangesAsync(token);
                var revision = await writer.Set<EventDiscoveryRevision>().SingleAsync(token);
                await RequireCanonicalAsync(revision, tenant.Id);
                observedCanonicalCreation = true;
                if (flushBeforeRollback)
                    await writer.FlushDisclosureAsync(token);

                await transaction.RollbackToSavepointAsync("before_enrollment", token);
                // Database effects disappeared, but EF's accepted source image did
                // not. Re-adding ONLY Tenant must never manufacture a new receipt.
                await Assert.That(writer.Entry(revision).State).IsEqualTo(EntityState.Unchanged);
                await Assert.That(await writer.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenant.Id, token)).IsFalse();
                await Assert.That(await writer.Set<EventDiscoveryRevision>().AsNoTracking().AnyAsync(token)).IsFalse();
                writer.Entry(tenant).State = EntityState.Added;
                try
                {
                    await writer.SaveChangesAsync(token);
                }
                catch (InvalidOperationException)
                {
                    refusedReusedReceipt = true;
                }
                // Catching the immediate refusal cannot authorize settlement of
                // the original attempt, including a scope already flushed once.
                return true;
            }, CancellationToken.None));

        await Assert.That(observedCanonicalCreation).IsTrue();
        await Assert.That(refusedReusedReceipt).IsTrue();
        await using var reader = store.Open(tenant.Id);
        await Assert.That(await reader.Tenants.AnyAsync(row => row.Id == tenant.Id)).IsFalse();
        await Assert.That(await reader.Set<EventDiscoveryRevision>().AnyAsync()).IsFalse();
    }

    private static async Task RequireCanonicalAsync(EventDiscoveryRevision revision, Guid tenantId)
    {
        await Assert.That(revision.TenantId).IsEqualTo(tenantId);
        await Assert.That(revision.Id.Version).IsEqualTo(7);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(0);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(1);
    }

    private sealed class CreationRefusedException : Exception;
}
