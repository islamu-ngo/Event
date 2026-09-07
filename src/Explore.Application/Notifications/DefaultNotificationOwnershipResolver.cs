using Explore.Application.Contracts.Notifications;
using Microsoft.Extensions.Options;

namespace Explore.Application.Notifications;

public sealed class DefaultNotificationOwnershipResolver : INotificationOwnershipResolver
{
    private readonly NotificationRoutingOptions _options;

    public DefaultNotificationOwnershipResolver(IOptions<NotificationRoutingOptions> options)
    {
        _options = options.Value;
        var errors = _options.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    public Task<NotificationOwnershipDecision> ResolveAsync(
        NotificationIntentDraft draft,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var owner = _options.GetOwner(draft.Category);
        var decision = owner switch
        {
            NotificationOwnership.AccountAuthority => new NotificationOwnershipDecision(
                draft.Category,
                owner,
                AccountAuthorityKind: _options.DefaultAccountAuthorityKind,
                RequiresLocalAudit: draft.IsIslamuInitiated),
            NotificationOwnership.ExternalWorkflowProvider => new NotificationOwnershipDecision(
                draft.Category,
                owner,
                ExternalWorkflowProviderKind: ResolveExternalProvider(draft.Category),
                RequiresLocalAudit: draft.IsUserFacing),
            NotificationOwnership.Disabled => new NotificationOwnershipDecision(
                draft.Category,
                owner,
                RequiresLocalAudit: false),
            _ => new NotificationOwnershipDecision(draft.Category, owner)
        };

        return Task.FromResult(decision);
    }

    private ExternalWorkflowProviderKind ResolveExternalProvider(NotificationCategory category)
    {
        return category is NotificationCategory.TrustSafetyReporting or NotificationCategory.TrustSafetyModeration
            ? _options.ExternalUserFacingModerationProvider
            : _options.ProviderInternalProvider;
    }
}
