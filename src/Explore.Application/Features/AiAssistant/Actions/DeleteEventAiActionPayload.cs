namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class DeleteEventAiActionPayload
{
    public Guid? EventId { get; init; }

    public Guid? ExpectedConcurrencyStamp { get; init; }

    public bool? ManagementContextHasDelete { get; init; }

    public string? DestructiveSummary { get; init; }

    public string? ConfirmationPhrase { get; init; }

    public bool? AcknowledgedConsequences { get; init; }
}
