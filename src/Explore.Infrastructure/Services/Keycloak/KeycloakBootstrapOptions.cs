namespace Explore.Infrastructure.Services.Keycloak;

public sealed class KeycloakBootstrapOptions
{
    public const string SectionName = "KeycloakBootstrap";

    public bool AllowLocalUrls { get; set; }
}
