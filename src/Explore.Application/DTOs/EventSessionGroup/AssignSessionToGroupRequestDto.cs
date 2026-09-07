namespace Explore.Application.DTOs.EventSessionGroup;

public sealed record AssignSessionToGroupRequestDto
{
    public Guid EventId { get; init; }
    public Guid EventSessionGroupId { get; init; }
    public Guid EventSessionId { get; init; }
    public bool IsPrimary { get; init; }
    public int SortOrder { get; init; }
}
