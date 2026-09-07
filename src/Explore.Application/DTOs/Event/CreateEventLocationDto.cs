namespace Explore.Application.DTOs.Event;

public sealed record CreateEventLocationDto
{
    public required string TempKey { get; init; }
    public required string FullName { get; init; }
    public required string Address { get; init; }
    public required string Postcode { get; init; }
    public required string Country { get; init; }
    public required string City { get; init; }
    public string? Timezone { get; init; }
}
