namespace Explore.Application.DTOs.Event;

public sealed record CancelEventRequestDto
{
    public Guid ExpectedConcurrencyStamp { get; init; }
}
