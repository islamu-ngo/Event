using Explore.Domain;

namespace Explore.Application.Authorization;

/// <summary>Persisted reservation authority; lifecycle and content checks remain owned by finalization.</summary>
public sealed record StorageUploadFinalizationFacts(
    Guid UploadSessionId,
    Guid TenantId,
    Guid OwnerUserId,
    string Purpose,
    string Visibility,
    string? OwningResourceKind,
    Guid? OwningResourceId,
    string ContentType,
    string? Extension,
    long ExpectedSizeBytes) : IAuthorizationFacts
{
    public bool IsOrganizationEvidence =>
        OwningResourceKind == StorageOwningResourceKinds.OrganizationTenant
        && OwningResourceId is { } ownerId && ownerId != Guid.Empty
        && Purpose == StorageObjectPurposes.Document
        && Visibility == StorageObjectVisibilities.PrivateOwner
        && ContentType == "application/pdf"
        && Extension == "pdf"
        && ExpectedSizeBytes > 0;

    public bool MatchesReservationOwner(string resourceId, Guid? userId, Guid tenantId) =>
        UploadSessionId != Guid.Empty
        && Guid.TryParse(resourceId, out var sessionId) && sessionId == UploadSessionId
        && OwnerUserId != Guid.Empty && OwnerUserId == userId
        && TenantId != Guid.Empty && TenantId == tenantId
        && IsOrganizationEvidence;
}
