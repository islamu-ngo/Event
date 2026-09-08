namespace Explore.Application.DTOs.Webhooks;

public sealed record PauseWebhookEndpointRequestDto
{
    public long ExpectedDeliveryStateVersion { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}
