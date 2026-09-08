using Explore.Domain.Constants;

namespace Explore.Application.Authorization;

/// <summary>
/// Translates between external API key scopes (e.g. <c>events:write</c>) and the fine-grained
/// <see cref="AuthorizationActions"/> used by handlers and Cerbos policies.
/// Used by authorization services to enforce that a machine principal's scopes permit a given
/// resource + action pair before delegating to authority checks.
/// </summary>
public static class MachineScopeMapping
{
    /// <summary>
    /// Returns <c>true</c> when the supplied scope set permits the given resource kind + action.
    /// A machine principal must satisfy this scope gate in addition to owner-type authority.
    /// </summary>
    public static bool ScopesPermit(IReadOnlyList<string> scopes, string resourceKind, string action)
    {
        if (scopes is null || scopes.Count == 0)
            return false;

        var scopeSet = new HashSet<string>(scopes, StringComparer.OrdinalIgnoreCase);

        if (resourceKind is ResourceKinds.EventOrganizerClaim or ResourceKinds.RegistrationForm)
            return false;

        if (scopeSet.Contains(ExternalApiKeyScopes.AdminInstance))
            return true;

        if (scopeSet.Contains(InternalMachineScopes.ProcessIncomingWebhook))
        {
            return resourceKind == ResourceKinds.Webhook &&
                   action == AuthorizationActions.Webhooks.ProcessIncoming;
        }

        var isWrite = IsWriteAction(action);

        switch (resourceKind)
        {
            case ResourceKinds.Event:
            case ResourceKinds.EventSession:
            case ResourceKinds.EventSessionGroup:
            case ResourceKinds.EventSessionAgendaItem:
            case ResourceKinds.EventDay:
            case ResourceKinds.EventAgendaItem:
            case ResourceKinds.EventContactShareConsent:
                return isWrite
                    ? HasAny(scopeSet, ExternalApiKeyScopes.EventsWrite, ExternalApiKeyScopes.AdminTenant)
                    : HasAny(scopeSet, ExternalApiKeyScopes.EventsRead, ExternalApiKeyScopes.EventsWrite, ExternalApiKeyScopes.AdminTenant);

            case ResourceKinds.Organization:
            case ResourceKinds.OrganizationMember:
            case ResourceKinds.OrganizationReview:
                return isWrite
                    ? HasAny(scopeSet, ExternalApiKeyScopes.OrganizationsWrite, ExternalApiKeyScopes.AdminTenant)
                    : HasAny(scopeSet, ExternalApiKeyScopes.OrganizationsRead, ExternalApiKeyScopes.OrganizationsWrite, ExternalApiKeyScopes.AdminTenant);

            case ResourceKinds.Group:
            case ResourceKinds.GroupMember:
                return isWrite
                    ? HasAny(scopeSet, ExternalApiKeyScopes.GroupsWrite, ExternalApiKeyScopes.AdminTenant)
                    : HasAny(scopeSet, ExternalApiKeyScopes.GroupsRead, ExternalApiKeyScopes.GroupsWrite, ExternalApiKeyScopes.AdminTenant);

            case ResourceKinds.User:
            case ResourceKinds.ActorSubscription:
                return isWrite
                    ? HasAny(scopeSet, ExternalApiKeyScopes.UsersWrite, ExternalApiKeyScopes.AdminTenant)
                    : HasAny(scopeSet, ExternalApiKeyScopes.UsersRead, ExternalApiKeyScopes.UsersWrite, ExternalApiKeyScopes.AdminTenant);

            case ResourceKinds.AiConversation:
                return action switch
                {
                    AuthorizationActions.ProposeAction => HasAny(
                        scopeSet,
                        ExternalApiKeyScopes.McpPropose,
                        ExternalApiKeyScopes.UsersWrite,
                        ExternalApiKeyScopes.AdminTenant),
                    AuthorizationActions.View => HasAny(
                        scopeSet,
                        ExternalApiKeyScopes.McpRead,
                        ExternalApiKeyScopes.McpPropose,
                        ExternalApiKeyScopes.UsersRead,
                        ExternalApiKeyScopes.UsersWrite,
                        ExternalApiKeyScopes.AdminTenant),
                    _ => isWrite
                        ? HasAny(scopeSet, ExternalApiKeyScopes.UsersWrite, ExternalApiKeyScopes.AdminTenant)
                        : HasAny(scopeSet, ExternalApiKeyScopes.UsersRead, ExternalApiKeyScopes.UsersWrite, ExternalApiKeyScopes.AdminTenant)
                };

            case ResourceKinds.Category:
            case ResourceKinds.Tag:
            case ResourceKinds.Location:
            case ResourceKinds.LocationRoom:
            case ResourceKinds.CustomPropertyDefinition:
            case ResourceKinds.CustomPropertyTemplate:
            case ResourceKinds.CustomPropertyValue:
            case ResourceKinds.CustomPropertyProjection:
            case ResourceKinds.CustomPropertyGovernance:
                return isWrite
                    ? HasAny(scopeSet, ExternalApiKeyScopes.AdminTenant)
                    : HasAny(scopeSet, ExternalApiKeyScopes.LookupsRead, ExternalApiKeyScopes.AdminTenant);

            case ResourceKinds.Tenant:
            case ResourceKinds.TenantUserRoleGrant:
            case ResourceKinds.TenantSetting:
            case ResourceKinds.Webhook:
                return HasAny(scopeSet, ExternalApiKeyScopes.AdminTenant);

            case ResourceKinds.EmailDispatch:
                return HasAny(scopeSet, ExternalApiKeyScopes.AdminTenant, ExternalApiKeyScopes.AdminInstance);

            case ResourceKinds.InstanceSetting:
            case ResourceKinds.AtprotoRecord:
            case ResourceKinds.IndexedDid:
            case ResourceKinds.PlatformNamespace:
                return HasAny(scopeSet, ExternalApiKeyScopes.AdminInstance);

            case ResourceKinds.StorageObject:
                return isWrite
                    ? HasAny(scopeSet, ExternalApiKeyScopes.EventsWrite, ExternalApiKeyScopes.OrganizationsWrite, ExternalApiKeyScopes.AdminTenant)
                    : HasAny(scopeSet, ExternalApiKeyScopes.EventsRead, ExternalApiKeyScopes.OrganizationsRead, ExternalApiKeyScopes.AdminTenant);

            case ResourceKinds.Actor:
                return HasAny(scopeSet, ExternalApiKeyScopes.AdminTenant);

            case ResourceKinds.Notification:
                return HasAny(scopeSet, ExternalApiKeyScopes.UsersRead, ExternalApiKeyScopes.UsersWrite, ExternalApiKeyScopes.AdminTenant);

            default:
                return false;
        }
    }

