using Explore.Application.Notifications;

namespace Explore.Infrastructure.Services.Keycloak;

public sealed class KeycloakLifecycleEmailOptions
{
    public const string SectionName = "KeycloakLifecycleEmail";

    public bool Enabled { get; set; }
    public bool AllowLocalUrls { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string AdminUsername { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string AdminClientId { get; set; } = "admin-cli";
    public string? DefaultClientId { get; set; }
    public int? DefaultLifespanSeconds { get; set; }
    public AccountAuthorityKind AccountAuthorityKind { get; set; } = AccountAuthorityKind.Keycloak;
}
