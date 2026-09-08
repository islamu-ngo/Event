using Explore.Application.Authorization;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

public partial class FallbackAuthorizationService
{
    private async Task<bool> EvaluateMachineCallerAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (resourceKind == ResourceKinds.RegistrationForm ||
            resourceKind == ResourceKinds.Event &&
            action is AuthorizationActions.Events.ManageRegistrations
                or AuthorizationActions.Events.ManageRegistrationWorkflow
                or AuthorizationActions.Events.ManageTickets
                or AuthorizationActions.Events.ManagePaidEventCommerce)
        {
            return false;
        }

        var context = _machinePrincipalAccessor.Current;
        if (context is null)
        {
            return false;
        }

        if (SafeMode && context.OwnerType != ExternalApiKeyOwnerType.InstanceAdmin)
        {
            return false;
        }

        if (!MachineScopeMapping.ScopesPermit(context.Scopes, resourceKind, action))
        {
            return false;
        }

        if (resourceKind == ResourceKinds.TenantSetting
            && !IsTenantBrandingDocument(resourceAttributes)
            && resourceAttributes?.TryGetValue("isLockedByInstance", out var lockedObj) == true
            && lockedObj is true)
        {
            return false;
        }

        return context.OwnerType switch
        {
            ExternalApiKeyOwnerType.InstanceAdmin => IsInstanceAdminFallbackAllowed(resourceKind, action),
            ExternalApiKeyOwnerType.Tenant => EvaluateTenantOwnerMachineAccess(context, resourceKind, resourceId, resourceAttributes),
            ExternalApiKeyOwnerType.Organization => EvaluateOrganizationOwnerMachineAccess(context, resourceKind, resourceId, resourceAttributes),
            ExternalApiKeyOwnerType.Group => EvaluateGroupOwnerMachineAccess(context, resourceKind, resourceId, resourceAttributes),
            ExternalApiKeyOwnerType.User => await EvaluateUserOwnerMachineAccessAsync(context, resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            _ => false,
        };
    }

    private bool EvaluateTenantOwnerMachineAccess(
        Explore.Application.Authentication.ApiKeyPrincipalContext context,
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? resourceAttributes)
    {
        if (!context.TenantId.HasValue)
        {
            return false;
        }

        if (resourceKind == ResourceKinds.InstanceSetting
            || resourceKind == ResourceKinds.AtprotoRecord
            || resourceKind == ResourceKinds.IndexedDid
            || resourceKind == ResourceKinds.PlatformNamespace)
        {
            return false;
        }

        var resolvedTenantId = ResolveTenantId(resourceAttributes);
        return resolvedTenantId == context.TenantId.Value;
    }

    private bool EvaluateOrganizationOwnerMachineAccess(
        Explore.Application.Authentication.ApiKeyPrincipalContext context,
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? resourceAttributes)
    {
        if (!context.TenantId.HasValue)
        {
            return false;
        }

        var resolvedTenantId = ResolveTenantId(resourceAttributes);
        if (resolvedTenantId != context.TenantId.Value)
        {
            return false;
        }

        if (IsTenantWideResource(resourceKind))
        {
            return false;
        }

        if (IsOrganizationScopedResource(resourceKind))
        {
            var orgId = ResolveOrganizationId(resourceAttributes, resourceId);
            if (orgId == context.OwnerId)
            {
                return true;
            }

            if (resourceKind == ResourceKinds.Organization && Guid.TryParse(resourceId, out var directOrgId))
            {
                return directOrgId == context.OwnerId;
            }

            return false;
        }

        return IsUserOrLookupResource(resourceKind);
    }

    private bool EvaluateGroupOwnerMachineAccess(
        Explore.Application.Authentication.ApiKeyPrincipalContext context,
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? resourceAttributes)
    {
        if (!context.TenantId.HasValue)
        {
            return false;
        }

        var resolvedTenantId = ResolveTenantId(resourceAttributes);
        if (resolvedTenantId != context.TenantId.Value)
        {
            return false;
        }

        if (IsTenantWideResource(resourceKind) || IsOrganizationScopedResource(resourceKind))
        {
            return false;
        }

        if (resourceKind == ResourceKinds.Group || resourceKind == ResourceKinds.GroupMember)
        {
            var groupId = ResolveGroupId(resourceAttributes, resourceId);
            return groupId == context.OwnerId;
        }

        return IsUserOrLookupResource(resourceKind);
    }