    private static bool IsWriteAction(string action) => action switch
    {
        AuthorizationActions.Create => true,
        AuthorizationActions.Update => true,
        AuthorizationActions.Delete => true,
        AuthorizationActions.ManageMembers => true,
        AuthorizationActions.Lock => true,
        AuthorizationActions.Unlock => true,
        AuthorizationActions.Events.ModerateLight => true,
        AuthorizationActions.Events.ModerateHeavy => true,
        AuthorizationActions.Events.Unmoderate => true,
        AuthorizationActions.Events.ManagePaidEventCommerce => true,
        AuthorizationActions.SyncApply => true,
        AuthorizationActions.ExportSharedContacts => true,
        AuthorizationActions.SendMessage => true,
        AuthorizationActions.ConfirmAction => true,
        AuthorizationActions.RejectAction => true,
        AuthorizationActions.CancelRun => true,
        AuthorizationActions.ProposeAction => true,
        AuthorizationActions.Webhooks.Create => true,
        AuthorizationActions.Webhooks.Update => true,
        AuthorizationActions.Webhooks.Delete => true,
        AuthorizationActions.Webhooks.RotateSecret => true,
        AuthorizationActions.Webhooks.Test => true,
        AuthorizationActions.Webhooks.Retry => true,
        AuthorizationActions.Webhooks.RedriveIncoming => true,
        AuthorizationActions.Webhooks.Pause => true,
        AuthorizationActions.Webhooks.Resume => true,
        AuthorizationActions.Webhooks.ReconcilePublication => true,
        AuthorizationActions.Webhooks.AbandonPublication => true,
        AuthorizationActions.Webhooks.BulkReplay => true,
        AuthorizationActions.Webhooks.ManageProvider => true,
        AuthorizationActions.Webhooks.OpenProviderPortal => true,
        AuthorizationActions.Webhooks.ProcessIncoming => true,
        AuthorizationActions.EmailDispatches.ManageTenant => true,
        AuthorizationActions.EmailDispatches.Park => true,
        AuthorizationActions.EmailDispatches.Replay => true,
        AuthorizationActions.EmailDispatches.Resolve => true,
        AuthorizationActions.EmailDispatches.Reconcile => true,
        _ => false
    };

    private static bool HasAny(HashSet<string> scopes, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (scopes.Contains(candidate))
                return true;
        }
        return false;
    }
}
