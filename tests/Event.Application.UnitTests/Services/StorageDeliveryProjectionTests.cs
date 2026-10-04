using Explore.Application.Mappings;
using Explore.Application.Services;
using Explore.Domain;
using System.Text.Json;

namespace Event.Application.UnitTests.Services;

public sealed class StorageDeliveryProjectionTests
{
    private static readonly Guid ObjectId = Guid.Parse("019a0000-0000-7000-8000-000000000001");

    [Test]
    public async Task PublicImageProjectsStablePublicRouteInsteadOfStoredLocator()
    {
        var image = Object(StorageObjectVisibilities.PublicImage, StorageObjectPurposes.EventImage);
        var dto = ActorFederationMapper.ToStorageDetail(image);
        await Assert.That(dto.Uri).IsEqualTo("/api/storageobject/019a0000-0000-7000-8000-000000000001/public");
        await Assert.That(ActorFederationMapper.ToStorageListItem(image).Uri).IsEqualTo(dto.Uri);
    }

    [Test]
    public async Task PrivateContentProjectsAuthenticatedRouteInsteadOfStoredLocator()
    {
        var file = Object(StorageObjectVisibilities.PrivateOwner, StorageObjectPurposes.Attachment);
        file.ContentType = "application/pdf";
        file.Extension = "pdf";
        await Assert.That(ActorFederationMapper.ToStorageDetail(file).Uri)
            .IsEqualTo("/api/storageobject/019a0000-0000-7000-8000-000000000001/content");
    }

    [Test]
    public async Task ExternalReferenceHasNoInventedManagedDelivery()
    {
        var image = Object(StorageObjectVisibilities.PublicImage, StorageObjectPurposes.EventImage, external: true);
        await Assert.That(ActorFederationMapper.ToStorageDetail(image).Uri).IsNull();
    }

    [Test]
    public async Task InactiveObjectHasNoDeliveryProjection()
    {
        var image = Object(StorageObjectVisibilities.PublicImage, StorageObjectPurposes.EventImage);
        image.MarkQuarantined(null, "unsafe content", DateTime.UtcNow);
        await Assert.That(ActorFederationMapper.ToStorageDetail(image).Uri).IsNull();
    }

    [Test]
    public async Task PrivateOriginAndCapturedLocationsAreAbsentFromOrdinaryJson()
    {
        var file = Object(StorageObjectVisibilities.PrivateOwner, StorageObjectPurposes.Attachment);
        file.Provider = StorageProviders.S3Compatible;
        file.ProviderVersionId = "private-version";
        var detail = ActorFederationMapper.ToStorageDetail(file);
        await Assert.That(detail.SupportsPresignedDownload).IsTrue();
        foreach (var dto in new object[] { detail, ActorFederationMapper.ToStorageListItem(file) })
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            foreach (var property in new[] { "provider", "bucket", "objectKey", "storageProviderBindingId", "providerVersionId", "sourceUri", "supportsPresignedDownload" })
                await Assert.That(json.RootElement.TryGetProperty(property, out _)).IsFalse();
        }
    }

    [Test]
    public async Task ResourceBytesHaveNoGenericDeliveryProjection()
    {
        var file = Object(StorageObjectVisibilities.PrivateOwner, StorageObjectPurposes.EventResource);
        await Assert.That(ActorFederationMapper.ToStorageDetail(file).Uri).IsNull();
    }

    [Test]
    public async Task UnsafePublicImageHasNoProjection()
    {
        var file = Object(StorageObjectVisibilities.PublicImage, StorageObjectPurposes.EventImage);
        file.ContentType = "image/svg+xml";
        file.Extension = "svg";
        await Assert.That(ActorFederationMapper.ToStorageDetail(file).Uri).IsNull();
    }

    [Test]
    public async Task CrossTenantImageHasNoDisplayProjection()
    {
        var image = Object(StorageObjectVisibilities.PublicImage, StorageObjectPurposes.EventImage);
        await Assert.That(StoragePresentationUrlResolver.PublicImageUri(image, Guid.CreateVersion7())).IsNull();
        await Assert.That(StoragePresentationUrlResolver.PublicImageUri(image, image.TenantId))
            .IsEqualTo($"/api/storageobject/{image.Id}/public");
    }

    private static StorageObject Object(string visibility, string purpose, bool external = false) => new()
    {
        Id = ObjectId,
        TenantId = Guid.CreateVersion7(),
        Tenant = null!,
        FileTypeId = 1,
        FileType = null!,
        SourceUri = "https://private-provider.example/bucket/key?credential=private",
        Provider = external ? StorageProviders.LegacyExternal : StorageProviders.Local,
        StorageProviderBindingId = external ? null : Guid.CreateVersion7(),
        ObjectKey = external ? null : "private/key.png",
        FullName = "image.png",
        SafeDisplayName = "image.png",
        Extension = "png",
        ContentType = "image/png",
        Visibility = visibility,
        Purpose = purpose,
        LifecycleState = StorageObjectLifecycleStates.Active
    };
}
