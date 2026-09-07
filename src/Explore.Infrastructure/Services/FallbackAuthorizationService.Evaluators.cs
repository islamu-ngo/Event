using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Helpers;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Infrastructure.Services;

public partial class FallbackAuthorizationService
{
    private async Task<bool> EvaluateTenantSettingAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        bool isView = string.Equals(
            action,
            AuthorizationActions.TenantSettings.View,
            StringComparison.Ordinal);
        if (!isView
            && !IsTenantBrandingDocument(resourceAttributes)
            && resourceAttributes?.TryGetValue("isLockedByInstance", out var lockedObj) == true
            && lockedObj is true)
        {
            LogDecision("deny", "locked_by_instance", "islamuevent_tenant_setting", action);
            return false;
        }

        bool isInstanceAdmin =
            await _adminContext.IsInstanceAdminAsync(cancellationToken);
        if (isInstanceAdmin && isView)
        {
            LogDecision(
                "allow",
                "is_instance_admin=true,action=view",
                ResourceKinds.TenantSetting,
                action);
            return true;
        }

        var tenantId = _tenantContext.TenantId;
        if (resourceAttributes?.TryGetValue("tenantId", out var tenantIdObj) == true
            && AttributeResolver.TryGetGuid(tenantIdObj, out var parsedTenantId))
        {
            tenantId = parsedTenantId;
        }

