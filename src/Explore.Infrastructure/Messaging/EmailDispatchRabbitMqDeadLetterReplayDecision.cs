using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;

namespace Explore.Infrastructure.Messaging;

internal static class EmailDispatchRabbitMqDeadLetterReplayDecision
{
    public static EmailDispatchRabbitMqDeadLetterReplaySettlement Decide(
        EmailDispatchPointer pointer,
        EmailDispatchOutbox? dispatch)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        if (dispatch is null)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Park("outbox_missing");
        }

        if (dispatch.TenantId != pointer.TenantId)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Park("tenant_mismatch");
        }

        if (dispatch.PublishEventId != pointer.PublishEventId)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Park("publish_event_mismatch");
        }

        if (dispatch.EventId != pointer.EventId)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Park("event_mismatch");
        }

        if (dispatch.Status == EmailDispatchStatus.Sent)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Park("already_sent");
        }

        if (dispatch.Status == EmailDispatchStatus.Skipped)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Park("already_skipped");
        }

        if (dispatch.Status == EmailDispatchStatus.Processing)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Park("already_processing");
        }

        if (dispatch.Status == EmailDispatchStatus.Unknown)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Park("unknown_requires_reconciliation");
        }

        if (dispatch.Status == EmailDispatchStatus.Pending)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Replay(requiresDurableReplayReset: false);
        }

        if (dispatch.Status is EmailDispatchStatus.DeadLettered
            or EmailDispatchStatus.Parked
            or EmailDispatchStatus.RetryScheduled)
        {
            return EmailDispatchRabbitMqDeadLetterReplaySettlement.Replay(requiresDurableReplayReset: true);
        }

        return EmailDispatchRabbitMqDeadLetterReplaySettlement.Park("invalid_status");
    }
}

internal sealed record EmailDispatchRabbitMqDeadLetterReplaySettlement(
    EmailDispatchRabbitMqDeadLetterReplayAction Action,
    string FailureCategory,
    bool RequiresDurableReplayReset)
{
    public static EmailDispatchRabbitMqDeadLetterReplaySettlement Replay(bool requiresDurableReplayReset) =>
        new(EmailDispatchRabbitMqDeadLetterReplayAction.Replay, "none", requiresDurableReplayReset);

    public static EmailDispatchRabbitMqDeadLetterReplaySettlement Park(string failureCategory) =>
        new(EmailDispatchRabbitMqDeadLetterReplayAction.Park, failureCategory, RequiresDurableReplayReset: false);

    public static EmailDispatchRabbitMqDeadLetterReplaySettlement Nack(string failureCategory) =>
        new(EmailDispatchRabbitMqDeadLetterReplayAction.Nack, failureCategory, RequiresDurableReplayReset: false);
}

internal enum EmailDispatchRabbitMqDeadLetterReplayAction
{
    Replay,
    Park,
    Nack
}
