namespace Event.Domain.UnitTests.Entities;

using Explore.Domain;

public class StorageUploadSessionTests
{
    [Test]
    public async Task ReserveObjectKey_WhenReserved_PersistsCleanupIdentityBeforeUpload()
    {
        var session = CreateSession();

        session.ReserveObjectKey("tenants/default/uploads/reserved.png");

        await Assert.That(session.ObjectKey).IsEqualTo("tenants/default/uploads/reserved.png");
        await Assert.That(session.Status).IsEqualTo(StorageUploadSessionStates.Reserved);
    }

    [Test]
    public async Task MarkUploading_WhenReserved_MovesSessionToUploading()
    {
        var session = CreateSession();
        var utcNow = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc);

        session.ReserveObjectKey("tenants/default/uploads/reserved.png");
        session.MarkUploading(utcNow);

        await Assert.That(session.Status).IsEqualTo(StorageUploadSessionStates.Uploading);
        await Assert.That(session.UploadStartedAt).IsEqualTo(utcNow);
    }

    [Test]
    public async Task Finalize_WhenUploading_StoresObjectKeyChecksumAndFinalizedAt()
    {
        var session = CreateSession();
        var utcNow = new DateTime(2026, 5, 29, 12, 1, 0, DateTimeKind.Utc);
        var objectId = session.Id;

        session.ReserveObjectKey("tenants/default/final.png");
        session.MarkUploading(utcNow.AddMinutes(-1));
        session.RecordProducerSettlement(objectId, session.StorageProviderBindingId!.Value,
            session.ObjectKey!, null);
        session.Finalize(objectId, "tenants/default/final.png", new string('a', 64), utcNow);

        await Assert.That(session.Status).IsEqualTo(StorageUploadSessionStates.Finalized);
        await Assert.That(session.StorageObjectId).IsEqualTo(objectId);
        await Assert.That(session.ObjectKey).IsEqualTo("tenants/default/final.png");
        await Assert.That(session.FinalizedAt).IsEqualTo(utcNow);
    }

    [Test]
    public async Task Cancel_WhenFinalized_ThrowsInvalidOperationException()
    {
        var session = CreateSession();
        session.ReserveObjectKey("tenants/default/final.png");
        session.MarkUploading(DomainTestClock.UtcNow);
        session.RecordProducerSettlement(session.Id, session.StorageProviderBindingId!.Value,
            session.ObjectKey!, null);
        session.Finalize(session.Id, session.ObjectKey!, null, DomainTestClock.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            session.Cancel(DomainTestClock.UtcNow);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task MarkExpired_WhenReserved_SetsExpiredFailureCode()
    {
        var session = CreateSession();
        var utcNow = DomainTestClock.UtcNow;

        session.MarkExpired(utcNow);

        await Assert.That(session.Status).IsEqualTo(StorageUploadSessionStates.Expired);
        await Assert.That(session.FailureCode).IsEqualTo("upload_session_expired");
        await Assert.That(session.FailedAt).IsEqualTo(utcNow);
    }

    private static StorageUploadSession CreateSession()
    {
        return new StorageUploadSession
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            Provider = StorageProviders.Local,
            StorageProviderBindingId = StorageProviderBinding.Local(Path.GetTempPath()).Id,
            ExpectedSizeBytes = 1024,
            ReservedBytes = 1024,
            ContentType = "image/png",
            OriginalFileName = "upload.png",
            SafeDisplayName = "upload.png",
            Extension = ".png",
            Purpose = StorageObjectPurposes.Attachment,
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            Status = StorageUploadSessionStates.Reserved,
            ExpiresAt = DomainTestClock.UtcNow.AddMinutes(15)
        };
    }
}
