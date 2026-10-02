using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Storage;

[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class ManagedStorageRetirementTests(EventResourceFileUploadTests.Database database)
{
    [Test]
    public async Task CommittedRetirementPreventsANewManagedProfileReference()
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
            seed.Add(StorageObjectDeletionTombstone.Create(objectId, tenant.Id,
                source.Provider, binding.Id, source.ObjectKey!, null, true,
                new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            await seed.SaveChangesAsync();
        }

        await using (var attach = database.CreateContext())
        {
            var actor = await attach.Actors.SingleAsync(row => row.Id == actorId);
            actor.Pii.SetProfilePicture(objectId, null);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => attach.SaveChangesAsync());
        }
        await using var verify = database.CreateContext();
        await Assert.That((await verify.Set<ActorPii>().SingleAsync(row => row.ActorId == actorId))
            .ProfilePictureStorageObjectId).IsNull();
        await Assert.That(await verify.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == objectId)).IsTrue();
    }
}
