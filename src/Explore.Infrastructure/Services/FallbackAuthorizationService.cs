using System.Diagnostics;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Fallback authorization service that evaluates access decisions using database-driven admin checks
/// and settings lock semantics. Used when Cerbos PDP is not configured (e.g., development, ATProto/PDS-only).
/// Implements the same IAuthorizationProvider contract for seamless DI swapping.
/// </summary>
public partial class FallbackAuthorizationService : IAuthorizationProvider
{
    private readonly IAdminContext _adminContext;
    private readonly IMachinePrincipalAccessor _machinePrincipalAccessor;
    private readonly IEventAuthoritySnapshotService _eventAuthoritySnapshotService;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FallbackAuthorizationService> _logger;

    /// <summary>
    /// When true, only instance admin emergency access is allowed — all other requests are denied.
    /// Activated when a BYO-Cerbos tenant's PDP is unreachable and failure_mode is "closed".
    /// This prevents bypassing the tenant's potentially stricter policies via a more permissive fallback.
    /// Once activated, safe mode persists for this provider instance.
    /// </summary>
    public bool SafeMode { get; private set; }

    private bool _safeModeLogged;

    /// <summary>
    /// Transitions the service to safe mode. Only instance admins will be allowed.
    /// This is a one-way latch — safe mode cannot be deactivated programmatically.
    /// Logs at Critical level on first activation.
    /// </summary>
    public void ActivateSafeMode()
    {
        SafeMode = true;

        if (!_safeModeLogged)
        {
            _safeModeLogged = true;
            _logger.LogCritical(
                "Safe mode ACTIVATED. Only instance admin access is permitted. " +
                "Cause: BYO Cerbos PDP unreachable with failure_mode=closed. " +
                "Recreate the provider instance after the PDP recovers to deactivate safe mode.");
        }
    }

