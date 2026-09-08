namespace Explore.Application.DTOs.Actor;

public sealed record GlobalModerationRequestDto
{
    public string ReasonCode { get; init; } = string.Empty;
}
