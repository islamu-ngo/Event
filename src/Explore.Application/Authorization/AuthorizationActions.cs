namespace Explore.Application.Authorization;

/// <summary>
/// Canonical catalog of authorization action strings used across the application.
/// All values match Cerbos policy action definitions exactly.
/// <para>
/// Top-level constants define shared action verbs reused across resource kinds.
/// Resource-scoped nested classes document which actions are valid for each resource kind,
/// providing IDE discoverability without requiring knowledge of Cerbos policy internals.
/// Standard CRUD resources reference top-level constants to stay DRY.
/// </para>
/// <para>
/// Phase 5 of the modernization plan may migrate these values to a <c>resource:verb</c> format
/// (e.g., <c>"islamuevent_event:view"</c>) alongside matching Cerbos policy changes.
/// Until then, values use simple verb strings for backward compatibility.
/// </para>
/// </summary>
public static class AuthorizationActions
{
    // ── Shared action verbs ─────────────────────────────────────────────

    /// <summary>View/read access to a resource.</summary>
    public const string View = "view";

    /// <summary>Create a new resource instance.</summary>
    public const string Create = "create";

    /// <summary>Update an existing resource.</summary>
    public const string Update = "update";

    /// <summary>Delete a resource (soft or hard delete).</summary>
    public const string Delete = "delete";

    /// <summary>Manage members of a group-like resource (organization, group).</summary>
    public const string ManageMembers = "manage_members";

    /// <summary>Lock a setting at a higher governance level.</summary>
    public const string Lock = "lock";

    /// <summary>Unlock a previously locked setting.</summary>
    public const string Unlock = "unlock";

    /// <summary>View shared contact information from consented registrants.</summary>
    public const string ViewSharedContacts = "viewsharedcontacts";

    /// <summary>Export shared contact information from consented registrants.</summary>
    public const string ExportSharedContacts = "exportsharedcontacts";

    /// <summary>Send a message into an AI assistant conversation.</summary>
    public const string SendMessage = "send_message";

    /// <summary>Confirm an AI-proposed action before executing a mutating tool.</summary>
    public const string ConfirmAction = "confirm_action";

    /// <summary>Reject an AI-proposed action without executing side effects.</summary>
    public const string RejectAction = "reject_action";

    /// <summary>Cancel an AI provider run that has not completed.</summary>
    public const string CancelRun = "cancel_run";

    /// <summary>Create an AI-proposed action without executing its side effects.</summary>
    public const string ProposeAction = "propose_action";

    // ── Resource-scoped action catalogs ─────────────────────────────────
    //
    // Each nested class documents the valid actions for its Cerbos resource kind.
    // Standard CRUD resources reference top-level constants.
    // Resources with restricted or extended action sets are explicit.

