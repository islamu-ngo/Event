using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Storage;

[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class ManagedStorageRetirementTests(EventResourceFileUploadTests.Database database)
{
    [Test]
    public async Task TenantDeletionFencesUnloadedSeriesPictureWithoutRetiringSharedSource()
    {
        await using var fixture = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await fixture.SeedScopeAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid seriesId = Guid.CreateVersion7();
        Guid originalStamp;
        await using (var seed = database.CreateContext())
        {
            seed.Tenants.Add(new Tenant
            {
                Id = tenantId,
                FullName = "Cascade owner",
                Slug = $"cascade-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            seed.EventSeries.Add(new EventSeries
            {
                Id = seriesId,
                TenantId = tenantId,
                Title = "Physical shared reference",
                ActorId = scope.ActorId,
                FeaturedImageId = scope.StorageAId,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!
            });
            await seed.SaveChangesAsync();
            originalStamp = await seed.StorageObjects.AsNoTracking().Where(row => row.Id == scope.StorageAId)
                .Select(row => row.ConcurrencyStamp).SingleAsync();
        }
        await using (var removing = database.CreateContext())
        {
            ITenantRepository repository = new TenantRepository(removing);
            var tenant = (await repository.GetById(tenantId))!;
            await repository.Delete(tenant);
        }
        await using var verify = database.CreateContext();
        await Assert.That(await verify.Tenants.AnyAsync(row => row.Id == tenantId)).IsFalse();
        await Assert.That(await verify.EventSeries.IgnoreQueryFilters().AnyAsync(row => row.Id == seriesId)).IsFalse();
        var source = await verify.StorageObjects.AsNoTracking().SingleAsync(row => row.Id == scope.StorageAId);
        await Assert.That(source.ConcurrencyStamp).IsNotEqualTo(originalStamp);
        await Assert.That(source.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
        await Assert.That(await verify.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == source.Id)).IsFalse();
    }

    [Test]
    public async Task BulkActorPiiDetachmentFencesPictureWithoutDeletingItsBytes()
    {
        await using var fixture = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await fixture.SeedScopeAsync();
        Guid originalStamp;
        await using (var seed = database.CreateContext())
        {
            var picture = await seed.ActorPii.SingleAsync(item => item.ActorId == scope.ActorId);
            picture.SetProfilePicture(scope.StorageAId, null);
            await seed.SaveChangesAsync();
            originalStamp = await seed.StorageObjects.AsNoTracking().Where(item => item.Id == scope.StorageAId)
                .Select(item => item.ConcurrencyStamp).SingleAsync();
        }
        await using (var context = database.CreateContext())
            await Assert.That(await new ActorRepository(context).ForgetPiiAsync(scope.ActorId)).IsEqualTo(1);
        await using var verify = database.CreateContext();
        var source = await verify.StorageObjects.AsNoTracking().SingleAsync(item => item.Id == scope.StorageAId);
        await Assert.That(source.ConcurrencyStamp).IsNotEqualTo(originalStamp);
        await Assert.That(source.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
        await Assert.That(await verify.ActorPii.AnyAsync(item => item.ActorId == scope.ActorId)).IsFalse();
        await Assert.That(await verify.StorageObjectDeletionTombstones.AnyAsync(item => item.Id == scope.StorageAId)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetirementPreservesSurvivingUsageRegardlessOfHistoricalCharge(bool previouslyCharged)
    {
        await using var fixture = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await fixture.SeedScopeAsync();
        Guid survivingId = Guid.CreateVersion7();
        await using (var seed = database.CreateContext())
        {
            var source = await seed.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
            source.Purpose = StorageObjectPurposes.EventResource;
            source.OwningResourceKind = StorageOwningResourceKinds.EventResource;
            source.OwningResourceId = Guid.CreateVersion7();
            seed.Add(new StorageObject
            {
                Id = survivingId,
                TenantId = scope.TenantAId,
                Tenant = null!,
                FileTypeId = source.FileTypeId,
                FileType = null!,
                StorageProviderBindingId = source.StorageProviderBindingId,
                Provider = source.Provider,
                ObjectKey = $"fixtures/{survivingId:N}.pdf",
                FullName = "surviving.pdf",
                SafeDisplayName = "surviving.pdf",
                Extension = ".pdf",
                ContentType = "application/pdf",
                Size = 500,
                Purpose = StorageObjectPurposes.Document,
                Visibility = StorageObjectVisibilities.PrivateOwner,
                LifecycleState = StorageObjectLifecycleStates.Active
            });
            seed.Add(new StorageUsageCounter
            {
                TenantId = scope.TenantAId,
                Provider = source.Provider,
                UsedBytes = 500 + (previouslyCharged ? source.Size : 0),
                ObjectCount = previouslyCharged ? 2 : 1
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var lifecycle = new EventResourceStorageLifecycleRepository(context);
            await lifecycle.RetireAsync(scope.TenantAId, [], [scope.StorageAId],
                new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc), default);
            await transaction.CommitAsync();
        }
        await using var verify = database.CreateContext();
        var counter = await verify.StorageUsageCounters.SingleAsync(row => row.TenantId == scope.TenantAId);
        await Assert.That(counter.UsedBytes).IsEqualTo(500L);
        await Assert.That(counter.ObjectCount).IsEqualTo(1L);
        await Assert.That(await verify.StorageObjects.AnyAsync(row => row.Id == survivingId
            && row.LifecycleState == StorageObjectLifecycleStates.Active)).IsTrue();
        await Assert.That(await verify.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == scope.StorageAId)).IsTrue();
        using var meter = new Meter(BusinessMetrics.MeterName);
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(meter);
        var administration = new InstanceStorageSettingService(Substitute.For<ISystemSettingRepository>(),
            Substitute.For<IStoragePolicyResolver>(), new StorageUsageCounterRepository(verify),
            new StorageObjectRepository(verify), new EfCoreUnitOfWork(verify), new BusinessMetrics(meterFactory));
        await administration.RecalculateUsageAsync();
        await verify.Entry(counter).ReloadAsync();
        await Assert.That(counter.UsedBytes).IsEqualTo(500L);
        await Assert.That(counter.ObjectCount).IsEqualTo(1L);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RetiredSourceCannotBeAttachedWithoutAuthorizedActivationProof(bool committedAuthority)
    {
        Guid actorId = Guid.CreateVersion7();
        Guid objectId = Guid.CreateVersion7();
        await using (var seed = database.CreateContext())
        {
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Retirement invariant",
                Slug = $"retirement-{objectId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            var group = new Group { Id = Guid.CreateVersion7(), FullName = "Reference owner" };
            var actor = new Actor
            {
                Id = actorId,
                Group = group,
                GroupId = group.Id,
                ActorTypeId = (int)ActorTypeEnum.Group,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Reference owner" }
            };
            var binding = StorageProviderBinding.Local(Path.GetTempPath());
            var source = new StorageObject
            {
                Id = objectId,
                Tenant = tenant,
                TenantId = tenant.Id,
                FileTypeId = (int)FileTypeEnum.Image,
                FileType = null!,
                Provider = StorageProviders.Local,
                StorageProviderBindingId = binding.Id,
                ObjectKey = $"images/{objectId:N}.png",
                FullName = "retired.png",
                SafeDisplayName = "retired.png",
                Extension = "png",
                ContentType = "image/png",
                Size = 32,
                Visibility = StorageObjectVisibilities.PublicImage,
                Purpose = StorageObjectPurposes.ProfileImage,
                LifecycleState = StorageObjectLifecycleStates.Active
            };
            seed.AddRange(binding, source, actor);
            await seed.SaveChangesAsync();
            source.RequestDelete();
            if (committedAuthority)
                seed.Add(StorageObjectDeletionTombstone.Create(objectId, tenant.Id,
                    source.Provider, binding.Id, source.ObjectKey!, null, true,
                    new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            await seed.SaveChangesAsync();
        }

        await using (var attach = database.CreateContext())
        {
            var actor = await attach.Actors.SingleAsync(row => row.Id == actorId);
            if (!committedAuthority)
                (await attach.StorageObjects.SingleAsync(row => row.Id == objectId)).LifecycleState =
                    StorageObjectLifecycleStates.Active;
            actor.Pii.SetProfilePicture(objectId, null);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => attach.SaveChangesAsync());
        }
        await using var verify = database.CreateContext();
        await Assert.That((await verify.Set<ActorPii>().SingleAsync(row => row.ActorId == actorId))
            .ProfilePictureStorageObjectId).IsNull();
        await Assert.That(await verify.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == objectId))
            .IsEqualTo(committedAuthority);
        await Assert.That((await verify.StorageObjects.SingleAsync(row => row.Id == objectId)).LifecycleState)
            .IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
    }
}
