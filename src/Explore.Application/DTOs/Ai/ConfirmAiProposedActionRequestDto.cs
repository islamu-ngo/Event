namespace Explore.Application.DTOs.Ai;

/// <summary>The exact action preview accepted by the caller; it is not an authority token.</summary>
public sealed record ConfirmAiProposedActionRequestDto
{
    public required string ExpectedVersion { get; init; }
}