        var isTenantAdmin = await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
        LogDecision(
            isTenantAdmin ? "allow" : "deny",
            $"is_tenant_admin={isTenantAdmin}",
            "islamuevent_tenant_setting",
            action);
        return isTenantAdmin;
    }

    private static bool IsTenantBrandingDocument(IDictionary<string, object>? resourceAttributes)
        => resourceAttributes?.TryGetValue("documentKey", out var documentKey) == true
            && documentKey is string documentKeyString
            && string.Equals(documentKeyString, "tenant.branding", StringComparison.Ordinal);

    private async Task<bool> EvaluateOrganizationAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (IsPreCreateCheck(action, resourceAttributes))
        {
            var preCreateTenantId = ResolveTenantId(resourceAttributes);
            var userId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
            var allowed = preCreateTenantId == _tenantContext.TenantId && userId.HasValue;

            LogDecision(
                allowed ? "allow" : "deny",
                $"organization_pre_create user_present={userId.HasValue}",
                "islamuevent_organization",
                action);
            return allowed;
        }

        Guid orgId;

        if (resourceAttributes?.TryGetValue("organizationId", out var orgIdObj) != true)
        {
            if (!AttributeResolver.TryGetGuid(resourceId, out var orgIdFromResource))
            {
                LogDecision("deny", "missing_organization_id", "islamuevent_organization", action);
                return false;
            }

            orgId = orgIdFromResource;
        }
        else
        {
            if (AttributeResolver.TryGetGuid(orgIdObj, out var parsedOrgId))
            {
                orgId = parsedOrgId;
            }
            else if (!AttributeResolver.TryGetGuid(resourceId, out orgId))
            {
                LogDecision("deny", "invalid_organization_id", "islamuevent_organization", action);
                return false;
            }
        }

        var tenantId = _tenantContext.TenantId;
        var isTenantAdmin = await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
        if (action == AuthorizationActions.Organizations.ReviewEvidence)
        {
            LogDecision(
                isTenantAdmin ? "allow" : "deny",
                $"organization_evidence_reviewer_tenant_admin={isTenantAdmin}",
                "islamuevent_organization",
                action);
            return isTenantAdmin;
        }

        if (action == AuthorizationActions.Organizations.SubmitEvidence)
        {
            var canSubmitEvidence = await _adminContext.IsOrganizationAdminAsync(orgId, cancellationToken);
            LogDecision(
                canSubmitEvidence ? "allow" : "deny",
                $"organization_evidence_submitter_org_admin={canSubmitEvidence}",
                "islamuevent_organization",
                action);
            return canSubmitEvidence;
        }

        if (isTenantAdmin)
        {
            LogDecision("allow", "is_tenant_admin=true", "islamuevent_organization", action);
            return true;
        }

        var isOrgAdmin = await _adminContext.IsOrganizationAdminAsync(orgId, cancellationToken);
        if (action == AuthorizationActions.Organizations.ViewEvidence)
        {
            LogDecision(
                isOrgAdmin ? "allow" : "deny",
                $"organization_evidence_submitter_org_admin={isOrgAdmin}",
                "islamuevent_organization",
                action);
            return isOrgAdmin;
        }
        LogDecision(
            isOrgAdmin ? "allow" : "deny",
            $"organization_admin={isOrgAdmin}",
            "islamuevent_organization",
            action);
        return isOrgAdmin;
    }

    private static bool IsPreCreateCheck(string action, IDictionary<string, object>? resourceAttributes)
    {
        return action == AuthorizationActions.Create
            && resourceAttributes?.TryGetValue("authorizationPhase", out var phase) == true
            && string.Equals(phase?.ToString(), AuthorizationPhases.PreCreate, StringComparison.Ordinal);
    }

    private async Task<bool> EvaluateTenantScopedAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        Guid tenantId;
        if (resourceAttributes?.ContainsKey("tenantId") == true)
        {
            if (!TryResolveGuidAttribute(resourceAttributes, "tenantId", out tenantId))
            {
                LogDecision("deny", "invalid_tenant_context", resourceKind, action);
                return false;
            }

            if (tenantId != _tenantContext.TenantId)
            {
                LogDecision("deny", "tenant_mismatch", resourceKind, action);
                return false;
            }
        }
        else
        {
            tenantId = _tenantContext.TenantId;
        }

        var isTenantAdmin = await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
        LogDecision(isTenantAdmin ? "allow" : "deny", $"is_tenant_admin={isTenantAdmin}", resourceKind, action);
        return isTenantAdmin;
    }

    private async Task<bool> EvaluateWebhookAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (!TryResolveWebhookOwnerKind(resourceAttributes, out var ownerKind))
        {
            return await EvaluateTenantScopedAccessAsync(
                resourceKind,
                resourceId,
                action,
                resourceAttributes,
                cancellationToken);
        }

        if (ownerKind == WebhookConsumerKind.Instance)
        {
            LogDecision("deny", "instance_owner_requires_instance_admin", resourceKind, action);
            return false;
        }

        if (ownerKind == WebhookConsumerKind.Tenant)
        {
            return await EvaluateTenantScopedAccessAsync(
                resourceKind,
                resourceId,
                action,
                resourceAttributes,
                cancellationToken);
        }

        if (!IsDelegatedWebhookAction(action))
        {
            LogDecision("deny", "delegated_webhook_action_not_allowed", resourceKind, action);
            return false;
        }

        var allowed = ownerKind switch
        {
            WebhookConsumerKind.Organization =>
                TryResolveGuidAttribute(resourceAttributes, "organizationId", out var organizationId) &&
                await _adminContext.IsOrganizationAdminAsync(organizationId, cancellationToken),
            WebhookConsumerKind.Group =>
                TryResolveGuidAttribute(resourceAttributes, "groupId", out var groupId) &&
                await _adminContext.IsGroupAdminAsync(groupId, cancellationToken),
            WebhookConsumerKind.User =>
                TryResolveGuidAttribute(resourceAttributes, "userId", out var ownerUserId) &&
                ownerUserId == (_adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken)),
            _ => false
        };

        LogDecision(
            allowed ? "allow" : "deny",
            allowed ? $"webhook_{ownerKind.ToString().ToLowerInvariant()}_owner" : "unrelated_webhook_owner",
            resourceKind,
            action);
        return allowed;
    }

    private static bool IsDelegatedWebhookAction(string action)
        => action is AuthorizationActions.Webhooks.View
            or AuthorizationActions.Webhooks.Create
            or AuthorizationActions.Webhooks.Update
            or AuthorizationActions.Webhooks.Delete
            or AuthorizationActions.Webhooks.RotateSecret
            or AuthorizationActions.Webhooks.Test
            or AuthorizationActions.Webhooks.Retry
            or AuthorizationActions.Webhooks.Pause
            or AuthorizationActions.Webhooks.Resume
            or AuthorizationActions.Webhooks.ViewDelivery
            or AuthorizationActions.Webhooks.OpenProviderPortal;

    private static bool TryResolveWebhookOwnerKind(
        IDictionary<string, object>? resourceAttributes,
        out WebhookConsumerKind ownerKind)
    {
        ownerKind = default;
        if (resourceAttributes?.TryGetValue("ownerKindId", out var rawKind) != true
            || !AttributeResolver.TryGetInt(rawKind, out var ownerKindId))
        {
            return false;
        }

        if (!Enum.IsDefined(typeof(WebhookConsumerKind), ownerKindId))
        {
            return false;
        }

        ownerKind = (WebhookConsumerKind)ownerKindId;
        return true;
    }

    private async Task<bool> EvaluateCustomPropertyProjectionAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is not (AuthorizationActions.CustomPropertyProjections.View or AuthorizationActions.CustomPropertyProjections.Update))
        {
            LogDecision("deny", "invalid_projection_action", ResourceKinds.CustomPropertyProjection, action);
            return false;
        }

        if (!TryResolveGuidAttribute(resourceAttributes, "tenantId", out var tenantId))
        {
            LogDecision("deny", "missing_tenant_context", ResourceKinds.CustomPropertyProjection, action);
            return false;
        }

        if (tenantId != _tenantContext.TenantId)
        {
            LogDecision("deny", "tenant_mismatch", ResourceKinds.CustomPropertyProjection, action);
            return false;
        }

        var isTenantAdmin = await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
        LogDecision(isTenantAdmin ? "allow" : "deny", $"is_tenant_admin={isTenantAdmin}", ResourceKinds.CustomPropertyProjection, action);
        return isTenantAdmin;
    }

    private async Task<bool> EvaluateTenantResourceAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is not (AuthorizationActions.View or AuthorizationActions.Update))
        {
            LogDecision("deny", "tenant_action_requires_instance_admin", ResourceKinds.Tenant, action);
            return false;
        }

        var tenantId = ResolveTenantId(resourceAttributes);
        if (tenantId != _tenantContext.TenantId)
        {
            LogDecision("deny", "tenant_mismatch", ResourceKinds.Tenant, action);
            return false;
        }

        var isTenantAdmin = await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
        LogDecision(isTenantAdmin ? "allow" : "deny", $"is_tenant_admin={isTenantAdmin}", ResourceKinds.Tenant, action);
        return isTenantAdmin;
    }

    private async Task<bool> EvaluateOrgScopedAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(resourceAttributes);
        if (await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken)
            && IsTenantAdminOrgScopedAction(action))
        {
            LogDecision("allow", "is_tenant_admin=true", resourceKind, action);
            return true;
        }

        var orgId = ResolveOrganizationId(resourceAttributes, resourceId);
        if (orgId.HasValue && await _adminContext.IsOrganizationAdminAsync(orgId.Value, cancellationToken))
        {
            LogDecision("allow", "organization_admin=true", resourceKind, action);
            return true;
        }

        LogDecision("deny", "no_admin_authority", resourceKind, action);
        return false;
    }

    private async Task<bool> EvaluateOrgReviewAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is "create" or "view")
            return true;

        return await EvaluateOrgScopedAccessAsync("islamuevent_organization_review", resourceId, action, resourceAttributes, cancellationToken);
    }

    private static bool IsTenantAdminOrgScopedAction(string action)
        => action is AuthorizationActions.View
            or AuthorizationActions.Create
            or AuthorizationActions.Update
            or AuthorizationActions.Delete
            or AuthorizationActions.ManageMembers;

    private async Task<bool> EvaluateRegistrationOrderAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is not (AuthorizationActions.RegistrationOrders.View
            or AuthorizationActions.RegistrationOrders.Cancel
            or AuthorizationActions.RegistrationOrders.Continue
            or AuthorizationActions.RegistrationOrders.Finalize
            or AuthorizationActions.RegistrationOrders.RequestRefund
            or AuthorizationActions.RegistrationOrders.RespondMaterialChange) ||
            !TryResolveEventContext(ResourceKinds.RegistrationOrder, resourceId, resourceAttributes, out var tenantId, out var eventId) ||
            tenantId != _tenantContext.TenantId)
        {
            return false;
        }

        var currentUserId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (currentUserId.HasValue &&
            TryResolveGuidAttribute(resourceAttributes, "accountUserId", out var accountUserId) &&
            currentUserId == accountUserId)
        {
            return true;
        }

        return action == AuthorizationActions.RegistrationOrders.View &&
               await EvaluateManageRegistrationsAccessAsync(
                   ResourceKinds.Event,
                   eventId.ToString("D"),
                   resourceAttributes,
                   tenantId,
                   eventId,
                   cancellationToken);
    }

    private async Task<bool> EvaluateEventScopedAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (resourceKind is "islamuevent_event" && action is "create")
            return await EvaluateEventCreateAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken);

        if (!TryResolveEventContext(resourceKind, resourceId, resourceAttributes, out var tenantId, out var eventId))
        {
            LogDecision("deny", "missing_event_context", resourceKind, action);
            return false;
        }

        if (tenantId != _tenantContext.TenantId)
        {
            LogDecision("deny", "tenant_mismatch", resourceKind, action);
            return false;
        }

        if ((resourceKind == ResourceKinds.Event
                && action is AuthorizationActions.Events.ManageRegistrations
                    or AuthorizationActions.Events.ManageRegistrationWorkflow
                    or AuthorizationActions.Events.ManageRegistrationChannels
                    or AuthorizationActions.Events.ViewRegistrationProviderHealth)
            || resourceKind == ResourceKinds.RegistrationForm)
        {
            return await EvaluateManageRegistrationsAccessAsync(
                ResourceKinds.Event,
                resourceId,
                resourceAttributes,
                tenantId,
                eventId,
                cancellationToken);
        }

        if (resourceKind == ResourceKinds.Event
            && action == AuthorizationActions.Events.ManageTickets)
        {
            return await EvaluateManageTicketsAccessAsync(resourceKind, resourceId, resourceAttributes, tenantId, eventId, cancellationToken);
        }

        // Admission authority is assignment-specific in Cerbos. Evaluate it before organization-admin,
        // actor-owner, and other broad event grants so those authorities cannot acquire door access.
        if (resourceKind == ResourceKinds.Event && IsAdmissionCheckInAction(action))
        {
            return await EvaluateAdmissionCheckInAccessAsync(
                resourceKind,
                resourceId,
                action,
                tenantId,
                eventId,
                cancellationToken);
        }

        if (resourceKind == ResourceKinds.Event
            && action == AuthorizationActions.Events.ManagePaidEventCommerce)
        {
            return await EvaluateManagePaidEventCommerceAccessAsync(resourceAttributes, cancellationToken);
        }

        if (resourceKind == ResourceKinds.EventOrganizerClaim && !IsOrganizerClaimAction(action))
        {
            LogDecision("deny", "unknown_organizer_claim_action", resourceKind, action);
            return false;
        }

        if (action == AuthorizationActions.Events.ClaimOrganizer)
        {
            return !_machinePrincipalAccessor.IsMachineCaller
                && !await _adminContext.IsInstanceAdminAsync(cancellationToken)
                && (_adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken)).HasValue;
        }

        if (action == AuthorizationActions.Events.WithdrawOrganizerClaim)
        {
            if (await _adminContext.IsInstanceAdminAsync(cancellationToken))
            {
                return false;
            }

            return await IsClaimantActorOwnerAsync(resourceAttributes, cancellationToken);
        }

        if (await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken)
            && (resourceKind is not (ResourceKinds.Event or ResourceKinds.EventOrganizerClaim)
                || IsTenantAdminEventAction(action)))
        {
            LogDecision("allow", "is_tenant_admin=true", resourceKind, action);
            return true;
        }

        if (resourceKind == ResourceKinds.Event && IsEventModerationAction(action))
        {
            LogDecision("deny", "moderation_requires_platform_or_tenant_admin", resourceKind, action);
            return false;
        }

        if (action == AuthorizationActions.Events.ReviewOrganizerClaim)
        {
            LogDecision("deny", "claim_review_requires_tenant_curator", resourceKind, action);
            return false;
        }

        var orgId = ResolveOrganizationId(resourceAttributes, resourceId);
        if (orgId.HasValue && await _adminContext.IsOrganizationAdminAsync(orgId.Value, cancellationToken))
        {
            LogDecision("allow", "organization_admin=true", resourceKind, action);
            return true;
        }

        if (await IsActorUserOwnerAsync(resourceAttributes, cancellationToken))
        {
            LogDecision("allow", "actor_user_owner=true", resourceKind, action);
            return true;
        }

        if (await EvaluateEventRolePermissionAsync(resourceKind, resourceId, action, tenantId, eventId, cancellationToken))
            return true;

        LogDecision("deny", "no_event_authority", resourceKind, action);
        return false;
    }

    private async Task<bool> EvaluateApprovePublishAccessAsync(
        string resourceId,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (_machinePrincipalAccessor.IsMachineCaller
            || !TryResolveEventContext(ResourceKinds.Event, resourceId, resourceAttributes, out var tenantId, out var eventId)
            || tenantId == Guid.Empty
            || eventId == Guid.Empty
            || tenantId != _tenantContext.TenantId)
        {
            return false;
        }

        if (await _adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            return true;
        }

        return !SafeMode
            && await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
    }

    private async Task<bool> EvaluateManageRegistrationsAccessAsync(
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? resourceAttributes,
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        if (_machinePrincipalAccessor.IsMachineCaller)
        {
            return false;
        }

        if (await IsVerifiedOrganizerControllerAsync(resourceAttributes, cancellationToken))
        {
            return true;
        }

        if (await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken))
        {
            return false;
        }

        return await EvaluateEventRolePermissionAsync(
            resourceKind,
            resourceId,
            AuthorizationActions.Events.ManageRegistrations,
            tenantId,
            eventId,
            cancellationToken);
    }

    private async Task<bool> EvaluateManageTicketsAccessAsync(
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? resourceAttributes,
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        if (_machinePrincipalAccessor.IsMachineCaller || await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken)) return false;
        if (await IsVerifiedOrganizerControllerAsync(resourceAttributes, cancellationToken)) return true;
        return await EvaluateEventRolePermissionAsync(resourceKind, resourceId, AuthorizationActions.Events.ManageTickets, tenantId, eventId, cancellationToken);
    }

    private async Task<bool> EvaluateAdmissionCheckInAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        if (_machinePrincipalAccessor.IsMachineCaller
            || await _adminContext.IsInstanceAdminAsync(cancellationToken)
            || await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken))
        {
            return false;
        }

        var userId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!userId.HasValue)
            return false;

        EventAuthoritySnapshot snapshot = await _eventAuthoritySnapshotService.GetForUserAndEventsAsync(
            tenantId,
            userId.Value,
            [eventId],
            cancellationToken);
        if (!snapshot.Events.TryGetValue(eventId, out EventAuthorityForUser? authority)
            || !HasAdmissionCheckInRole(authority))
        {
            return false;
        }

        return action == AuthorizationActions.Events.EventCheckInView
            ? authority.PermissionCodes.Contains(PermissionCodes.EventCheckInView)
                || authority.PermissionCodes.Contains(PermissionCodes.EventCheckInManage)
            : authority.PermissionCodes.Contains(PermissionCodes.EventCheckInManage);
    }

    private async Task<bool> EvaluateManagePaidEventCommerceAccessAsync(
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (_machinePrincipalAccessor.IsMachineCaller || !HasExactlyOneOrganizerActor(resourceAttributes))
            return false;

        var currentUserId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!currentUserId.HasValue)
            return false;

        if (TryResolveGuidAttribute(resourceAttributes, "organizerUserId", out var organizerUserId))
            return organizerUserId == currentUserId.Value;

        if (TryResolveGuidAttribute(resourceAttributes, "organizerOrganizationId", out var organizerOrganizationId))
        {
            return await _organizationMemberRepository.HasPermissionInOrganization(
                organizerOrganizationId,
                currentUserId.Value,
                PermissionCodes.EventManageFinance);
        }

        return TryResolveGuidAttribute(resourceAttributes, "organizerGroupId", out var organizerGroupId)
            && await _groupMemberRepository.HasPermissionInGroup(
                organizerGroupId,
                currentUserId.Value,
                PermissionCodes.EventManageFinance);
    }

    private async Task<bool> IsVerifiedOrganizerControllerAsync(
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        var currentUserId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!currentUserId.HasValue)
        {
            return false;
        }

        if (TryResolveGuidAttribute(resourceAttributes, "organizerUserId", out var organizerUserId))
        {
            return organizerUserId == currentUserId.Value;
        }

        if (TryResolveGuidAttribute(resourceAttributes, "organizerOrganizationId", out var organizerOrganizationId))
        {
            return await _organizationMemberRepository.HasPermissionInOrganization(
                organizerOrganizationId,
                currentUserId.Value,
                PermissionCodes.EventCreate);
        }

        return TryResolveGuidAttribute(resourceAttributes, "organizerGroupId", out var organizerGroupId)
            && await _groupMemberRepository.HasPermissionInGroup(
                organizerGroupId,
                currentUserId.Value,
                PermissionCodes.EventCreate);
    }

    private static bool HasExactlyOneOrganizerActor(IDictionary<string, object>? resourceAttributes)
    {
        if (!TryResolveGuidAttribute(resourceAttributes, "organizerActorId", out _))
            return false;

        var count = 0;
        if (TryResolveGuidAttribute(resourceAttributes, "organizerUserId", out _)) count++;
        if (TryResolveGuidAttribute(resourceAttributes, "organizerOrganizationId", out _)) count++;
        if (TryResolveGuidAttribute(resourceAttributes, "organizerGroupId", out _)) count++;
        return count == 1;
    }

    private async Task<bool> EvaluateEventCreateAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(resourceAttributes);
        if (tenantId != _tenantContext.TenantId)
        {
            LogDecision("deny", "tenant_mismatch", resourceKind, action);
            return false;
        }

        var userId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!userId.HasValue)
        {
            LogDecision("deny", "missing_user_id", resourceKind, action);
            return false;
        }

        LogDecision("allow", "authenticated_pre_create_handler_policy", resourceKind, action);
        return true;
    }

    private async Task<bool> IsActorUserOwnerAsync(
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (!TryResolveGuidAttribute(resourceAttributes, "userId", out var ownerUserId))
            return false;

        var currentUserId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        return currentUserId == ownerUserId;
    }

    private async Task<bool> IsClaimantActorOwnerAsync(
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        var currentUserId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!currentUserId.HasValue)
        {
            return false;
        }

        if (TryResolveGuidAttribute(resourceAttributes, "claimantUserId", out var claimantUserId))
        {
            return claimantUserId == currentUserId.Value;
        }

        if (TryResolveGuidAttribute(resourceAttributes, "claimantOrganizationId", out var claimantOrganizationId))
        {
            return await _organizationMemberRepository.HasPermissionInOrganization(
                claimantOrganizationId,
                currentUserId.Value,
                PermissionCodes.EventCreate);
        }

        return TryResolveGuidAttribute(resourceAttributes, "claimantGroupId", out var claimantGroupId)
            && await _groupMemberRepository.HasPermissionInGroup(
                claimantGroupId,
                currentUserId.Value,
                PermissionCodes.EventCreate);
    }

    private async Task<bool> EvaluateEventRolePermissionAsync(
        string resourceKind,
        string resourceId,
        string action,
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var userId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!userId.HasValue)
        {
            LogDecision("deny", "missing_user_id", resourceKind, action);
            return false;
        }

        var snapshot = await _eventAuthoritySnapshotService.GetForUserAndEventsAsync(
            tenantId,
            userId.Value,
            [eventId],
            cancellationToken);

        if (snapshot is null)
        {
            LogDecision("deny", "event_role_snapshot_missing", resourceKind, action);
            return false;
        }

        var permissionCode = PermissionCodeFor(resourceKind, action);
        var allowed = snapshot.Events.TryGetValue(eventId, out var authority)
            && authority.PermissionCodes.Contains(permissionCode);

        LogDecision(
            allowed ? "allow" : "deny",
            allowed ? "event_role_permission=true" : "event_role_permission_missing",
            resourceKind,
            action);

        return allowed;
    }

    private async Task<bool> EvaluateStorageObjectAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        IAuthorizationFacts? facts,
        CancellationToken cancellationToken)
    {
        if (action == AuthorizationActions.StorageObjects.Create)
            return await CanCreateStorageUploadAsync(resourceId, facts, cancellationToken);

        if (action is AuthorizationActions.StorageObjects.Download
            or AuthorizationActions.StorageObjects.PresignedDownload)
        {
            if (await EvaluateTenantScopedAccessAsync("islamuevent_storage_object", resourceId, action, resourceAttributes, cancellationToken))
                return true;

            return CanReadStorageObjectContent(resourceId, action, resourceAttributes);
        }

        return await EvaluateTenantScopedAccessAsync("islamuevent_storage_object", resourceId, action, resourceAttributes, cancellationToken);
    }

    private async Task<bool> CanCreateStorageUploadAsync(
        string resourceId,
        IAuthorizationFacts? facts,
        CancellationToken cancellationToken)
    {
        if (facts is not StorageUploadIntentFacts storageFacts ||
            !storageFacts.IsOrganizationTenantUpload ||
            storageFacts.TenantId == Guid.Empty ||
            storageFacts.OwningResourceId == Guid.Empty ||
            storageFacts.OwningOrganizationId is not { } organizationId ||
            !string.Equals(resourceId, nameof(CreateStorageUploadSessionCommand), StringComparison.Ordinal))
        {
            LogDecision("deny", "storage_upload_intent_facts_missing", "islamuevent_storage_object", AuthorizationActions.StorageObjects.Create);
            return false;
        }

        var currentUserId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (currentUserId != storageFacts.SubjectUserId || storageFacts.TenantId != _tenantContext.TenantId)
        {
            LogDecision("deny", "storage_upload_subject_or_tenant_mismatch", "islamuevent_storage_object", AuthorizationActions.StorageObjects.Create);
            return false;
        }

        var allowed = await _adminContext.IsOrganizationAdminAsync(organizationId, cancellationToken);
        LogDecision(
            allowed ? "allow" : "deny",
            allowed ? "storage_upload_owner_admin" : "storage_upload_owner_admin_missing",
            "islamuevent_storage_object",
            AuthorizationActions.StorageObjects.Create);
        return allowed;
    }

    private bool CanReadStorageObjectContent(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes)
    {
        var visibility = GetAttribute(resourceAttributes, "visibility");
        var lifecycleState = GetAttribute(resourceAttributes, "lifecycleState");
        if (!string.Equals(lifecycleState, StorageObjectLifecycleStates.Active, StringComparison.Ordinal))
        {
            LogDecision("deny", "storage_object_not_active", "islamuevent_storage_object", action);
            return false;
        }

        if (visibility is StorageObjectVisibilities.PublicImage or StorageObjectVisibilities.AuthenticatedTenant)
        {
            LogDecision("allow", $"storage_visibility={visibility}", "islamuevent_storage_object", action);
            return true;
        }

        var createdBy = GetAttribute(resourceAttributes, "createdBy");
        var currentUserId = _adminContext.UserId?.ToString("D");
        var isOwner = !string.IsNullOrWhiteSpace(createdBy)
            && !string.IsNullOrWhiteSpace(currentUserId)
            && string.Equals(createdBy, currentUserId, StringComparison.OrdinalIgnoreCase);

        LogDecision(
            isOwner ? "allow" : "deny",
            isOwner ? "storage_private_owner" : "storage_private_owner_mismatch",
            "islamuevent_storage_object",
            action);
        return isOwner;
    }

    private static string? GetAttribute(IDictionary<string, object>? resourceAttributes, string name) =>
        resourceAttributes is not null && resourceAttributes.TryGetValue(name, out var value)
            ? value?.ToString()
            : null;

    private async Task<bool> EvaluateUserAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is "view" or "update" && _adminContext.UserId.HasValue
            && AttributeResolver.TryGetGuid(resourceId, out var targetUserId)
            && targetUserId == _adminContext.UserId.Value)
        {
            LogDecision("allow", "self_service", "islamuevent_user", action);
            return true;
        }

        var tenantId = ResolveTenantId(resourceAttributes);
        var isTenantAdmin = await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
        LogDecision(isTenantAdmin ? "allow" : "deny", $"is_tenant_admin={isTenantAdmin}", "islamuevent_user", action);
        return isTenantAdmin;
    }

    private async Task<bool> EvaluateViewableTenantResourceAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is "view")
            return true;

        return await EvaluateTenantScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken);
    }

    private async Task<bool> EvaluateViewableOrgResourceAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is "view")
            return true;

        return await EvaluateOrgScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken);
    }

    private async Task<bool> EvaluateGroupMemberAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is "view" or "create")
            return true;

        return await EvaluateOrgScopedAccessAsync("islamuevent_group_member", resourceId, action, resourceAttributes, cancellationToken);
    }

    private async Task<bool> EvaluateContactShareConsentAccessAsync(
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is not ("viewsharedcontacts" or "exportsharedcontacts"))
            return false;

        if (ResolveTenantId(resourceAttributes) != _tenantContext.TenantId)
            return false;

        return await EvaluateOrgScopedAccessAsync("islamuevent_event_contact_share_consent", resourceId, action, resourceAttributes, cancellationToken);
    }

    private async Task<bool> EvaluateActorAccessAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (action is "view")
            return true;

        return await EvaluateTenantScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken);
    }
}
