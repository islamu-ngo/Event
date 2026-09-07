namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakClientSecretRotationResultDto
{
    public string Status { get; set; } = "blocked";
    public string Message { get; set; } = "Keycloak client secret rotation is blocked.";
    public string ClientId { get; init; } = string.Empty;
    public string SecretOwnershipMode { get; init; } = "application-managed";
    public bool AuthSchemesReloaded { get; set; }
    public bool RequiresRestart { get; init; }
    public string OperatorInstructions { get; set; } = string.Empty;
    public IReadOnlyList<KeycloakRealmSyncOperationDto> Operations { get; set; } = [];
}
