namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakClientDesiredStateDto
{
    public string ClientId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ClientKind { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public string Protocol { get; init; } = "openid-connect";
    public bool PublicClient { get; init; }
    public bool BearerOnly { get; init; }
    public bool StandardFlowEnabled { get; init; }
    public bool DirectAccessGrantsEnabled { get; init; }
    public bool ServiceAccountsEnabled { get; init; }
    public IReadOnlyList<string> RedirectUris { get; init; } = [];
    public IReadOnlyList<string> WebOrigins { get; init; } = [];
    public IReadOnlyList<string> OptionalClientScopes { get; init; } = [];
    public IReadOnlyList<string> DefaultClientScopes { get; init; } = [];
    public IReadOnlyList<string> RealmRoleMappings { get; init; } = [];
    public IReadOnlyList<KeycloakProtocolMapperDesiredStateDto> ProtocolMappers { get; init; } = [];
}
