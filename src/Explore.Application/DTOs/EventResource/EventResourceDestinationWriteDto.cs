namespace Explore.Application.DTOs.EventResource;

/// <summary>Write-only external destination input; never use it as a resource read model.</summary>
public sealed record EventResourceDestinationWriteDto(Guid ExpectedVersion, string Destination)
{
    public override string ToString() => nameof(EventResourceDestinationWriteDto);
}
