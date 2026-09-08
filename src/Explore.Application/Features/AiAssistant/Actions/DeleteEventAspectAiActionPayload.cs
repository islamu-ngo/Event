namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class DeleteEventAspectAiActionPayload
{
    public Guid? EventId { get; init; }

    public Guid? ExpectedConcurrencyStamp { get; init; }

    public string? AspectKind { get; init; }

    public bool? ManagementContextHasEdit { get; init; }

    public string? DestructiveSummary { get; init; }

    public string? ConfirmationPhrase { get; init; }

    public bool? AcknowledgedConsequences { get; init; }
}
