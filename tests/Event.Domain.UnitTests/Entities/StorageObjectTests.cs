namespace Event.Domain.UnitTests.Entities;

using Explore.Domain;
using Explore.Domain.Interfaces;

public class StorageObjectTests
{
    [Test]
    public async Task StorageObject_ImplementsTenantAuditSoftDeleteAndConcurrencyContracts()
    {
        var interfaces = typeof(StorageObject).GetInterfaces();

        await Assert.That(interfaces).Contains(typeof(ITenantEntity));
        await Assert.That(interfaces).Contains(typeof(IAuditableEntity));
        await Assert.That(interfaces).Contains(typeof(ISoftDeletable));
        await Assert.That(interfaces).Contains(typeof(IConcurrencyAware));
    }

    [Test]
    public async Task MarkQuarantined_SetsLifecycleAndAuditMetadata()
    {
        var entity = CreateStorageObject();
        var userId = Guid.CreateVersion7();
        var utcNow = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc);

        entity.MarkQuarantined(userId, "malware_scan_failed", utcNow);

        await Assert.That(entity.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Quarantined);
        await Assert.That(entity.QuarantinedBy).IsEqualTo(userId);
        await Assert.That(entity.QuarantinedAt).IsEqualTo(utcNow);
        await Assert.That(entity.QuarantineReason).IsEqualTo("malware_scan_failed");
    }

    [Test]
    public async Task MarkQuarantined_WithoutReason_ThrowsArgumentException()
    {
        var entity = CreateStorageObject();

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            entity.MarkQuarantined(null, " ", DomainTestClock.UtcNow);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task RequestDelete_WhenActive_MarksDeleteRequested()
    {
        var entity = CreateStorageObject();

        entity.RequestDelete();

        await Assert.That(entity.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
    }

    [Test]
    public async Task MarkDeleted_SetsLifecycleAndSoftDeleteMetadata()
    {
        var entity = CreateStorageObject();
        var userId = Guid.CreateVersion7();
        var utcNow = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);

        entity.MarkDeleted(userId, utcNow);

        await Assert.That(entity.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Deleted);
        await Assert.That(entity.IsDeleted).IsTrue();
        await Assert.That(entity.DeletedAt).IsEqualTo(utcNow);
        await Assert.That(entity.DeletedBy).IsEqualTo(userId);
    }

    private static StorageObject CreateStorageObject()
    {
        return new StorageObject
        {
            Uri = "/storage/local/test-file.png",
            ObjectKey = "tenants/default/test-file.png",
            Provider = StorageProviders.Local,
            FullName = "test-file.png",
            SafeDisplayName = "test-file.png",
            Extension = ".png",
            ContentType = "image/png",
            Size = 1024,
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            Purpose = StorageObjectPurposes.Attachment,
            LifecycleState = StorageObjectLifecycleStates.Active,
            TenantId = Guid.CreateVersion7(),
            Tenant = new Tenant
            {
                FullName = "Default Tenant",
                Slug = "default",
                TenantStatus = new TenantStatus
                {
                    MasterCode = "ACTIVE",
                    FullName = "Active"
                }
            },
            FileTypeId = 1,
            FileType = new FileType
            {
                Id = 1,
                MasterCode = "IMAGE",
                FullName = "Image"
            }
        };
    }
}
