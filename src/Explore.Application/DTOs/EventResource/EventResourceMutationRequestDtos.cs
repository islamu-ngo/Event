namespace Explore.Application.DTOs.EventResource;

/// <summary>The caller retains its UUIDv7 resource identity across retries; event authority comes from the route.</summary>
public sealed record CreateEventResourceRequestDto(Guid ResourceId, EventResourceDraftDto Draft)
{
    public override string ToString() => nameof(CreateEventResourceRequestDto);
}

public sealed record UpdateEventResourceRequestDto(Guid ExpectedVersion, EventResourceDraftDto Draft)
{
    public override string ToString() => nameof(UpdateEventResourceRequestDto);
}

public sealed record EventResourceVersionRequestDto(Guid ExpectedVersion);
