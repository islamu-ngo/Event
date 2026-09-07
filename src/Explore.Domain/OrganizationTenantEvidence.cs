using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class OrganizationTenantEvidence : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid OrganizationTenantId { get; private set; }
    public OrganizationTenant? OrganizationTenant { get; private set; }
    public Guid DocumentStorageObjectId { get; private set; }
    public StorageObject? DocumentStorageObject { get; private set; }
    public int ReviewStatusId { get; private set; }
    public ApprovalStatus? ReviewStatus { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public User? ReviewedByUser { get; private set; }
    public string? ReviewNotes { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static OrganizationTenantEvidence CreatePending(
        OrganizationTenant organizationTenant,
        StorageObject documentStorageObject)
    {
        ArgumentNullException.ThrowIfNull(organizationTenant);
        ArgumentNullException.ThrowIfNull(documentStorageObject);
        EnsureIdentifier(organizationTenant.TenantId, nameof(organizationTenant));
        EnsureIdentifier(organizationTenant.Id, nameof(organizationTenant));
        EnsureIdentifier(documentStorageObject.Id, nameof(documentStorageObject));
        if (documentStorageObject.TenantId != organizationTenant.TenantId)
        {
            throw new InvalidOperationException("Legitimacy evidence document and Organization participation must belong to the same tenant.");
        }

        return new OrganizationTenantEvidence
        {
            Id = Guid.CreateVersion7(),
            TenantId = organizationTenant.TenantId,
            OrganizationTenantId = organizationTenant.Id,
            OrganizationTenant = organizationTenant,
            DocumentStorageObjectId = documentStorageObject.Id,
            DocumentStorageObject = documentStorageObject,
            ReviewStatusId = (int)ApprovalStatusEnum.Pending
        };
    }

    public void Review(bool approved, Guid reviewerUserId, string? notes, DateTime reviewedAt)
    {
        EnsureIdentifier(reviewerUserId, nameof(reviewerUserId));
        if (ReviewStatusId != (int)ApprovalStatusEnum.Pending)
        {
            throw new InvalidOperationException("Organization legitimacy evidence is no longer pending review.");
        }

        if (reviewedAt == default || reviewedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Review timestamp must be a non-default UTC value.", nameof(reviewedAt));
        }

        ReviewStatusId = approved
            ? (int)ApprovalStatusEnum.Approved
            : (int)ApprovalStatusEnum.Rejected;
        ReviewedByUserId = reviewerUserId;
        ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ReviewedAt = reviewedAt;
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }
}
