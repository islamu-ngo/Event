using Explore.Application.Constants;
using Microsoft.AspNetCore.Authentication;

namespace Explore.API.Authentication;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SectionName = "Authentication:ApiKeys";

    public string HeaderName { get; set; } = ApiAuthenticationHeaderNames.ApiKey;

    public List<ApiKeyClientDescriptor> Clients { get; set; } = [];
}

public sealed class ApiKeyClientDescriptor
{
    public string KeyId { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public string OwnerType { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = [];

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public string SecretHash { get; set; } = string.Empty;
}
