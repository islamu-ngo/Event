using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationAnswerFileTests
{
    [Test]
    public async Task Create_SnapshotsValidatedStorageMetadataAndStartsQuarantined()
    {
        Guid tenantId = Guid.CreateVersion7();
        DateTime now = new(2026, 8, 2, 20, 0, 0, DateTimeKind.Utc);
        StorageObject storageObject = CreateStorageObject(tenantId);

        RegistrationAnswerFile file = RegistrationAnswerFile.Create(
            tenantId,
            Guid.CreateVersion7(),
            CreateFileField(tenantId),
            storageObject,
            now);

        await Assert.That(file.StorageObjectId).IsEqualTo(storageObject.Id);
        await Assert.That(file.ContentType).IsEqualTo(storageObject.ContentType);
        await Assert.That(file.Size).IsEqualTo(storageObject.Size);
        await Assert.That(file.QuarantineState).IsEqualTo(RegistrationAnswerFileQuarantineStates.Quarantined);
        await Assert.That(file.ScanStatus).IsEqualTo(RegistrationAnswerFileScanStatuses.NotScanned);
        await Assert.That(file.IsReleased).IsFalse();
    }

    [Test]
    public async Task ReleaseManually_RequiresOperatorAndRecordsExplicitRelease()
    {
        Guid tenantId = Guid.CreateVersion7();
        DateTime now = new(2026, 8, 2, 20, 0, 0, DateTimeKind.Utc);
        RegistrationAnswerFile file = RegistrationAnswerFile.Create(
            tenantId,
            Guid.CreateVersion7(),
            CreateFileField(tenantId),
            CreateStorageObject(tenantId),
            now);
        Guid operatorId = Guid.CreateVersion7();

        RegistrationAnswerFileRelease release = file.ReleaseManually(
            operatorId,
            "Verified by tenant administrator",
            now.AddMinutes(5));

        await Assert.That(file.IsReleased).IsTrue();
        await Assert.That(file.ReleasedBy).IsEqualTo(operatorId);
        await Assert.That(file.ReleasedAt).IsEqualTo(now.AddMinutes(5));
        await Assert.That(file.ScanStatus).IsEqualTo(RegistrationAnswerFileScanStatuses.NotScanned);
        await Assert.That(release.RegistrationAnswerFileId).IsEqualTo(file.Id);
        await Assert.That(release.ReleasedBy).IsEqualTo(operatorId);
        await Assert.That(release.Reason).IsEqualTo("Verified by tenant administrator");
        await Assert.That(release.PreviousQuarantineState).IsEqualTo(RegistrationAnswerFileQuarantineStates.Quarantined);
        await Assert.That(release.NewQuarantineState).IsEqualTo(RegistrationAnswerFileQuarantineStates.Released);
    }

    [Test]
    public async Task ReleaseManually_WhenAlreadyReleased_PreservesFirstReleaseAudit()
    {
        Guid tenantId = Guid.CreateVersion7();
        DateTime now = new(2026, 8, 2, 20, 0, 0, DateTimeKind.Utc);
        RegistrationAnswerFile file = RegistrationAnswerFile.Create(
            tenantId,
            Guid.CreateVersion7(),
            CreateFileField(tenantId),
            CreateStorageObject(tenantId),
            now);
        Guid firstOperator = Guid.CreateVersion7();
        file.ReleaseManually(firstOperator, "First review", now.AddMinutes(5));

        void Act() => file.ReleaseManually(Guid.CreateVersion7(), "Retry", now.AddMinutes(10));

        await Assert.That(Act).Throws<InvalidOperationException>();
        await Assert.That(file.ReleasedBy).IsEqualTo(firstOperator);
        await Assert.That(file.ReleasedAt).IsEqualTo(now.AddMinutes(5));
    }

    [Test]
    public async Task Create_RejectsCrossTenantStorage()
    {
        Guid tenantId = Guid.CreateVersion7();

        void Act() => RegistrationAnswerFile.Create(
            tenantId,
            Guid.CreateVersion7(),
            CreateFileField(tenantId),
            CreateStorageObject(Guid.CreateVersion7()),
            DomainTestClock.UtcNow);

        await Assert.That(Act).Throws<ArgumentException>();
    }

    private static RegistrationFormField CreateFileField(Guid tenantId)
    {
        var form = RegistrationForm.Create(tenantId, Guid.CreateVersion7(), "native", "files", "Files", DomainTestClock.UtcNow);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, DomainTestClock.UtcNow);
        var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Documents", DomainTestClock.UtcNow);
        return RegistrationFormField.Create(
            Guid.CreateVersion7(), section, 1, "native", "document", "Document",
            RegistrationFieldTypeEnum.File, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, false, DomainTestClock.UtcNow);
    }

    private static StorageObject CreateStorageObject(Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        FileTypeId = 1,
        FileType = null!,
        Tenant = null!,
        Uri = "/api/storageobject/file/content",
        ObjectKey = "tenants/test/file.pdf",
        Provider = StorageProviders.Local,
        FullName = "document.pdf",
        SafeDisplayName = "document.pdf",
        Extension = "pdf",
        ContentType = "application/pdf",
        Sha256Checksum = new string('a', 64),
        Size = 4,
        Visibility = StorageObjectVisibilities.PrivateOwner,
        Purpose = StorageObjectPurposes.Document,
        LifecycleState = StorageObjectLifecycleStates.Active
    };
}
