namespace Explore.Application.Features.ExternalApiKeys;

/// <summary>
/// Result of checking whether the current user has authority to create an API key for a given owner type.
/// </summary>
internal readonly record struct OwnerAuthorityResult(bool IsAuthorized, Guid OwnerId, string DenialMessage, string DenialDetail)
{
    public static OwnerAuthorityResult Authorized(Guid ownerId) => new(true, ownerId, string.Empty, string.Empty);
    public static OwnerAuthorityResult Denied(string message, string detail) => new(false, Guid.Empty, message, detail);
}
