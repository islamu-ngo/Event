namespace Explore.Application.DTOs.EventSession;

public sealed record EventSessionLifecycleRequestDto
{
    public Guid ExpectedConcurrencyStamp { get; init; }
}
