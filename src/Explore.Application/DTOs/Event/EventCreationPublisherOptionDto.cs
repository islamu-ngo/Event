namespace Explore.Application.DTOs.Event;

public sealed record EventCreationPublisherOptionDto
{
    public required string PublisherMode { get; init; }

    public Guid? PublisherId { get; init; }

    public required string DisplayName { get; init; }

    public int? RoleId { get; init; }

    public bool CanPublish { get; init; }

    public string? Reason { get; init; }
}
