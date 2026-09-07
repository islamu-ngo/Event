namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookProviderPortalAccessDto
{
    public required string Url { get; init; }

    public string? Token { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}
