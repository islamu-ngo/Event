namespace Explore.Application.DTOs.Webhooks;

public sealed record OpenSvixAppPortalRequestDto
{
    public Guid ConsumerId { get; init; }

    public int? ExpiresInSeconds { get; init; }
}
