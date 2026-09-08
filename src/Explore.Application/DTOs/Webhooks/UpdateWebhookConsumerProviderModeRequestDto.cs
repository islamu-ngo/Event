namespace Explore.Application.DTOs.Webhooks;

public sealed record UpdateWebhookConsumerProviderModeRequestDto
{
    public UpdateWebhookConsumerProviderModeDto? ProviderMode { get; init; }
}

public sealed record UpdateWebhookConsumerProviderModeDto
{
    public int ProviderModeId { get; init; }

    public int ExpectedConfigurationVersion { get; init; }

    public int PendingWorkDecisionId { get; init; }

    public required string PendingWorkReason { get; init; }

    public bool AcknowledgeUncertainProviderPublications { get; init; }
}
