using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationAnswerFile : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private RegistrationAnswerFile()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationSubmissionId { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public Guid RegistrationFormSectionId { get; private set; }
    public Guid RegistrationFormFieldId { get; private set; }
    public int FieldTypeId { get; private set; }
    public Guid StorageObjectId { get; private set; }
    public string SafeDisplayName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public string Extension { get; private set; } = string.Empty;
    public string? Sha256Checksum { get; private set; }
    public long Size { get; private set; }
    public string QuarantineState { get; private set; } = string.Empty;
    public string ScanStatus { get; private set; } = string.Empty;
    public DateTime QuarantinedAt { get; private set; }
    public DateTime? ReleasedAt { get; private set; }
    public Guid? ReleasedBy { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public bool IsReleased => QuarantineState == RegistrationAnswerFileQuarantineStates.Released;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationAnswerFile Create(
        Guid tenantId,
        Guid registrationSubmissionId,
        RegistrationFormField field,
        StorageObject storageObject,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(storageObject);
        if (tenantId == Guid.Empty || registrationSubmissionId == Guid.Empty || utcNow == default || utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Tenant, submission, and UTC creation time are required.");
        }

        if (field.TenantId != tenantId || field.FieldTypeId != (int)RegistrationFieldTypeEnum.File)
        {
            throw new ArgumentException("The registration field must be a File field in the same tenant.", nameof(field));
        }

        if (storageObject.TenantId != tenantId ||
            storageObject.LifecycleState != StorageObjectLifecycleStates.Active ||
            string.IsNullOrWhiteSpace(storageObject.ContentType) ||
            string.IsNullOrWhiteSpace(storageObject.SafeDisplayName) ||
            string.IsNullOrWhiteSpace(storageObject.Extension) ||
            storageObject.Size < 0)
        {
            throw new ArgumentException("The storage object must be active, validated, and owned by the same tenant.", nameof(storageObject));
        }

        if (storageObject.OwningResourceKind is not null || storageObject.OwningResourceId is not null)
        {
            throw new ArgumentException("The storage object is already assigned to another resource.", nameof(storageObject));
        }

        Guid id = Guid.CreateVersion7();
        storageObject.OwningResourceKind = RegistrationAnswerFileStorageOwnership.ResourceKind;
        storageObject.OwningResourceId = id;

        return new RegistrationAnswerFile
        {
            Id = id,
            TenantId = tenantId,
            EventId = field.EventId,
            RegistrationSubmissionId = registrationSubmissionId,
            RegistrationFormId = field.RegistrationFormId,
            RegistrationFormVersionId = field.RegistrationFormVersionId,
            RegistrationFormSectionId = field.RegistrationFormSectionId,
            RegistrationFormFieldId = field.Id,
            FieldTypeId = field.FieldTypeId,
            StorageObjectId = storageObject.Id,
            SafeDisplayName = storageObject.SafeDisplayName,
            ContentType = storageObject.ContentType,
            Extension = storageObject.Extension,
            Sha256Checksum = storageObject.Sha256Checksum,
            Size = storageObject.Size,
            QuarantineState = RegistrationAnswerFileQuarantineStates.Quarantined,
            ScanStatus = RegistrationAnswerFileScanStatuses.NotScanned,
            QuarantinedAt = utcNow,
            CreatedAt = utcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public RegistrationAnswerFileRelease ReleaseManually(Guid releasedBy, string reason, DateTime utcNow)
    {
        if (releasedBy == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 ||
            utcNow == default || utcNow.Kind != DateTimeKind.Utc || utcNow < QuarantinedAt)
        {
            throw new ArgumentException("A releasing operator, bounded reason, and valid UTC release time are required.");
        }

        if (IsReleased)
        {
            throw new InvalidOperationException("The registration file is already released.");
        }

        RegistrationAnswerFileRelease release = RegistrationAnswerFileRelease.Record(
            this, releasedBy, reason.Trim(), utcNow);
        QuarantineState = RegistrationAnswerFileQuarantineStates.Released;
        ReleasedBy = releasedBy;
        ReleasedAt = utcNow;
        UpdatedBy = releasedBy;
        UpdatedAt = utcNow;
        ConcurrencyStamp = Guid.CreateVersion7();
        return release;
    }
}

public static class RegistrationAnswerFileStorageOwnership
{
    public const string ResourceKind = "registration_answer_file";
}

public static class RegistrationAnswerFileQuarantineStates
{
    public const string Quarantined = "quarantined";
    public const string Released = "released";
}

public static class RegistrationAnswerFileScanStatuses
{
    public const string NotScanned = "not_scanned";
}
