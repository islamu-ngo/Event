namespace Explore.Application.DTOs.Event;

public sealed record EventModerationRequestDto
{
    public string? ReasonCode { get; init; }
    public string? CorrelationId { get; init; }
}
