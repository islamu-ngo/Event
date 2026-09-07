namespace Explore.Application.DTOs.Webhooks;

public sealed record RepairWebhookProviderBindingRequestDto
{
    public string ExternalApplicationId { get; init; } = string.Empty;

    public string ReasonCode { get; init; } = string.Empty;
}
