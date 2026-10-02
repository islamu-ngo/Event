using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

public sealed class StorageRetirementAdmissionTests
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ActiveMetadataCannotSettleAnUnacknowledgedProducer()
    {
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var source = Source(binding.Id);
        var operation = StorageProducerOperation.Create(source.Id, source.TenantId, binding, source.ObjectKey!, Now);
        var work = StorageRetirementTarget.Capture(source, [], operation, Now);

        await Assert.That(work.State).IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
        await Assert.That(work.TryClaim(work.ConcurrencyStamp, Guid.CreateVersion7(), Now.AddYears(50),
            Now.AddYears(50).AddMinutes(1))).IsFalse();
        await Assert.That(work.TrySettleProducer("acknowledged-version", Now.AddYears(50))).IsTrue();
        await Assert.That(work.ProviderObjectVersion).IsEqualTo("acknowledged-version");
    }

    [Test]
    public async Task ActiveMetadataWithoutCustodyTransfersTheCapturedVersion()
    {
        var source = Source(Guid.CreateVersion7());
        source.ProviderVersionId = "version-1";
        var work = StorageRetirementTarget.Capture(source, [], null, Now);
        await Assert.That(work.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(work.ProviderBindingId).IsEqualTo(source.StorageProviderBindingId!.Value);
        await Assert.That(work.ProviderObjectVersion).IsEqualTo("version-1");
        await Assert.That(source.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("provider")]
    [Arguments("binding")]
    [Arguments("key")]
    [Arguments("version")]
    public async Task DisagreeingCustodyCannotAuthorizeAnotherTarget(string mismatch)
    {
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var source = Source(binding.Id);
        var session = new StorageUploadSession
        {
            Id = source.Id, TenantId = source.TenantId, StorageObjectId = source.Id,
            Provider = source.Provider, StorageProviderBindingId = mismatch == "binding" ? Guid.CreateVersion7() : binding.Id,
            ObjectKey = mismatch == "key" ? "images/another.png" : source.ObjectKey,
            ProviderVersionId = mismatch == "version" ? "different-version" : null,
            Purpose = source.Purpose, Visibility = source.Visibility, ContentType = "image/png",
            SafeDisplayName = "image.png", Status = StorageUploadSessionStates.Uploading
        };
        if (mismatch == "tenant") session.TenantId = Guid.CreateVersion7();
        if (mismatch == "provider") session.Provider = StorageProviders.S3Compatible;
        source.ProviderVersionId = "known-version";
        if (mismatch != "version") session.ProviderVersionId = "known-version";
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            StorageRetirementTarget.Capture(source, [session], null, Now);
            return Task.CompletedTask;
        });
    }

    private static StorageObject Source(Guid bindingId) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), Tenant = null!, FileType = null!,
        Provider = StorageProviders.Local, StorageProviderBindingId = bindingId, ObjectKey = "images/image.png",
        FullName = "image.png", SafeDisplayName = "image.png", Extension = "png",
        Purpose = StorageObjectPurposes.ProfileImage, Visibility = StorageObjectVisibilities.PublicImage,
        LifecycleState = StorageObjectLifecycleStates.Active
    };
}
