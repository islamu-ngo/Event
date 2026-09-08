// ABOUTME: Default category-based notification ownership resolver.
// ABOUTME: Applies account-authority, ISLAMU product, and external workflow ownership rules.

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
            NotificationOwnership.AccountAuthority => ResolveAccountAuthority(draft),
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

    private static NotificationOwnershipDecision ResolveAccountAuthority(NotificationIntentDraft draft)
    {
        var authority = draft.AccountAuthority
            ?? throw new InvalidOperationException("Identity lifecycle routing requires a resolved linked account authority.");
        if (authority.UserId != draft.UserId)
            throw new InvalidOperationException("Identity lifecycle recipient does not own the resolved account authority.");

        return new NotificationOwnershipDecision(draft.Category, NotificationOwnership.AccountAuthority,
            AccountAuthorityKind: authority.Kind,
            RequiresLocalAudit: draft.IsIslamuInitiated && authority.Kind != AccountAuthorityKind.LocalIdentity);
    }

    private ExternalWorkflowProviderKind ResolveExternalProvider(NotificationCategory category)
    {
        return category is NotificationCategory.TrustSafetyReporting or NotificationCategory.TrustSafetyModeration
            ? _options.ExternalUserFacingModerationProvider
            : _options.ProviderInternalProvider;
    }
}
