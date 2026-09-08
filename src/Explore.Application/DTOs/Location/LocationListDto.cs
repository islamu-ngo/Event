using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Location;

public sealed record LocationListDto
{
    [JsonIgnore]
    public Guid TenantId { get; init; }

    public Guid Id { get; init; }
    public required string FullName { get; init; }
    public required string Address { get; init; }
    public required string City { get; init; }
    public required string Country { get; init; }
    public string? Timezone { get; init; }
    public Guid ConcurrencyStamp { get; init; }
}
