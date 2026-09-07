namespace Explore.Application.DTOs.Webhooks;

public sealed record RedriveIncomingWebhookRequestDto
{
    public int ExpectedProcessingGeneration { get; init; }

    public string Reason { get; init; } = string.Empty;
}
