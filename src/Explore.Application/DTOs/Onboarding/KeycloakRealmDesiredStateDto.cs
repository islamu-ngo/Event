namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakRealmDesiredStateDto
{
    public string Realm { get; init; } = string.Empty;
    public string BlazorClientId { get; init; } = string.Empty;
    public string? ApiClientId { get; init; }
    public bool DestructiveOperationsSupported { get; init; }
    public IReadOnlyList<string> RequiredRealmRoles { get; set; } = [];
    public IReadOnlyList<KeycloakRoleCompositeDesiredStateDto> RoleComposites { get; set; } = [];
    public IReadOnlyList<KeycloakClientScopeDesiredStateDto> ClientScopes { get; set; } = [];
    public IReadOnlyList<KeycloakClientDesiredStateDto> Clients { get; set; } = [];
}