    /// <summary>Valid actions for the <c>islamuevent_event</c> resource kind.</summary>
    public static class Events
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
        public const string Publish = "publish";
        public const string ApprovePublish = "approve-publish";
        public const string ModerateLight = "moderate-light";
        public const string ModerateHeavy = "moderate-heavy";
        public const string Unmoderate = "unmoderate";
        public const string ViewManagement = "view-management";
        public const string ManageTeam = "manage-team";
        public const string ManageOwner = "manage-owner";
        public const string TransferOwnership = "transfer-ownership";
        public const string ManageFinance = "manage-finance";
        public const string ManagePublicActions = "manage-public-actions";
        public const string ManageRegistrationWorkflow = "manage-registration-workflow";
        public const string ManageRegistrationChannels = "manage-registration-channels";
        public const string ViewRegistrationProviderHealth = "view-registration-provider-health";
        public const string ClaimOrganizer = "claim-organizer";
        public const string WithdrawOrganizerClaim = "withdraw-organizer-claim";
        public const string ReviewOrganizerClaim = "review-organizer-claim";
        public const string ViewOrganizerClaims = "view-organizer-claims";
        public const string ManageRegistrations = "manage-registrations";
        public const string ManageTickets = "manage-tickets";
        public const string EventCheckInView = "event_check_in:view";
        public const string EventCheckInManage = "event_check_in:manage";
        public const string ManagePaidEventCommerce = "manage-paid-event-commerce";
        public const string ManageAttendees = "manage-attendees";
    }

    public static class RegistrationForms
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
        public const string Preflight = "preflight";
        public const string Publish = "publish";
        public const string ManageRequirements = "manage-requirements";
        public const string Attach = "attach";
        public const string Detach = "detach";
    }

    /// <summary>Valid actions for the <c>islamuevent_event_session</c> resource kind.</summary>
    public static class EventSessions
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_event_session_agenda_item</c> resource kind.</summary>
    public static class EventSessionAgendaItems
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_event_day</c> resource kind.</summary>
    public static class EventDays
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_event_agenda_item</c> resource kind.</summary>
    public static class EventAgendaItems
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_registration_order</c> resource kind.</summary>
    public static class RegistrationOrders
    {
        public const string View = AuthorizationActions.View;
        public const string Cancel = "cancel";
        public const string Continue = "continue";
        public const string Finalize = "finalize";
        public const string RequestRefund = "request_refund";
        public const string RespondMaterialChange = "respond_material_change";
    }

    public static class SupportAccessSessions
    {
        public const string View = AuthorizationActions.View;
        public const string List = "list";
        public const string Start = "start";
        public const string Stop = "stop";
        public const string ViewAudit = "view_audit";
        public const string ForceStop = "force_stop";
    }

    /// <summary>
    /// Valid actions for the <c>islamuevent_event_contact_share_consent</c> resource kind.
    /// Uses domain-specific actions instead of standard CRUD.
    /// </summary>
    public static class EventContactShareConsents
    {
        public const string ViewSharedContacts = AuthorizationActions.ViewSharedContacts;
        public const string ExportSharedContacts = AuthorizationActions.ExportSharedContacts;
    }

    /// <summary>
    /// Valid actions for the <c>islamuevent_organization</c> resource kind.
    /// Extends standard CRUD with member management.
    /// </summary>
    public static class Organizations
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
        public const string ManageMembers = AuthorizationActions.ManageMembers;
        public const string ViewEvidence = "view-evidence";
        public const string SubmitEvidence = "submit-evidence";
        public const string ReviewEvidence = "review-evidence";
    }

    /// <summary>
    /// Valid actions for the <c>islamuevent_organization_member</c> resource kind.
    /// Extends standard CRUD with member management.
    /// </summary>
    public static class OrganizationMembers
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
        public const string ManageMembers = AuthorizationActions.ManageMembers;
    }

    /// <summary>Valid actions for the <c>islamuevent_organization_review</c> resource kind.</summary>
    public static class OrganizationReviews
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>
    /// Valid actions for the <c>islamuevent_tenant</c> resource kind.
    /// Restricted to view and update only (no create/delete at resource level).
    /// </summary>
    public static class Tenants
    {
        public const string View = AuthorizationActions.View;
        public const string Update = AuthorizationActions.Update;

        /// <summary>
        /// Provisioning a tenant is a control-plane operation reserved to instance administrators.
        /// </summary>
        public const string Create = AuthorizationActions.Create;
    }

    /// <summary>
    /// Valid actions for the <c>islamuevent_tenant_setting</c> resource kind.
    /// No create action; settings are predefined.
    /// </summary>
    public static class TenantSettings
    {
        public const string View = AuthorizationActions.View;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_tenant_user_role_grant</c> resource kind.</summary>
    public static class TenantUserRoleGrants
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_actor_subscription</c> resource kind.</summary>
    public static class ActorSubscriptions
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_ai_conversation</c> resource kind.</summary>
    public static class AiConversations
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string SendMessage = AuthorizationActions.SendMessage;
        public const string ConfirmAction = AuthorizationActions.ConfirmAction;
        public const string RejectAction = AuthorizationActions.RejectAction;
        public const string CancelRun = AuthorizationActions.CancelRun;
        public const string ProposeAction = AuthorizationActions.ProposeAction;
    }

    /// <summary>Valid actions for the <c>islamuevent_category</c> resource kind.</summary>
    public static class Categories
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_tag</c> resource kind.</summary>
    public static class Tags
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_location</c> resource kind.</summary>
    public static class Locations
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
        public const string ManageCustomAddresses = "manage_custom_addresses";
        public const string CreateCustomAddress = "create_custom_address";
        public const string ApproveTenantAddress = "approve_tenant_address";
    }

    /// <summary>Valid actions for the <c>islamuevent_location_room</c> resource kind.</summary>
    public static class LocationRooms
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_storage_object</c> resource kind.</summary>
    public static class StorageObjects
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
        public const string Download = "download";
        public const string PresignedDownload = "presigned_download";
    }

    /// <summary>
    /// Valid actions for the <c>islamuevent_user</c> resource kind.
    /// No create action; users are provisioned through authentication.
    /// </summary>
    public static class Users
    {
        public const string View = AuthorizationActions.View;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_atproto_record</c> resource kind.</summary>
    public static class AtprotoRecords
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>Valid actions for the <c>islamuevent_indexed_did</c> resource kind.</summary>
    public static class IndexedDids
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>
    /// Valid actions for the <c>islamuevent_instance_setting</c> resource kind.
    /// Extends standard CRUD with governance lock/unlock semantics.
    /// </summary>
    public static class InstanceSettings
    {
        public const string View = AuthorizationActions.View;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
        public const string Lock = AuthorizationActions.Lock;
        public const string Unlock = AuthorizationActions.Unlock;
    }

    /// <summary>Diff a template against current definitions before applying.</summary>
    public const string SyncDiff = "sync_diff";

    /// <summary>Apply a template sync to update/create definitions.</summary>
    public const string SyncApply = "sync_apply";

    // ── EAV Custom Property resource-scoped catalogs ────────────────────

    /// <summary>
    /// Valid actions for the <c>custom_property_template</c> resource kind.
    /// Extends standard CRUD with template sync diff/apply operations.
    /// </summary>
    public static class CustomPropertyTemplates
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
        public const string SyncDiff = AuthorizationActions.SyncDiff;
        public const string SyncApply = AuthorizationActions.SyncApply;
    }

    /// <summary>
    /// Valid actions for the <c>custom_property_value</c> resource kind.
    /// Standard CRUD for runtime custom property values on events/sessions.
    /// </summary>
    public static class CustomPropertyValues
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

    /// <summary>
    /// Valid actions for the <c>custom_property_projection</c> resource kind.
    /// Covers projection rebuild, dirty-scope drain, and status inspection.
    /// Authorized through explicit custom-property projection resource metadata.
    /// </summary>
    public static class CustomPropertyProjections
    {
        public const string View = AuthorizationActions.View;
        public const string Update = AuthorizationActions.Update;
    }

    /// <summary>
    /// Valid actions for the <c>custom_property_governance</c> resource kind.
    /// Covers Rule 12 governance reporting and promotion recommendations.
    /// Authorized through explicit custom-property governance resource metadata.
    /// </summary>
    public static class CustomPropertyGovernance
    {
        public const string View = AuthorizationActions.View;
    }

    /// <summary>
    /// Valid actions for the <c>email_dispatch</c> resource kind.
    /// Covers operator status inspection and high-risk delivery control actions.
    /// </summary>
    public static class EmailDispatches
    {
        public const string View = AuthorizationActions.View;
        public const string ManageTenant = "manage_tenant";
        public const string Park = "park";
        public const string Replay = "replay";
        public const string Resolve = "resolve";
        public const string Reconcile = "reconcile";
    }

    public static class Webhooks
    {
        public const string View = "webhook:view";
        public const string Create = "webhook:create";
        public const string Update = "webhook:update";
        public const string Delete = "webhook:delete";
        public const string RotateSecret = "webhook:rotate-secret";
        public const string Test = "webhook:test";
        public const string Retry = "webhook:retry";
        public const string RedriveIncoming = "webhook:redrive-incoming";
        public const string Pause = "webhook:pause";
        public const string Resume = "webhook:resume";
        public const string ReconcilePublication = "webhook:reconcile-publication";
        public const string AbandonPublication = "webhook:abandon-publication";
        public const string ViewDelivery = "webhook:view-delivery";
        public const string ViewPayload = "webhook:view-payload";
        public const string BulkReplay = "webhook:bulk-replay";
        public const string ManageProvider = "webhook:manage-provider";
        public const string OpenProviderPortal = "webhook:open-provider-portal";
        public const string ProcessIncoming = "webhook:process-incoming";
    }

    /// <summary>
    /// Valid actions for the <c>platform_namespace</c> resource kind.
    /// Default deny — only instance admins (platform operators) can write.
    /// </summary>
    public static class PlatformNamespaces
    {
        public const string View = AuthorizationActions.View;
        public const string Create = AuthorizationActions.Create;
        public const string Update = AuthorizationActions.Update;
        public const string Delete = AuthorizationActions.Delete;
    }

}
