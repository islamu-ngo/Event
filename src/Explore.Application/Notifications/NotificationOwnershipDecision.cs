namespace Explore.Application.Notifications;

public sealed record NotificationOwnershipDecision(
    NotificationCategory Category,
    NotificationOwnership Ownership,
    AccountAuthorityKind AccountAuthorityKind = AccountAuthorityKind.None,
    ExternalWorkflowProviderKind ExternalWorkflowProviderKind = ExternalWorkflowProviderKind.None,
    bool RequiresLocalAudit = true)
{
    public bool IsLocalIslamuDelivery => Ownership == NotificationOwnership.IslamuEvent;
}