    public FallbackAuthorizationService(
        IAdminContext adminContext,
        IMachinePrincipalAccessor machinePrincipalAccessor,
        IEventAuthoritySnapshotService eventAuthoritySnapshotService,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository,
        IHierarchicalSettingsResolver resolver,
        ITenantContext tenantContext,
        ILogger<FallbackAuthorizationService> logger)
    {
        _adminContext = adminContext;
        _machinePrincipalAccessor = machinePrincipalAccessor;
        _eventAuthoritySnapshotService = eventAuthoritySnapshotService;
        _organizationMemberRepository = organizationMemberRepository;
        _groupMemberRepository = groupMemberRepository;
        _resolver = resolver;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<AuthorizationDecision> AuthorizeAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ResourceId))
            return AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local, AuthorizationDecisionReasonCodes.InvalidRequest);

        var resourceAttributes = TrustedAttributes(request);
        resourceAttributes = await AddTenantSettingLockAttributeAsync(request, resourceAttributes, cancellationToken);

        var allowed = await IsAllowedCoreAsync(
            request.ResourceKind,
            request.ResourceId,
            request.Action,
            resourceAttributes,
            cancellationToken,
            request.Facts);

        return allowed
            ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
            : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local);
    }

    /// <summary>
    /// Trusted policy inputs come only from the closed fact catalog. A request with no facts has no
    /// attributes at all, so every fact-dependent rule below denies rather than reading caller input.
    /// </summary>
    private static Dictionary<string, object>? TrustedAttributes(AuthorizationRequest request) =>
        AuthorizationFactAttributeProjection.ToAttributes(request.Facts);

    private async Task<Dictionary<string, object>?> AddTenantSettingLockAttributeAsync(
        AuthorizationRequest request,
        Dictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        if (request.ResourceKind != ResourceKinds.TenantSetting
            || resourceAttributes?.ContainsKey("isLockedByInstance") == true)
        {
            return resourceAttributes;
        }

        var tenantId = ResolveTenantId(resourceAttributes);
        var resolved = await _resolver.ResolveWithMetadataAsync(
            request.ResourceId,
            new SettingContext(tenantId),
            cancellationToken);

        if (resolved?.IsLocked != true)
        {
            return resourceAttributes;
        }

        resourceAttributes ??= [];
        resourceAttributes["isLockedByInstance"] = true;
        return resourceAttributes;
    }

    private async Task<bool> IsAllowedCoreAsync(
        string resourceKind,
        string resourceId,
        string action,
        IDictionary<string, object>? resourceAttributes = null,
        CancellationToken cancellationToken = default,
        IAuthorizationFacts? facts = null)
    {
        if (IsConfigurationManifestExport(resourceKind, resourceId, action))
        {
            bool hasExportFact =
                resourceAttributes?.TryGetValue("configurationManifestExport", out object? exportFact) == true
                && exportFact is true;
            bool isExportingInstanceAdmin =
                await _adminContext.IsInstanceAdminAsync(cancellationToken);
            bool exportDecision = hasExportFact && isExportingInstanceAdmin;
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
            var approvalDecision = await EvaluateApprovePublishAccessAsync(
                resourceId, resourceAttributes, cancellationToken);
            LogDecision(
                approvalDecision ? "allow" : "deny",
                approvalDecision ? "publication_approval_admin" : "publication_approval_authority_missing",
                resourceKind,
                action);
            return approvalDecision;
        }

        if (_machinePrincipalAccessor.IsMachineCaller)
        {
            bool machineDecision = await EvaluateMachineCallerAccessAsync(
                resourceKind, resourceId, action, resourceAttributes, cancellationToken);
            LogDecision(machineDecision ? "allow" : "deny", "machine_caller", resourceKind, action);
            return machineDecision;
        }

        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(cancellationToken);
        if (isInstanceAdmin
            && resourceKind == ResourceKinds.Event
            && action == AuthorizationActions.Events.ManageTickets)
        {
            LogDecision("deny", "event_management_requires_event_authority", resourceKind, action);
            return false;
        }

        if (isInstanceAdmin && IsInstanceAdminFallbackAllowed(resourceKind, action))
        {
            LogDecision("allow", "is_instance_admin", resourceKind, action);
            return true;
        }

        if (isInstanceAdmin && IsInstanceAdminFallbackDenied(resourceKind, action))
        {
            LogDecision("deny", "is_instance_admin_shortcut_denied", resourceKind, action);
            return false;
        }

        // Safe-Mode: only instance admin allowed (bypassed above) — deny everything else.
        // Activated when a BYO-Cerbos tenant's PDP is unreachable with failure_mode=closed.
        if (SafeMode && !isInstanceAdmin)
        {
            LogDecision("deny", "safe_mode_active", resourceKind, action);
            return false;
        }

        var decision = resourceKind switch
        {
            // Instance-scoped: only instance admins (bypassed above)
            "islamuevent_instance_setting" => false,

            // Tenant-scoped: tenant admin with lock semantics
            "islamuevent_tenant_setting" => await EvaluateTenantSettingAccessAsync(resourceId, action, resourceAttributes, cancellationToken),

            "islamuevent_tenant" => await EvaluateTenantResourceAccessAsync(resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_tenant_user_role_grant" => await EvaluateTenantScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_category" => await EvaluateTenantScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_tag" => await EvaluateTenantScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_location" => await EvaluateTenantScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_location_room" => await EvaluateTenantScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_custom_property_definition" => await EvaluateViewableTenantResourceAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_custom_property_template" => await EvaluateViewableTenantResourceAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_custom_property_value" => await EvaluateViewableOrgResourceAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_custom_property_projection" => await EvaluateCustomPropertyProjectionAccessAsync(resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_custom_property_governance" => await EvaluateTenantScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_email_dispatch" => await EvaluateTenantScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_webhook" => await EvaluateWebhookAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_support_access_session" => await EvaluateSupportAccessSessionResourceAsync(action, resourceAttributes, cancellationToken),
            "islamuevent_platform_namespace" => action is "view",

            // Org-scoped: tenant admin or org admin
            "islamuevent_organization" => await EvaluateOrganizationAccessAsync(resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_organization_member" => await EvaluateOrgScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_organization_review" => await EvaluateOrgReviewAccessAsync(resourceId, action, resourceAttributes, cancellationToken),

            // Group resources: org-scoped (tenant admin or org admin), view open
            "islamuevent_group" => await EvaluateViewableOrgResourceAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_group_member" => await EvaluateGroupMemberAccessAsync(resourceId, action, resourceAttributes, cancellationToken),

            // Event resources require explicit tenant/event context before inherited authority checks.
            "islamuevent_event" => await EvaluateEventScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_event_session" => await EvaluateEventScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_event_session_group" => await EvaluateEventScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_event_session_agenda_item" => await EvaluateEventScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_event_day" => await EvaluateEventScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_event_agenda_item" => await EvaluateEventScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_event_organizer_claim" => await EvaluateEventScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),
            "islamuevent_registration_form" => await EvaluateEventScopedAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),

            "islamuevent_registration_order" => await EvaluateRegistrationOrderAccessAsync(resourceId, action, resourceAttributes, cancellationToken),

            // Contact share consent: tenant/org admin can view and export shared contacts
            "islamuevent_event_contact_share_consent" => await EvaluateContactShareConsentAccessAsync(resourceId, action, resourceAttributes, cancellationToken),

            // Storage: all authenticated can create, tenant admin for full management
            "islamuevent_storage_object" => await EvaluateStorageObjectAccessAsync(resourceId, action, resourceAttributes, facts, cancellationToken),

            // User management: instance admin or tenant admin, or self-update
            "islamuevent_user" => await EvaluateUserAccessAsync(resourceId, action, resourceAttributes, cancellationToken),

            // Notification: personal data, all authenticated can manage own notifications
            "islamuevent_notification" => true,

            // Actor subscriptions: authenticated users reach handlers; handlers enforce current-user ownership.
            "islamuevent_actor_subscription" => true,

            // AI conversations: authenticated users reach handlers; handlers enforce owner and tenant isolation.
            "islamuevent_ai_conversation" => true,

            // Actor: read-only for all authenticated; writes require tenant admin
            "islamuevent_actor" => await EvaluateActorAccessAsync(resourceKind, resourceId, action, resourceAttributes, cancellationToken),

            // ATProto federation: instance admin only for writes (bypassed above)
            "islamuevent_atproto_record" => false,
            "islamuevent_indexed_did" => false,

            _ => await EvaluateDefaultAccessAsync(resourceKind, action, resourceAttributes, cancellationToken)
        };

        LogDecision(decision ? "allow" : "deny", "fallback_policy", resourceKind, action);
        return decision;
    }

    private Task<bool> EvaluateDefaultAccessAsync(
        string resourceKind,
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        // For unknown resource kinds, deny by default (secure by default)
        LogDecision("deny", "unknown_resource_kind", resourceKind, action);
        return Task.FromResult(false);
    }

    private async Task<bool> EvaluateSupportAccessSessionResourceAsync(
        string action,
        IDictionary<string, object>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(resourceAttributes);
        if (tenantId == Guid.Empty)
            return false;

        return action is
                AuthorizationActions.SupportAccessSessions.View
                or AuthorizationActions.SupportAccessSessions.List
                or AuthorizationActions.SupportAccessSessions.ViewAudit
            && await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
    }

    /// <summary>
    /// Resolves the tenant ID from resource attributes, falling back to the current tenant context.
    /// </summary>
    private Guid ResolveTenantId(IDictionary<string, object>? resourceAttributes)
    {
        if (resourceAttributes?.TryGetValue("tenantId", out var tenantIdObj) == true)
        {
            if (tenantIdObj is Guid tid) return tid;
            if (tenantIdObj is string s && Guid.TryParse(s, out var parsed)) return parsed;
        }

        return _tenantContext.TenantId;
    }

    /// <summary>
    /// Resolves the organization ID from resource attributes or resource ID.
    /// Returns null if no organization ID can be determined.
    /// </summary>
    private static Guid? ResolveOrganizationId(IDictionary<string, object>? resourceAttributes, string resourceId)
    {
        if (resourceAttributes?.TryGetValue("organizationId", out var orgIdObj) == true)
        {
            if (orgIdObj is Guid oid) return oid;
            if (orgIdObj is string s && Guid.TryParse(s, out var parsed)) return parsed;
        }

        return Guid.TryParse(resourceId, out var fromId) ? fromId : null;
    }

    private static bool HasRequiredEventContext(
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? resourceAttributes) =>
        TryResolveEventContext(resourceKind, resourceId, resourceAttributes, out _, out _);

    private static bool IsEventScopedResourceKind(string resourceKind) =>
        resourceKind is "islamuevent_event"
            or "islamuevent_event_session"
            or "islamuevent_event_session_group"
            or "islamuevent_event_session_agenda_item"
            or "islamuevent_event_day"
            or "islamuevent_event_agenda_item"
            or "islamuevent_event_organizer_claim"
            or "islamuevent_registration_form";

    private static bool RequiresDirectEventAuthority(string resourceKind, string action) =>
        resourceKind == ResourceKinds.RegistrationForm
            ? IsRegistrationFormAction(action)
            : resourceKind is ResourceKinds.Event or ResourceKinds.EventOrganizerClaim &&
              action is AuthorizationActions.Update
            or AuthorizationActions.Delete
            or AuthorizationActions.Events.Publish
            or AuthorizationActions.Events.ManageTeam
            or AuthorizationActions.Events.ManageOwner
            or AuthorizationActions.Events.TransferOwnership
            or AuthorizationActions.Events.ManageFinance
            or AuthorizationActions.Events.ManagePublicActions
            or AuthorizationActions.Events.ManageRegistrations
            or AuthorizationActions.Events.ManageRegistrationWorkflow
            or AuthorizationActions.Events.ManageRegistrationChannels
            or AuthorizationActions.Events.ViewRegistrationProviderHealth
            or AuthorizationActions.Events.ManageTickets
            or AuthorizationActions.Events.ManagePaidEventCommerce
            or AuthorizationActions.Events.ClaimOrganizer
            or AuthorizationActions.Events.WithdrawOrganizerClaim
            or AuthorizationActions.Events.ViewOrganizerClaims
            or AuthorizationActions.Events.ReviewOrganizerClaim;

    private static bool IsConfigurationManifestExport(
        string resourceKind,
        string resourceId,
        string action) =>
        resourceKind == ResourceKinds.InstanceSetting
        && action == AuthorizationActions.InstanceSettings.View
        && string.Equals(
            resourceId,
            "instance.configuration-manifest.export",
            StringComparison.Ordinal);

    private static bool IsInstanceAdminFallbackAllowed(string resourceKind, string action) => resourceKind switch
    {
        ResourceKinds.InstanceSetting => action is AuthorizationActions.InstanceSettings.View
            or AuthorizationActions.InstanceSettings.Update
            or AuthorizationActions.InstanceSettings.Delete
            or AuthorizationActions.InstanceSettings.Lock
            or AuthorizationActions.InstanceSettings.Unlock,
        // Provisioning a tenant has no tenant row to weigh yet, so instance authority is the only thing that
        // can decide it. Cerbos grants instance admins every tenant action; omitting create here would make
        // the local evaluator deny an operation the PDP allows.
        ResourceKinds.Tenant => action is AuthorizationActions.Tenants.View
            or AuthorizationActions.Tenants.Update
            or AuthorizationActions.Tenants.Create,
        ResourceKinds.TenantUserRoleGrant => action is AuthorizationActions.TenantUserRoleGrants.View
            or AuthorizationActions.TenantUserRoleGrants.Create
            or AuthorizationActions.TenantUserRoleGrants.Delete,
        ResourceKinds.User => action is AuthorizationActions.Users.View
            or AuthorizationActions.Users.Update
            or AuthorizationActions.Users.Delete,
        ResourceKinds.AtprotoRecord or ResourceKinds.IndexedDid => action is AuthorizationActions.View
            or AuthorizationActions.Create
            or AuthorizationActions.Update
            or AuthorizationActions.Delete,
        ResourceKinds.PlatformNamespace => action is AuthorizationActions.View
            or AuthorizationActions.Create
            or AuthorizationActions.Update
            or AuthorizationActions.Delete,
        ResourceKinds.EmailDispatch => action is AuthorizationActions.EmailDispatches.View
            or AuthorizationActions.EmailDispatches.ManageTenant
            or AuthorizationActions.EmailDispatches.Park
            or AuthorizationActions.EmailDispatches.Replay
            or AuthorizationActions.EmailDispatches.Resolve
            or AuthorizationActions.EmailDispatches.Reconcile,
        ResourceKinds.Event => action is AuthorizationActions.Events.ViewManagement
            or AuthorizationActions.Events.ModerateLight
            or AuthorizationActions.Events.ModerateHeavy
            or AuthorizationActions.Events.Unmoderate,
        ResourceKinds.Location => action is AuthorizationActions.Locations.ManageCustomAddresses
            or AuthorizationActions.Locations.ApproveTenantAddress,
        ResourceKinds.Organization => action is AuthorizationActions.Locations.CreateCustomAddress,
        ResourceKinds.SupportAccessSession => action is AuthorizationActions.SupportAccessSessions.View
            or AuthorizationActions.SupportAccessSessions.List
            or AuthorizationActions.SupportAccessSessions.Start
            or AuthorizationActions.SupportAccessSessions.Stop
            or AuthorizationActions.SupportAccessSessions.ViewAudit
            or AuthorizationActions.SupportAccessSessions.ForceStop,
        ResourceKinds.Webhook => action is AuthorizationActions.Webhooks.View
            or AuthorizationActions.Webhooks.Create
            or AuthorizationActions.Webhooks.Update
            or AuthorizationActions.Webhooks.Delete
            or AuthorizationActions.Webhooks.RotateSecret
            or AuthorizationActions.Webhooks.Test
            or AuthorizationActions.Webhooks.Retry
            or AuthorizationActions.Webhooks.Pause
            or AuthorizationActions.Webhooks.Resume
            or AuthorizationActions.Webhooks.ReconcilePublication
            or AuthorizationActions.Webhooks.AbandonPublication
            or AuthorizationActions.Webhooks.ViewDelivery
            or AuthorizationActions.Webhooks.ViewPayload
            or AuthorizationActions.Webhooks.BulkReplay
            or AuthorizationActions.Webhooks.ManageProvider
            or AuthorizationActions.Webhooks.OpenProviderPortal,
        _ => false
    };

    private static bool IsInstanceAdminFallbackDenied(string resourceKind, string action) => resourceKind switch
    {
        ResourceKinds.AiConversation => true,
        ResourceKinds.Notification => true,
        ResourceKinds.ActorSubscription => true,
        _ => false
    };

    private static bool IsOrganizerClaimAction(string action) =>
        action is AuthorizationActions.Events.ClaimOrganizer
            or AuthorizationActions.Events.WithdrawOrganizerClaim
            or AuthorizationActions.Events.ViewOrganizerClaims
            or AuthorizationActions.Events.ReviewOrganizerClaim;

    private static bool IsSupportedEventResourceAction(string resourceKind, string action) => resourceKind switch
    {
        ResourceKinds.Event => IsEventAction(action),
        ResourceKinds.EventOrganizerClaim => IsOrganizerClaimAction(action),
        ResourceKinds.RegistrationForm => IsRegistrationFormAction(action),
        _ => true
    };

    private static bool IsRegistrationFormAction(string action) =>
        action is AuthorizationActions.RegistrationForms.View
            or AuthorizationActions.RegistrationForms.Create
            or AuthorizationActions.RegistrationForms.Update
            or AuthorizationActions.RegistrationForms.Delete
            or AuthorizationActions.RegistrationForms.Preflight
            or AuthorizationActions.RegistrationForms.Publish
            or AuthorizationActions.RegistrationForms.ManageRequirements
            or AuthorizationActions.RegistrationForms.Attach
            or AuthorizationActions.RegistrationForms.Detach;

    private static bool IsEventAction(string action) =>
        action is AuthorizationActions.View
            or AuthorizationActions.Create
            or AuthorizationActions.Update
            or AuthorizationActions.Delete
            or AuthorizationActions.Events.Publish
            or AuthorizationActions.Events.ApprovePublish
            or AuthorizationActions.Events.ViewManagement
            or AuthorizationActions.Events.ModerateLight
            or AuthorizationActions.Events.ModerateHeavy
            or AuthorizationActions.Events.Unmoderate
            or AuthorizationActions.Events.ManageTeam
            or AuthorizationActions.Events.ManageOwner
            or AuthorizationActions.Events.TransferOwnership
            or AuthorizationActions.Events.ManageFinance
            or AuthorizationActions.Events.ManagePublicActions
            or AuthorizationActions.Events.ManageRegistrations
            or AuthorizationActions.Events.ManageRegistrationWorkflow
            or AuthorizationActions.Events.ManageRegistrationChannels
            or AuthorizationActions.Events.ViewRegistrationProviderHealth
            or AuthorizationActions.Events.ManageTickets
            or AuthorizationActions.Events.EventCheckInView
            or AuthorizationActions.Events.EventCheckInManage
            or AuthorizationActions.Events.ManagePaidEventCommerce;

    private static bool IsTenantAdminEventAction(string action) =>
        action is AuthorizationActions.View
            or AuthorizationActions.Events.ViewManagement
            or AuthorizationActions.Events.ModerateLight
            or AuthorizationActions.Events.ModerateHeavy
            or AuthorizationActions.Events.Unmoderate
            or AuthorizationActions.Events.ManagePublicActions
            or AuthorizationActions.Events.ViewOrganizerClaims
            or AuthorizationActions.Events.ReviewOrganizerClaim;

    private static bool IsAdmissionCheckInAction(string action) =>
        action is AuthorizationActions.Events.EventCheckInView
            or AuthorizationActions.Events.EventCheckInManage;

    private static bool HasAdmissionCheckInRole(EventAuthorityForUser authority) =>
        authority.RoleCodes.Contains("event.owner")
        || authority.RoleCodes.Contains("event.manager")
        || authority.RoleCodes.Contains("event.check_in_staff");

    private static bool IsEventModerationAction(string action) =>
        action is AuthorizationActions.Events.ModerateLight
            or AuthorizationActions.Events.ModerateHeavy
            or AuthorizationActions.Events.Unmoderate;

    private static string PermissionCodeFor(string resourceKind, string action)
    {
        const string productNamespacePrefix = "islamuevent_";

        var permissionResourceKind = resourceKind == ResourceKinds.EventOrganizerClaim
            ? "event"
            : resourceKind.StartsWith(productNamespacePrefix, StringComparison.Ordinal)
                ? resourceKind[productNamespacePrefix.Length..]
                : resourceKind;
        if (resourceKind is ResourceKinds.Event or ResourceKinds.RegistrationForm &&
            action is AuthorizationActions.Events.ManageRegistrations
                or AuthorizationActions.Events.ManageRegistrationWorkflow
                or AuthorizationActions.Events.ManageRegistrationChannels
                or AuthorizationActions.Events.ViewRegistrationProviderHealth
                or AuthorizationActions.RegistrationForms.View
                or AuthorizationActions.RegistrationForms.Create
                or AuthorizationActions.RegistrationForms.Update
                or AuthorizationActions.RegistrationForms.Delete
                or AuthorizationActions.RegistrationForms.Preflight
                or AuthorizationActions.RegistrationForms.Publish
                or AuthorizationActions.RegistrationForms.ManageRequirements
                or AuthorizationActions.RegistrationForms.Attach
                or AuthorizationActions.RegistrationForms.Detach)
            return PermissionCodes.EventRegistrationManage;
        if (resourceKind == ResourceKinds.Event && action == AuthorizationActions.Events.ManageTickets)
            return PermissionCodes.EventManageTickets;
        if (resourceKind == ResourceKinds.Event && action == AuthorizationActions.Events.EventCheckInView)
            return PermissionCodes.EventCheckInView;
        if (resourceKind == ResourceKinds.Event && action == AuthorizationActions.Events.EventCheckInManage)
            return PermissionCodes.EventCheckInManage;
        if (resourceKind == ResourceKinds.Event && action == AuthorizationActions.Events.ManagePaidEventCommerce)
            return PermissionCodes.EventManageFinance;

        var permissionAction = resourceKind == ResourceKinds.Event && action == AuthorizationActions.Events.ViewManagement
            ? AuthorizationActions.View
            : action;

        return string.Concat(permissionResourceKind, ":", permissionAction);
    }

    private static bool TryResolveEventContext(
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? resourceAttributes,
        out Guid tenantId,
        out Guid eventId)
    {
        tenantId = Guid.Empty;
        eventId = Guid.Empty;

        if (!TryResolveGuidAttribute(resourceAttributes, "tenantId", out tenantId))
            return false;

        if (TryResolveGuidAttribute(resourceAttributes, "eventId", out eventId))
            return true;

        return resourceKind == "islamuevent_event" && Guid.TryParse(resourceId, out eventId);
    }

    private static bool TryResolveGuidAttribute(
        IDictionary<string, object>? resourceAttributes,
        string attributeName,
        out Guid value)
    {
        value = Guid.Empty;

        if (resourceAttributes?.TryGetValue(attributeName, out var attributeValue) != true)
            return false;

        if (attributeValue is Guid guidValue)
        {
            value = guidValue;
            return true;
        }

        return attributeValue is string stringValue && Guid.TryParse(stringValue, out value);
    }

    private void LogDecision(
        string decision,
        string reason,
        string resourceKind,
        string action)
    {
        var correlationId = Activity.Current?.Id ?? string.Empty;

        // Log level depends on decision: allow→Debug (high volume), deny→Warning (actionable)
        if (decision == "allow")
        {
            _logger.LogDebug(
                "Fallback authorization decision: {Decision} reason={Reason} resource={ResourceKind} action={Action} correlationId={CorrelationId}",
                decision, reason, resourceKind, action, correlationId);
        }
        else
        {
            _logger.LogWarning(
                "Fallback authorization decision: {Decision} reason={Reason} resource={ResourceKind} action={Action} correlationId={CorrelationId}",
                decision, reason, resourceKind, action, correlationId);
        }
    }
}
