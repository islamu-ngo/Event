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
