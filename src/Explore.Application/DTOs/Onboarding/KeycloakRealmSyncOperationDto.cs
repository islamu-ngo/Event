namespace Explore.Application.DTOs.Onboarding;

public sealed record KeycloakRealmSyncOperationDto
{
    public string OperationId { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> Changes { get; init; } = [];
    public bool RequiresBackupBeforeApply { get; init; }
}
