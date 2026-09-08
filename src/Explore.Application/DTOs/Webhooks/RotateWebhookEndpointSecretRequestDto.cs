namespace Explore.Application.DTOs.Webhooks;

public sealed record RotateWebhookEndpointSecretRequestDto
{
    public required string NewSecretRef { get; init; }

    public int? PreviousSecretValidForSeconds { get; init; }

    public int ExpectedConfigurationVersion { get; init; }

    public int PendingWorkDecisionId { get; init; }

    public required string PendingWorkReason { get; init; }

    public bool AcknowledgeUncertainProviderPublications { get; init; }
}
