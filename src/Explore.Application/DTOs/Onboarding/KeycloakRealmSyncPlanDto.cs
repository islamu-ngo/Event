namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakRealmSyncPlanDto
{
    public string Status { get; set; } = "blocked";
    public string Message { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string Authority { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string? ApiClientId { get; init; }
    public bool DestructiveOperationsSupported { get; init; }
    public bool RequiresBackupBeforeApply { get; set; }
    public KeycloakRealmDesiredStateDto DesiredState { get; set; } = new();
    public IReadOnlyList<KeycloakRealmSyncOperationDto> Operations { get; set; } = [];
    public IReadOnlyList<KeycloakRealmDoctorCheckDto> Diagnostics { get; set; } = [];
}
