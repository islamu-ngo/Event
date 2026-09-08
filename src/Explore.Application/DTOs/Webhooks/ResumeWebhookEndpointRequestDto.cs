namespace Explore.Application.DTOs.Webhooks;

public sealed record ResumeWebhookEndpointRequestDto
{
    public long ExpectedDeliveryStateVersion { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}
