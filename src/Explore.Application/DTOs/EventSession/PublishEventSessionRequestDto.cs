namespace Explore.Application.DTOs.EventSession;

public sealed record PublishEventSessionRequestDto
{
    public Guid ExpectedConcurrencyStamp { get; init; }
}