    private async Task<bool> EvaluateUserOwnerMachineAccessAsync(
        Explore.Application.Authentication.ApiKeyPrincipalContext context,
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (await _adminContext.IsInstanceAdminAsync(context.OwnerId, cancellationToken))
        {
            return IsInstanceAdminFallbackAllowed(resourceKind, action);
        }

        if (!context.TenantId.HasValue)
        {
            return false;
        }

        var resolvedTenantId = ResolveTenantId(resourceAttributes);
        if (resolvedTenantId != context.TenantId.Value)
        {
            return false;
        }

        if (resourceKind == ResourceKinds.User)
        {
            if (Guid.TryParse(resourceId, out var userResourceId) && userResourceId == context.OwnerId)
            {
                return true;
            }

            return false;
        }

        if (resourceKind == ResourceKinds.InstanceSetting
            || resourceKind == ResourceKinds.AtprotoRecord
            || resourceKind == ResourceKinds.IndexedDid
            || resourceKind == ResourceKinds.PlatformNamespace
            || resourceKind == ResourceKinds.Tenant
            || resourceKind == ResourceKinds.TenantSetting
            || resourceKind == ResourceKinds.TenantUserRoleGrant)
        {
            return false;
        }

        var adminTenants = await _adminContext.GetAdminTenantIdsAsync(context.OwnerId, cancellationToken);
        bool isTenantAdmin = adminTenants.Contains(context.TenantId.Value);

        if (IsTenantWideResource(resourceKind))
        {
            return isTenantAdmin;
        }

        if (IsOrganizationScopedResource(resourceKind))
        {
            var orgId = ResolveOrganizationId(resourceAttributes, resourceId);
            var adminOrgIds = await _adminContext.GetAdminOrganizationIdsAsync(context.OwnerId, context.TenantId.Value, cancellationToken);
            if (orgId.HasValue && adminOrgIds.Contains(orgId.Value))
            {
                return true;
            }

            return isTenantAdmin;
        }

        if (resourceKind == ResourceKinds.Group || resourceKind == ResourceKinds.GroupMember)
        {
            var groupId = ResolveGroupId(resourceAttributes, resourceId);
            var adminGroupIds = await _adminContext.GetAdminGroupIdsAsync(context.OwnerId, context.TenantId.Value, cancellationToken);
            if (groupId.HasValue && adminGroupIds.Contains(groupId.Value))
            {
                return true;
            }

            return isTenantAdmin;
        }

        return IsUserOrLookupResource(resourceKind);
    }

    private static bool IsTenantWideResource(string resourceKind) =>
        resourceKind == ResourceKinds.Tenant
        || resourceKind == ResourceKinds.TenantSetting
        || resourceKind == ResourceKinds.TenantUserRoleGrant
        || resourceKind == ResourceKinds.Category
        || resourceKind == ResourceKinds.Tag
        || resourceKind == ResourceKinds.Location
        || resourceKind == ResourceKinds.LocationRoom
        || resourceKind == ResourceKinds.CustomPropertyDefinition
        || resourceKind == ResourceKinds.CustomPropertyTemplate
        || resourceKind == ResourceKinds.CustomPropertyProjection
        || resourceKind == ResourceKinds.CustomPropertyGovernance
        || resourceKind == ResourceKinds.Webhook
        || resourceKind == ResourceKinds.Actor;

    private static bool IsOrganizationScopedResource(string resourceKind) =>
        resourceKind == ResourceKinds.Organization
        || resourceKind == ResourceKinds.OrganizationMember
        || resourceKind == ResourceKinds.OrganizationReview
        || resourceKind == ResourceKinds.Event
        || resourceKind == ResourceKinds.EventSession
        || resourceKind == ResourceKinds.EventSessionAgendaItem
        || resourceKind == ResourceKinds.EventDay
        || resourceKind == ResourceKinds.EventAgendaItem
        || resourceKind == ResourceKinds.EventContactShareConsent
        || resourceKind == ResourceKinds.StorageObject
        || resourceKind == ResourceKinds.CustomPropertyValue;

    private static bool IsUserOrLookupResource(string resourceKind) =>
        resourceKind == ResourceKinds.User
        || resourceKind == ResourceKinds.Notification
        || resourceKind == ResourceKinds.ActorSubscription
        || resourceKind == ResourceKinds.AiConversation;

    private static Guid? ResolveGroupId(IDictionary<string, object>? resourceAttributes, string resourceId)
    {
        if (resourceAttributes?.TryGetValue("groupId", out var groupIdObj) == true)
        {
            if (groupIdObj is Guid gid) return gid;
            if (groupIdObj is string s && Guid.TryParse(s, out var parsed)) return parsed;
        }

        return Guid.TryParse(resourceId, out var fromId) ? fromId : null;
    }
}
