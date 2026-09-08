namespace Explore.Application.DTOs.Event;

public sealed record EventModerationHistoryDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public Guid? ModeratorUserId { get; init; }
    public int ActionKindId { get; init; }
    public required string ActionKindName { get; init; }
    public required string ReasonCode { get; init; }
    public int PreviousStatusId { get; init; }
    public required string PreviousStatusName { get; init; }
    public int ResultingStatusId { get; init; }
    public required string ResultingStatusName { get; init; }
    public bool IsIrreversible { get; init; }
    public bool AllowsUnmoderation { get; init; }
    public Guid? SourceModerationRecordId { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
