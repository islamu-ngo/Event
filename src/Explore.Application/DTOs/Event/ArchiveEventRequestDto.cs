namespace Explore.Application.DTOs.Event;

public sealed record ArchiveEventRequestDto
{
    public Guid ExpectedConcurrencyStamp { get; init; }
}
