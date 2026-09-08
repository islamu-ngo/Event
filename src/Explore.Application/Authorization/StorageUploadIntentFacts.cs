using Explore.Domain;

namespace Explore.Application.Authorization;

public sealed record StorageUploadIntentFacts(
    Guid SubjectUserId,
    Guid TenantId,
    string OwningResourceKind,
    Guid OwningResourceId,
    Guid? OwningOrganizationId) : IAuthorizationFacts
{
    public bool IsOrganizationTenantUpload =>
        string.Equals(OwningResourceKind, StorageOwningResourceKinds.OrganizationTenant, StringComparison.Ordinal);
}
