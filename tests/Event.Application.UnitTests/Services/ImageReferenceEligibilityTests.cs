using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class ImageReferenceEligibilityTests
{
    [Test]
    [Arguments("missing")]
    [Arguments("cross_tenant")]
    [Arguments("inactive")]
    [Arguments("unsafe_mime")]
    [Arguments("unsafe_extension")]
    [Arguments("private")]
    public async Task AreEligibleAsync_WhenReferenceIsIneligible_ReturnsFalse(string scenario)
    {
        var tenantId = Guid.CreateVersion7();
        var storageObjectId = Guid.CreateVersion7();
        var repository = Substitute.For<IStorageObjectRepository>();
        StorageObject? storageObject = scenario == "missing"
            ? null
            : SafeImage(storageObjectId, tenantId);

        switch (scenario)
        {
            case "cross_tenant":
                storageObject!.TenantId = Guid.CreateVersion7();
                break;
            case "inactive":
                storageObject!.LifecycleState = StorageObjectLifecycleStates.Pending;
                break;
            case "unsafe_mime":
                storageObject!.ContentType = "image/svg+xml";
                break;
            case "unsafe_extension":
                storageObject!.Extension = "svg";
                break;
            case "private":
                storageObject!.Visibility = StorageObjectVisibilities.PrivateOwner;
                break;
        }

        repository.GetById(storageObjectId).Returns(storageObject);

        bool result = await ImageReferenceEligibility.AreEligibleAsync(
            repository,
            tenantId,
            storageObjectId);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AreEligibleAsync_WhenReferenceIsSameTenantActivePublicSafeRaster_ReturnsTrue()
    {
        var tenantId = Guid.CreateVersion7();
        var storageObjectId = Guid.CreateVersion7();
        var repository = Substitute.For<IStorageObjectRepository>();
        repository.GetById(storageObjectId).Returns(SafeImage(storageObjectId, tenantId));

        bool result = await ImageReferenceEligibility.AreEligibleAsync(
            repository,
            tenantId,
            storageObjectId);

        await Assert.That(result).IsTrue();
    }

    private static StorageObject SafeImage(Guid id, Guid tenantId) => new()
    {
        Id = id,
        TenantId = tenantId,
        Tenant = null!,
        FileType = null!,
        Provider = "local",
        Uri = "storage://image",
        FullName = "image.png",
        SafeDisplayName = "image.png",
        Extension = "png",
        ContentType = "image/png",
        Purpose = StorageObjectPurposes.EventImage,
        Visibility = StorageObjectVisibilities.PublicImage,
        LifecycleState = StorageObjectLifecycleStates.Active,
        IsDeleted = false
    };
}
