using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

public sealed class ManagedStorageRetirementTests
{
    [Test]
    public async Task DetachingOneActorImagePreservesTheOtherUseAndCapturedBytes()
    {
        var source = new StorageObject
        {
            Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), Tenant = null!, FileType = null!,
            Provider = StorageProviders.Local, StorageProviderBindingId = Guid.CreateVersion7(),
            ObjectKey = "images/shared.png", ProviderVersionId = "captured-version",
            FullName = "shared.png", SafeDisplayName = "shared.png", Extension = "png",
            Purpose = StorageObjectPurposes.ProfileImage, Visibility = StorageObjectVisibilities.PublicImage,
            LifecycleState = StorageObjectLifecycleStates.Active
        };
        var first = new ActorPii { DisplayName = "First owner" };
        var surviving = new ActorPii { DisplayName = "Surviving owner" };
        first.SetProfilePicture(source.Id, null);
        surviving.SetProfilePicture(source.Id, null);
        var identity = (source.StorageProviderBindingId, source.ObjectKey, source.ProviderVersionId);

        first.SetProfilePicture(null, null);

        await Assert.That(first.ProfilePictureStorageObjectId).IsNull();
        await Assert.That(surviving.ProfilePictureStorageObjectId).IsEqualTo(source.Id);
        await Assert.That(source.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
        await Assert.That(source.IsDeleted).IsFalse();
        await Assert.That((source.StorageProviderBindingId, source.ObjectKey, source.ProviderVersionId))
            .IsEqualTo(identity);
    }

    [Test]
    public async Task UnknownDeleteOutcomeRetainsExactAuthorityWithoutInventingAbsence()
    {
        DateTime now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var authority = StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            StorageProviders.S3Compatible, Guid.CreateVersion7(), "images/shared.png", "captured-version", true, now);
        Guid claim = Guid.CreateVersion7();
        await Assert.That(authority.TryClaim(authority.ConcurrencyStamp, claim, now, now.AddMinutes(1))).IsTrue();
        var identity = (authority.Id, authority.TenantId, authority.ProviderBindingId,
            authority.ObjectKey, authority.ProviderObjectVersion);

        await Assert.That(authority.TryScheduleRetry(claim, now.AddSeconds(1), now.AddMinutes(2))).IsTrue();

        await Assert.That(authority.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(authority.TryRecordAbsence(claim, now.AddSeconds(2))).IsFalse();
        await Assert.That(authority.TrySettleProducer("replacement-version", now.AddSeconds(2))).IsFalse();
        await Assert.That((authority.Id, authority.TenantId, authority.ProviderBindingId,
            authority.ObjectKey, authority.ProviderObjectVersion)).IsEqualTo(identity);
    }
}
