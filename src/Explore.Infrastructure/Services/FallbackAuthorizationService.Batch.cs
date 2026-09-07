using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Infrastructure.Services;

public partial class FallbackAuthorizationService
{
    public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken = default)
    {
        if (checks.Count == 0)
            return [];

        if (checks.Count <= 2 || _machinePrincipalAccessor.IsMachineCaller)
        {
            var smallResults = new AuthorizationDecision[checks.Count];
            for (var i = 0; i < checks.Count; i++)
            {
                smallResults[i] = await AuthorizeAsync(checks[i], cancellationToken);
            }

            return smallResults;
        }

        var profile = await ResolveAuthorityProfileAsync(cancellationToken);
        var eventAuthority = await ResolveBatchEventAuthorityAsync(profile, checks, cancellationToken);

        var results = new AuthorizationDecision[checks.Count];
        for (var i = 0; i < checks.Count; i++)
        {
            var check = checks[i];
            var attributes = TrustedAttributes(check);
            var allowed = EvaluateWithProfile(profile, eventAuthority, check.ResourceKind, check.ResourceId, check.Action, attributes, check.Facts);
            results[i] = allowed
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local);
        }

        return results;
    }

    private sealed record AuthorityProfile(
        bool IsInstanceAdmin,
        bool IsTenantAdmin,
        Guid TenantId,
        IReadOnlySet<Guid> AdminOrgIds,
        IReadOnlySet<Guid> AdminGroupIds,
        IReadOnlySet<Guid> EventCreateOrgIds,
        IReadOnlySet<Guid> EventCreateGroupIds,
        IReadOnlySet<Guid> EventFinanceOrgIds,
        IReadOnlySet<Guid> EventFinanceGroupIds,
        Guid? UserId);

    private async Task<AuthorityProfile> ResolveAuthorityProfileAsync(CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(cancellationToken);
        var tenantId = _tenantContext.TenantId;
        var isTenantAdmin = !isInstanceAdmin && await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
        var adminOrgIds = (await _adminContext.GetAdminOrganizationIdsAsync(cancellationToken) ?? []).ToHashSet();
        var adminGroupIds = (await _adminContext.GetAdminGroupIdsAsync(cancellationToken) ?? []).ToHashSet();

        var userId = _adminContext.UserId ?? await _adminContext.ResolveUserIdAsync(cancellationToken);
        var eventCreateOrgIds = userId.HasValue
            ? (await _organizationMemberRepository.GetOrganizationIdsWhereUserHasPermission(
                userId.Value,
                PermissionCodes.EventCreate,
                cancellationToken) ?? []).ToHashSet()
            : [];
        var eventCreateGroupIds = userId.HasValue
            ? (await _groupMemberRepository.GetGroupIdsWhereUserHasPermission(
                userId.Value,
                PermissionCodes.EventCreate,
                cancellationToken) ?? []).ToHashSet()
            : [];
        var eventFinanceOrgIds = userId.HasValue
            ? (await _organizationMemberRepository.GetOrganizationIdsWhereUserHasPermission(
                userId.Value,
                PermissionCodes.EventManageFinance,
                cancellationToken) ?? []).ToHashSet()
            : [];
        var eventFinanceGroupIds = userId.HasValue
            ? (await _groupMemberRepository.GetGroupIdsWhereUserHasPermission(
                userId.Value,
                PermissionCodes.EventManageFinance,
                cancellationToken) ?? []).ToHashSet()
            : [];
        return new AuthorityProfile(
            isInstanceAdmin,
            isTenantAdmin,
            tenantId,
            adminOrgIds,
            adminGroupIds,
            eventCreateOrgIds,
            eventCreateGroupIds,
            eventFinanceOrgIds,
            eventFinanceGroupIds,
            userId);
    }

    private bool EvaluateWithProfile(
        AuthorityProfile profile,
        EventAuthoritySnapshot? eventAuthority,
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        IAuthorizationFacts? facts)
    {
        if (IsConfigurationManifestExport(resourceKind, resourceId, action))
        {
            bool hasExportFact =
                resourceAttributes?.TryGetValue("configurationManifestExport", out object? exportFact) == true
                && exportFact is true;
            bool exportDecision = hasExportFact && profile.IsInstanceAdmin;
            LogDecision(
                exportDecision ? "allow" : "deny",
                exportDecision ? "configuration_manifest_export" : "configuration_manifest_export_authority_missing",
                resourceKind,
                action);
            return exportDecision;
        }

        if (!IsSupportedEventResourceAction(resourceKind, action))
        {
            LogDecision("deny", "unsupported_event_action", resourceKind, action);
            return false;
        }

        if (resourceKind == ResourceKinds.Event
            && action == AuthorizationActions.Events.ApprovePublish)
        {
            var approvalDecision = EvaluateApprovePublishWithProfile(
                profile, resourceId, resourceAttributes);
            LogDecision(
                approvalDecision ? "allow" : "deny",
                approvalDecision ? "publication_approval_admin" : "publication_approval_authority_missing",
                resourceKind,
                action);
            return approvalDecision;
        }

        if (profile.IsInstanceAdmin && IsInstanceAdminFallbackAllowed(resourceKind, action))
        {
            LogDecision("allow", "is_instance_admin", resourceKind, action);
            return true;
        }

        if (profile.IsInstanceAdmin && IsInstanceAdminFallbackDenied(resourceKind, action))
        {
            LogDecision("deny", "is_instance_admin_shortcut_denied", resourceKind, action);
            return false;
        }

        if (SafeMode && !profile.IsInstanceAdmin)
        {
            LogDecision("deny", "safe_mode_active", resourceKind, action);
            return false;
        }

        var decision = resourceKind switch
        {
            "islamuevent_instance_setting" => false,
            "islamuevent_tenant_setting" => EvaluateTenantSettingWithProfile(profile, resourceId, action, resourceAttributes),
            "islamuevent_tenant" => action is AuthorizationActions.View or AuthorizationActions.Update
                && EvaluateTenantScopedWithProfile(profile, resourceAttributes),
            "islamuevent_tenant_user_role_grant" or "islamuevent_category" or "islamuevent_tag" or "islamuevent_location" or "islamuevent_location_room"
                => EvaluateTenantScopedWithProfile(profile, resourceAttributes),
            "islamuevent_custom_property_definition" or "islamuevent_custom_property_template" or "islamuevent_actor"
                => action is "view" || EvaluateTenantScopedWithProfile(profile, resourceAttributes),
            "islamuevent_custom_property_value"
                => action is "view" || IsAdminForOrgScope(profile, resourceAttributes, resourceId),
            "islamuevent_custom_property_projection"
                => EvaluateCustomPropertyProjectionWithProfile(profile, action, resourceAttributes),
            "islamuevent_custom_property_governance" or "islamuevent_email_dispatch" or "islamuevent_webhook"
                => EvaluateTenantScopedWithProfile(profile, resourceAttributes),
            "islamuevent_support_access_session"
                => EvaluateSupportAccessSessionWithProfile(profile, action, resourceAttributes),
            "islamuevent_platform_namespace" => action is "view",
            "islamuevent_organization" when action is "create" && HasAuthorizationPhase(resourceAttributes, AuthorizationPhases.PreCreate)
                => IsOrganizationCreateAllowedForProfile(profile, resourceAttributes),
            "islamuevent_organization" => profile.IsTenantAdmin || IsOrgAdminFromProfile(profile, resourceAttributes, resourceId),
            "islamuevent_organization_member" => IsAdminForOrgScope(profile, resourceAttributes, resourceId),
            "islamuevent_organization_review" => action is "create" or "view" || IsAdminForOrgScope(profile, resourceAttributes, resourceId),
            "islamuevent_group" => action is "view" || IsAdminForOrgScope(profile, resourceAttributes, resourceId),
            "islamuevent_group_member" => action is "view" or "create" || IsAdminForOrgScope(profile, resourceAttributes, resourceId),
            "islamuevent_event" when action is "create" => IsEventCreateAllowedForProfile(profile, resourceAttributes),
            "islamuevent_event" when action is AuthorizationActions.Events.ManageRegistrations
                or AuthorizationActions.Events.ManageRegistrationWorkflow
                or AuthorizationActions.Events.ManageRegistrationChannels
                or AuthorizationActions.Events.ViewRegistrationProviderHealth
                => EvaluateManageRegistrationsWithProfile(profile, eventAuthority, resourceId, resourceAttributes),
            "islamuevent_registration_form"
                => EvaluateManageRegistrationsWithProfile(profile, eventAuthority, resourceId, resourceAttributes),
            "islamuevent_event" when action == AuthorizationActions.Events.ManageTickets
                => EvaluateManageTicketsWithProfile(profile, eventAuthority, resourceId, resourceAttributes),
            "islamuevent_event" when IsAdmissionCheckInAction(action)
                => EvaluateAdmissionCheckInWithProfile(
                    profile, eventAuthority, resourceId, action, resourceAttributes),
            "islamuevent_event" when action == AuthorizationActions.Events.ManagePaidEventCommerce
                => EvaluateManagePaidEventCommerceWithProfile(profile, resourceId, resourceAttributes),
            "islamuevent_event" or "islamuevent_event_session" or "islamuevent_event_session_group" or "islamuevent_event_session_agenda_item" or "islamuevent_event_day" or "islamuevent_event_agenda_item"
                => HasEventContextForProfile(profile, resourceKind, resourceId, resourceAttributes)
                    && (resourceKind == ResourceKinds.Event && IsEventModerationAction(action)
                        ? IsTenantAdminForResourceTenant(profile, resourceKind, resourceId, resourceAttributes)
                        : (IsTenantAdminForResourceTenant(profile, resourceKind, resourceId, resourceAttributes)
                            && (resourceKind != ResourceKinds.Event || IsTenantAdminEventAction(action)))
                            || IsOrgAdminFromProfile(profile, resourceAttributes, resourceId)
                             || IsActorUserOwnerFromProfile(profile, resourceAttributes)
                             || HasEventRolePermission(eventAuthority, resourceKind, resourceId, action, resourceAttributes)),
            "islamuevent_event_organizer_claim" => EvaluateEventOrganizerClaimWithProfile(
                profile,
                eventAuthority,
                resourceId,
                action,
                resourceAttributes),
            "islamuevent_event_contact_share_consent" => action is "viewsharedcontacts" or "exportsharedcontacts"
                && EvaluateTenantScopedWithProfile(profile, resourceAttributes)
                && IsAdminForOrgScope(profile, resourceAttributes, resourceId),
            "islamuevent_storage_object"
                => EvaluateStorageObjectWithProfile(profile, resourceId, action, resourceAttributes, facts),
            "islamuevent_user" => EvaluateUserWithProfile(profile, resourceId, action),
            "islamuevent_notification" => true,
            "islamuevent_actor_subscription" => true,
            "islamuevent_ai_conversation" => true,
            "islamuevent_atproto_record" or "islamuevent_indexed_did" => false,
            _ => false
        };

        LogDecision(decision ? "allow" : "deny", "fallback_batch_policy", resourceKind, action);
        return decision;
    }

    private bool EvaluateApprovePublishWithProfile(
        AuthorityProfile profile,
        string resourceId,
        IDictionary<string, object>? resourceAttributes)
    {
        return TryResolveEventContext(
                ResourceKinds.Event,
                resourceId,
                resourceAttributes,
                out var tenantId,
                out var eventId)
            && tenantId != Guid.Empty
            && eventId != Guid.Empty
            && tenantId == profile.TenantId
            && (profile.IsInstanceAdmin || !SafeMode && profile.IsTenantAdmin);
    }

    private static bool EvaluateTenantSettingWithProfile(
        AuthorityProfile profile,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes)
    {
        if (!IsTenantBrandingDocument(resourceAttributes)
            && resourceAttributes?.TryGetValue("isLockedByInstance", out var lockedObj) == true
            && lockedObj is true)
        {
            return false;
        }

        return profile.IsTenantAdmin;
    }

    private static bool EvaluateManageRegistrationsWithProfile(
        AuthorityProfile profile,
        EventAuthoritySnapshot? eventAuthority,
        string resourceId,
        IDictionary<string, object>? resourceAttributes)
    {
        if (!HasEventContextForProfile(profile, ResourceKinds.Event, resourceId, resourceAttributes))
        {
            return false;
        }

        if (IsVerifiedOrganizerControllerFromProfile(profile, resourceAttributes))
        {
            return true;
        }

        return !profile.IsInstanceAdmin
            && !profile.IsTenantAdmin
            && HasEventRolePermission(
                eventAuthority,
                ResourceKinds.Event,
                resourceId,
                AuthorizationActions.Events.ManageRegistrations,
                resourceAttributes);
    }

    private static bool EvaluateManageTicketsWithProfile(
        AuthorityProfile profile,
        EventAuthoritySnapshot? eventAuthority,
        string resourceId,
        IDictionary<string, object>? resourceAttributes) =>
        !profile.IsInstanceAdmin
        && !profile.IsTenantAdmin
        && HasEventContextForProfile(profile, ResourceKinds.Event, resourceId, resourceAttributes)
        && (IsVerifiedOrganizerControllerFromProfile(profile, resourceAttributes)
            || HasEventRolePermission(eventAuthority, ResourceKinds.Event, resourceId, AuthorizationActions.Events.ManageTickets, resourceAttributes));

    private static bool EvaluateAdmissionCheckInWithProfile(
        AuthorityProfile profile,
        EventAuthoritySnapshot? eventAuthority,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes)
    {
        if (profile.IsInstanceAdmin
            || profile.IsTenantAdmin
            || !HasEventContextForProfile(profile, ResourceKinds.Event, resourceId, resourceAttributes)
            || eventAuthority is null
            || !TryResolveEventContext(
                ResourceKinds.Event, resourceId, resourceAttributes, out _, out Guid eventId)
            || !eventAuthority.Events.TryGetValue(eventId, out EventAuthorityForUser? authority)
            || !HasAdmissionCheckInRole(authority))
        {
            return false;
        }

        return action == AuthorizationActions.Events.EventCheckInView
            ? authority.PermissionCodes.Contains(PermissionCodes.EventCheckInView)
                || authority.PermissionCodes.Contains(PermissionCodes.EventCheckInManage)
            : authority.PermissionCodes.Contains(PermissionCodes.EventCheckInManage);
    }

    private static bool EvaluateManagePaidEventCommerceWithProfile(
        AuthorityProfile profile,
        string resourceId,
        IDictionary<string, object>? resourceAttributes)
    {
        if (!HasEventContextForProfile(profile, ResourceKinds.Event, resourceId, resourceAttributes)
            || !HasExactlyOneOrganizerActor(resourceAttributes)
            || !profile.UserId.HasValue)
        {
            return false;
        }

        return TryResolveGuidAttribute(resourceAttributes, "organizerUserId", out var organizerUserId)
                && organizerUserId == profile.UserId.Value
            || TryResolveGuidAttribute(resourceAttributes, "organizerOrganizationId", out var organizerOrganizationId)
                && profile.EventFinanceOrgIds.Contains(organizerOrganizationId)
            || TryResolveGuidAttribute(resourceAttributes, "organizerGroupId", out var organizerGroupId)
                && profile.EventFinanceGroupIds.Contains(organizerGroupId);
    }

    private static bool IsVerifiedOrganizerControllerFromProfile(
        AuthorityProfile profile,
        IDictionary<string, object>? resourceAttributes)
    {
        return profile.UserId.HasValue
                && TryResolveGuidAttribute(resourceAttributes, "organizerUserId", out var organizerUserId)
                && organizerUserId == profile.UserId.Value
            || TryResolveGuidAttribute(resourceAttributes, "organizerOrganizationId", out var organizerOrganizationId)
                && profile.EventCreateOrgIds.Contains(organizerOrganizationId)
            || TryResolveGuidAttribute(resourceAttributes, "organizerGroupId", out var organizerGroupId)
                && profile.EventCreateGroupIds.Contains(organizerGroupId);
    }

    private static bool EvaluateEventOrganizerClaimWithProfile(
        AuthorityProfile profile,
        EventAuthoritySnapshot? eventAuthority,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes)
    {
        if (!HasEventContextForProfile(
                profile,
                ResourceKinds.EventOrganizerClaim,
                resourceId,
                resourceAttributes))
        {
            return false;
        }

        if (action == AuthorizationActions.Events.ClaimOrganizer)
        {
            return !profile.IsInstanceAdmin && profile.UserId.HasValue;
        }

        if (action == AuthorizationActions.Events.WithdrawOrganizerClaim)
        {
            return !profile.IsInstanceAdmin && IsClaimantActorOwnerFromProfile(profile, resourceAttributes);
        }

        if (profile.IsTenantAdmin && IsTenantAdminEventAction(action))
        {
            return true;
        }

        if (action == AuthorizationActions.Events.ReviewOrganizerClaim)
        {
            return false;
        }

        return IsOrgAdminFromProfile(profile, resourceAttributes, resourceId)
            || IsActorUserOwnerFromProfile(profile, resourceAttributes)
            || HasEventRolePermission(
                eventAuthority,
                ResourceKinds.EventOrganizerClaim,
                resourceId,
                action,
                resourceAttributes);
    }

    private static bool EvaluateUserWithProfile(AuthorityProfile profile, string resourceId, string action)
    {
        if (action is "view" or "update" && profile.UserId.HasValue
            && Guid.TryParse(resourceId, out var targetUserId)
            && targetUserId == profile.UserId.Value)
            return true;

        return profile.IsTenantAdmin;
    }

    private static bool EvaluateTenantScopedWithProfile(
        AuthorityProfile profile,
        IDictionary<string, object>? resourceAttributes)
    {
        if (resourceAttributes?.ContainsKey("tenantId") != true)
            return profile.IsTenantAdmin;

        return profile.IsTenantAdmin
            && TryResolveGuidAttribute(resourceAttributes, "tenantId", out var tenantId)
            && tenantId == profile.TenantId;
    }

    private static bool EvaluateCustomPropertyProjectionWithProfile(
        AuthorityProfile profile,
        string action,
        IDictionary<string, object>? resourceAttributes)
    {
        return action is AuthorizationActions.CustomPropertyProjections.View
                or AuthorizationActions.CustomPropertyProjections.Update
            && profile.IsTenantAdmin
            && TryResolveGuidAttribute(resourceAttributes, "tenantId", out var tenantId)
            && tenantId == profile.TenantId;
    }

    private static bool EvaluateSupportAccessSessionWithProfile(
        AuthorityProfile profile,
        string action,
        IDictionary<string, object>? resourceAttributes)
    {
        return action is AuthorizationActions.SupportAccessSessions.View
                or AuthorizationActions.SupportAccessSessions.List
                or AuthorizationActions.SupportAccessSessions.ViewAudit
            && profile.IsTenantAdmin
            && TryResolveGuidAttribute(resourceAttributes, "tenantId", out var tenantId)
            && tenantId == profile.TenantId;
    }

    private static bool EvaluateStorageObjectWithProfile(
        AuthorityProfile profile,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes,
        IAuthorizationFacts? facts)
    {
        if (action == AuthorizationActions.StorageObjects.Create)
            return CanCreateStorageUploadWithProfile(profile, resourceId, facts);

        if (action is AuthorizationActions.StorageObjects.Download
            or AuthorizationActions.StorageObjects.PresignedDownload)
        {
            return EvaluateTenantScopedWithProfile(profile, resourceAttributes)
                || CanReadStorageObjectContentWithProfile(profile, resourceId, resourceAttributes);
        }

        return EvaluateTenantScopedWithProfile(profile, resourceAttributes);
    }

    private static bool CanCreateStorageUploadWithProfile(
        AuthorityProfile profile,
        string resourceId,
        IAuthorizationFacts? facts)
    {
        if (facts is not StorageUploadIntentFacts storageFacts ||
            !string.Equals(resourceId, nameof(CreateStorageUploadSessionCommand), StringComparison.Ordinal) ||
            !storageFacts.IsOrganizationTenantUpload ||
            storageFacts.TenantId == Guid.Empty ||
            storageFacts.OwningResourceId == Guid.Empty ||
            storageFacts.OwningOrganizationId is not { } organizationId ||
            storageFacts.SubjectUserId != profile.UserId ||
            storageFacts.TenantId != profile.TenantId)
        {
            return false;
        }

        return profile.AdminOrgIds.Contains(organizationId);
    }

    private static bool CanReadStorageObjectContentWithProfile(
        AuthorityProfile profile,
        string resourceId,
        IDictionary<string, object>? resourceAttributes)
    {
        var visibility = GetAttribute(resourceAttributes, "visibility");
        var lifecycleState = GetAttribute(resourceAttributes, "lifecycleState");
        if (!string.Equals(lifecycleState, StorageObjectLifecycleStates.Active, StringComparison.Ordinal))
            return false;

        if (visibility is StorageObjectVisibilities.PublicImage or StorageObjectVisibilities.AuthenticatedTenant)
            return true;

        var createdBy = GetAttribute(resourceAttributes, "createdBy");
        var currentUserId = profile.UserId?.ToString("D");
        return !string.IsNullOrWhiteSpace(createdBy)
            && !string.IsNullOrWhiteSpace(currentUserId)
            && string.Equals(createdBy, currentUserId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsEventCreateAllowedForProfile(
        AuthorityProfile profile,
        IDictionary<string, object>? resourceAttributes)
    {
        var tenantId = ResolveTenantId(resourceAttributes);
        return tenantId == profile.TenantId && profile.UserId.HasValue;
    }

    private bool IsOrganizationCreateAllowedForProfile(
        AuthorityProfile profile,
        IDictionary<string, object>? resourceAttributes)
    {
        var tenantId = ResolveTenantId(resourceAttributes);
        return tenantId == profile.TenantId && profile.UserId.HasValue;
    }

    private static bool HasAuthorizationPhase(
        IDictionary<string, object>? resourceAttributes,
        string phase)
    {
        return resourceAttributes?.TryGetValue("authorizationPhase", out var value) == true
            && string.Equals(value?.ToString(), phase, StringComparison.Ordinal);
    }

    private static bool IsOrgAdminFromProfile(
        AuthorityProfile profile,
        IDictionary<string, object>? resourceAttributes,
        string resourceId)
    {
        var orgId = ResolveOrganizationId(resourceAttributes, resourceId);
        return orgId.HasValue && profile.AdminOrgIds.Contains(orgId.Value);
    }

    private static bool IsActorUserOwnerFromProfile(
        AuthorityProfile profile,
        IDictionary<string, object>? resourceAttributes)
    {
        return profile.UserId.HasValue
            && TryResolveGuidAttribute(resourceAttributes, "userId", out var ownerUserId)
            && ownerUserId == profile.UserId.Value;
    }

    private static bool IsClaimantActorOwnerFromProfile(
        AuthorityProfile profile,
        IDictionary<string, object>? resourceAttributes)
    {
        if (!profile.UserId.HasValue)
        {
            return false;
        }

        return TryResolveGuidAttribute(resourceAttributes, "claimantUserId", out var claimantUserId)
                && claimantUserId == profile.UserId.Value
            || TryResolveGuidAttribute(resourceAttributes, "claimantOrganizationId", out var claimantOrganizationId)
                && profile.EventCreateOrgIds.Contains(claimantOrganizationId)
            || TryResolveGuidAttribute(resourceAttributes, "claimantGroupId", out var claimantGroupId)
                && profile.EventCreateGroupIds.Contains(claimantGroupId);
    }

    private static bool IsAdminForOrgScope(
        AuthorityProfile profile,
        IDictionary<string, object>? resourceAttributes,
        string resourceId)
    {
        if (profile.IsTenantAdmin)
            return true;

        return IsOrgAdminFromProfile(profile, resourceAttributes, resourceId);
    }

    private static bool HasEventContextForProfile(
        AuthorityProfile profile,
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? resourceAttributes)
    {
        return TryResolveEventContext(resourceKind, resourceId, resourceAttributes, out var tenantId, out _)
            && tenantId == profile.TenantId;
    }

    private static bool IsTenantAdminForResourceTenant(
        AuthorityProfile profile,
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? resourceAttributes)
    {
        return profile.IsTenantAdmin
            && TryResolveEventContext(resourceKind, resourceId, resourceAttributes, out var tenantId, out _)
            && tenantId == profile.TenantId;
    }

    private async Task<EventAuthoritySnapshot?> ResolveBatchEventAuthorityAsync(
        AuthorityProfile profile,
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken)
    {
        if (!profile.UserId.HasValue)
            return null;

        var eventIds = checks
            .Where(check => IsEventScopedResourceKind(check.ResourceKind))
            .Select(check => TryResolveEventContext(
                check.ResourceKind,
                check.ResourceId,
                TrustedAttributes(check),
                out var tenantId,
                out var eventId)
                && tenantId == profile.TenantId
                    ? eventId
                    : Guid.Empty)
            .Where(eventId => eventId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (eventIds.Length == 0)
            return null;

        return await _eventAuthoritySnapshotService.GetForUserAndEventsAsync(
            profile.TenantId,
            profile.UserId.Value,
            eventIds,
            cancellationToken);
    }

    private static bool HasEventRolePermission(
        EventAuthoritySnapshot? eventAuthority,
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes)
    {
        if (eventAuthority is null)
            return false;

        return TryResolveEventContext(resourceKind, resourceId, resourceAttributes, out _, out var eventId)
            && eventAuthority.Events.TryGetValue(eventId, out var authority)
            && authority.PermissionCodes.Contains(PermissionCodeFor(resourceKind, action));
    }
}
