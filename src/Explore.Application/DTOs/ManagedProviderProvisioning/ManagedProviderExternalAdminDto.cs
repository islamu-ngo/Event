// ABOUTME: External identity payload for the tenant administrator created by provider provisioning.
// ABOUTME: Uses stable IdP issuer/provider and subject values instead of mutable email as authority.

using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.ManagedProviderProvisioning;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagedProviderExternalAdminDto
{
    public required string IdentityProvider { get; init; }
    public required string Subject { get; init; }
    public required string Email { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public bool EmailVerified { get; init; }
    public string? DisplayName { get; init; }
}
