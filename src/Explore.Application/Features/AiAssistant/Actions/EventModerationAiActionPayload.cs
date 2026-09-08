namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class EventModerationAiActionPayload
{
    public Guid? EventId { get; init; }

    public Guid? ExpectedConcurrencyStamp { get; init; }

    public bool? ManagementContextHasModerateLight { get; init; }

    public bool? ManagementContextHasModerateHeavy { get; init; }

    public bool? ManagementContextHasUnmoderate { get; init; }

    public string? ReasonCode { get; init; }

    public string? CorrelationId { get; init; }

    public string? DestructiveSummary { get; init; }

    public string? ConfirmationPhrase { get; init; }

    public bool? AcknowledgedConsequences { get; init; }
}
