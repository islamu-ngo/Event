namespace Explore.Application.DTOs.Webhooks;

public sealed record AbandonWebhookProviderPublicationRequestDto
{
    public long ExpectedConcurrencyVersion { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}
