using Explore.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class ActorProfileMediaTests
{
    [Test]
    public async Task ReplacementAndClearNeverRetainBothOwnershipKinds()
    {
        var pii = new ActorPii { DisplayName = "Profile" };
        Guid imageId = Guid.CreateVersion7();
        string external = $"https://foreign.example.test/api/storageobject/{imageId}/public";
        pii.SetProfilePicture(null, external);
        await Assert.That(pii.ProfilePictureStorageObjectId).IsNull();
        await Assert.That(pii.ExternalProfilePictureUri).IsEqualTo(external);

        pii.SetProfilePicture(imageId, null);
        await Assert.That(pii.ProfilePictureStorageObjectId).IsEqualTo(imageId);
        await Assert.That(pii.ExternalProfilePictureUri).IsNull();
        await Assert.That(() => pii.ExternalProfilePictureUri = external).Throws<ArgumentException>();
        await Assert.That(pii.ProfilePictureStorageObjectId).IsEqualTo(imageId);

        pii.SetProfilePicture(null, external);
        await Assert.That(pii.ProfilePictureStorageObjectId).IsNull();
        pii.SetProfilePicture(null, null);
        await Assert.That(pii.ExternalProfilePictureUri).IsNull();
        await Assert.That(pii.ProfilePicture).IsNull();
    }

    [Test]
    [Arguments("")]
    [Arguments("private/provider-key")]
    [Arguments("/api/storageobject/01900000-0000-7000-8000-000000000001/public")]
    [Arguments("javascript:alert(1)")]
    [Arguments("data:image/png;base64,AA==")]
    [Arguments("https://user:secret@example.test/image.png")]
    public async Task InvalidExternalReferenceCannotReplaceTheExistingImage(string uri)
    {
        var pii = new ActorPii
        {
            DisplayName = "Profile",
            ExternalProfilePictureUri = "https://foreign.example.test/profile.png"
        };
        await Assert.That(() => pii.SetProfilePicture(null, uri)).Throws<ArgumentException>();
        await Assert.That(pii.ExternalProfilePictureUri).IsEqualTo("https://foreign.example.test/profile.png");
        await Assert.That(() => pii.SetProfilePicture(Guid.Empty, null)).Throws<ArgumentException>();
    }
}
