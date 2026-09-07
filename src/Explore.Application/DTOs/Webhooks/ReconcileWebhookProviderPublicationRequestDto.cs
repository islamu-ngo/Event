namespace Explore.Application.DTOs.Webhooks;

public sealed record ReconcileWebhookProviderPublicationRequestDto
{
    public long ExpectedConcurrencyVersion { get; init; }
    public string ExternalProviderMessageId { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
}
