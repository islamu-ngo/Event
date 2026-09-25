namespace Event.Domain.UnitTests.Entities;

public sealed class EventResourceUploadTests
{
    [Test]
    public async Task ResourceReservationCannotRebindItsExpectedVersion()
    {
        var session = Session();
        var version = Guid.CreateVersion7();
        session.BindEventResourceVersion(version);
        await Assert.That(() => session.BindEventResourceVersion(Guid.CreateVersion7())).Throws<InvalidOperationException>();
        await Assert.That(session.ExpectedResourceVersion).IsEqualTo(version);
    }

    [Test]
    public async Task AcceptedInspectionIsUnscannedAndBoundToExactObjectAndHash()
    {
        var value = Object();
        await Assert.That(value.DocumentSafetyState).IsEqualTo(StorageDocumentSafetyStates.Unavailable);
        value.RecordEventResourceInspection(value.Id, new string('a', 64));
        await Assert.That(value.DocumentSafetyState).IsEqualTo(StorageDocumentSafetyStates.Unscanned);
        await Assert.That(value.HasBoundDocumentInspection).IsTrue();
        value.Sha256Checksum = new string('b', 64);
        await Assert.That(value.HasBoundDocumentInspection).IsFalse();
        await Assert.That(() => value.RecordEventResourceInspection(value.Id, new string('b', 64))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InspectionCannotAcceptDifferentObjectIdentity()
    {
        var value = Object();
        await Assert.That(() => value.RecordEventResourceInspection(Guid.CreateVersion7(), new string('a', 64)))
            .Throws<InvalidOperationException>();
        await Assert.That(value.DocumentSafetyState).IsEqualTo(StorageDocumentSafetyStates.Unavailable);
    }

    [Test]
    public async Task LateProducerSettlementCannotReactivateACanceledUpload()
    {
        var session = Session();
        var now = new DateTime(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Guid objectId = Guid.CreateVersion7(), bindingId = Guid.CreateVersion7();
        string key = $"objects/{objectId:N}";
        session.BindEventResourceVersion(Guid.CreateVersion7());
        session.StorageProviderBindingId = bindingId;
        session.ReserveObjectKey(key);
        session.MarkUploading(now);
        session.StageEventResourceObject(objectId);
        session.Cancel(now);
        await Assert.That(session.ProducerSettled).IsFalse();
        await Assert.That(() => session.RecordProducerSettlement(Guid.CreateVersion7(), bindingId, key, null))
            .Throws<InvalidOperationException>();
        await Assert.That(() => session.RecordProducerSettlement(objectId, Guid.CreateVersion7(), key, null))
            .Throws<InvalidOperationException>();
        session.RecordProducerSettlement(objectId, bindingId, key, null);
        await Assert.That(session.ProducerSettled).IsTrue();
        await Assert.That(session.Status).IsEqualTo(StorageUploadSessionStates.Canceled);
        await Assert.That(session.FinalizedResourceVersion).IsNull();
    }

    [Test]
    public async Task ResourceStageAndFinalizationRequireBoundSettledProducerIdentity()
    {
        var session = Session();
        var now = new DateTime(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var objectId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        const string key = "objects/resource.pdf";
        session.BindEventResourceVersion(Guid.CreateVersion7());
        session.ReserveObjectKey(key);
        session.MarkUploading(now);
        await Assert.That(() => session.StageEventResourceObject(objectId)).Throws<InvalidOperationException>();
        session.StorageProviderBindingId = bindingId;
        session.StageEventResourceObject(objectId);
        await Assert.That(() => session.Finalize(objectId, key, null, now)).Throws<InvalidOperationException>();
        session.RecordProducerSettlement(objectId, bindingId, key, "version-one");
        await Assert.That(() => session.RecordProducerSettlement(objectId, bindingId, key, "version-two"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => session.Finalize(Guid.CreateVersion7(), key, null, now)).Throws<InvalidOperationException>();
        await Assert.That(() => session.Finalize(objectId, "objects/other.pdf", null, now)).Throws<InvalidOperationException>();
        session.Finalize(objectId, key, null, now);
        await Assert.That(session.ProviderVersionId).IsEqualTo("version-one");
        await Assert.That(session.Status).IsEqualTo(StorageUploadSessionStates.Finalized);
    }

    [Test]
    public async Task ExpiryAndFailureNeverProveProducerSettlement()
    {
        var session = Session();
        var now = new DateTime(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        session.BindEventResourceVersion(Guid.CreateVersion7());
        session.StorageProviderBindingId = Guid.CreateVersion7();
        session.ReserveObjectKey("objects/pending.pdf");
        session.MarkUploading(now);
        session.StageEventResourceObject(Guid.CreateVersion7());
        session.MarkExpired(now.AddYears(1));
        await Assert.That(session.ProducerSettled).IsFalse();
        session.Fail("ambiguous_write", null, now.AddYears(2));
        await Assert.That(session.ProducerSettled).IsFalse();
    }

    private static StorageUploadSession Session() => new()
    {
        TenantId = Guid.CreateVersion7(), UserId = Guid.CreateVersion7(), Provider = StorageProviders.Local,
        Purpose = StorageObjectPurposes.EventResource, Visibility = StorageObjectVisibilities.PrivateOwner,
        OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = Guid.CreateVersion7(),
        ExpectedSizeBytes = 12, ReservedBytes = 12, ContentType = "application/pdf", SafeDisplayName = "file.pdf",
        Status = StorageUploadSessionStates.Reserved
    };

    private static StorageObject Object() => new()
    {
        Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), Tenant = null!, FileType = null!,
        Provider = StorageProviders.Local, Uri = "/private", FullName = "file.pdf", SafeDisplayName = "file.pdf",
        Extension = "pdf", Purpose = StorageObjectPurposes.EventResource, Visibility = StorageObjectVisibilities.PrivateOwner,
        OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = Guid.CreateVersion7(),
        LifecycleState = StorageObjectLifecycleStates.DeleteRequested, Sha256Checksum = new string('a', 64)
    };
}
