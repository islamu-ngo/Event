namespace Explore.Application.Settings;

/// <summary>
/// Explicit context for typed settings document resolution.
/// </summary>
/// <param name="TenantId">Tenant to resolve for. Required for tenant-owned documents.</param>
/// <param name="UserId">Current user, when resolution depends on user preferences or membership.</param>
/// <param name="OrganizationId">Current organization scope, when known.</param>
/// <param name="GroupId">Current group scope, when known.</param>
/// <param name="RouteGroup">Route-derived group slug or identifier, when the route chooses the group context.</param>
/// <param name="RequestedDocuments">Document keys the caller intends to resolve.</param>
public sealed record SettingsResolutionContext(
    Guid TenantId,
    Guid? UserId = null,
    Guid? OrganizationId = null,
    Guid? GroupId = null,
    string? RouteGroup = null,
    IReadOnlyCollection<string>? RequestedDocuments = null)
{
    public bool RequestsDocument(string documentKey)
    {
        if (RequestedDocuments is null || RequestedDocuments.Count == 0)
        {
            return true;
        }

        return RequestedDocuments.Contains(documentKey, StringComparer.Ordinal);
    }
}
