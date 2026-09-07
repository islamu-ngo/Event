namespace Explore.Application.Contracts.Infrastructure;

public sealed record EmailDispatchPublishResult(
    EmailDispatchPublishOutcome Outcome,
    ulong? PublishSequenceNumber = null,
    ushort? ReplyCode = null,
    string? ReplyText = null,
    string? FailureCategory = null)
{
    public bool Succeeded => Outcome is EmailDispatchPublishOutcome.Confirmed or EmailDispatchPublishOutcome.Disabled;

    public static EmailDispatchPublishResult Disabled() => new(EmailDispatchPublishOutcome.Disabled);

    public static EmailDispatchPublishResult Confirmed(ulong publishSequenceNumber) =>
        new(EmailDispatchPublishOutcome.Confirmed, publishSequenceNumber);
}
