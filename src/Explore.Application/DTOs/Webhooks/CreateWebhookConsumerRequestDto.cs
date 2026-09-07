namespace Explore.Application.DTOs.Webhooks;

public sealed record CreateWebhookConsumerRequestDto
{
    public Guid? OwnerId { get; init; }

    public int ConsumerKindId { get; init; }

    public string Name { get; init; } = string.Empty;

    public int ProviderModeId { get; init; }
}
