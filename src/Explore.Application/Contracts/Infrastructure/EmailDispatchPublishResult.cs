// ABOUTME: Application-layer publish result returned by EmailDispatch transport adapters.
// ABOUTME: Captures confirm/return/nack metadata without email content or provider-specific exceptions.

namespace Explore.Application.Contracts.Infrastructure;

public sealed record EmailDispatchPublishResult(
    EmailDispatchPublishOutcome Outcome,
    ulong? PublishSequenceNumber = null,
    ushort? ReplyCode = null,
    string? ReplyText = null,
    EmailDispatchPublishFailure? FailureCategory = null)
{
    public bool Succeeded => Outcome is EmailDispatchPublishOutcome.Confirmed or EmailDispatchPublishOutcome.Disabled;

    public static EmailDispatchPublishResult Disabled() => new(EmailDispatchPublishOutcome.Disabled);

    public static EmailDispatchPublishResult Confirmed(ulong publishSequenceNumber) =>
        new(EmailDispatchPublishOutcome.Confirmed, publishSequenceNumber);
}
