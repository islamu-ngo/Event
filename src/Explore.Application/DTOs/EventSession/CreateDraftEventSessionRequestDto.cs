namespace Explore.Application.DTOs.EventSession;

public sealed record CreateDraftEventSessionRequestDto
{
    public Guid EventId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}
