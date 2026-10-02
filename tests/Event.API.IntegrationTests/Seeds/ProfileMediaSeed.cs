using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;

namespace Event.Api.IntegrationTests.Seeds;

internal static class ProfileMediaSeed
{
    public static StorageObject Add(ExploreDbContext database, Guid id, Guid tenantId, Guid? createdBy = null)
    {
        var binding = CapturedStorageProviders.LocalBinding();
        database.Add(binding);
        var image = new StorageObject
        {
            Id = id,
            TenantId = tenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Provider = StorageProviders.Local,
            StorageProviderBindingId = binding.Id,
            ObjectKey = $"private-profile/{id:N}",
            SourceUri = "https://provider.example.test/private-bucket/raw-key",
            FullName = "profile.png",
            SafeDisplayName = "profile.png",
            ContentType = "image/png",
            Extension = "png",
            Size = 1,
            Purpose = StorageObjectPurposes.ProfileImage,
            Visibility = StorageObjectVisibilities.PublicImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            CreatedBy = createdBy
        };
        database.StorageObjects.Add(image);
        return image;
    }
}
