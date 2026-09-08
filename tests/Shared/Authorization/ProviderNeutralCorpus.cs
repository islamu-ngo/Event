using Explore.Application.Authorization;

namespace Explore.Authorization.ParityCorpus;

/// <summary>
/// The authority a scenario's caller holds, described without reference to either provider.
/// <para>
/// The Local evaluator materializes this through <c>IAdminContext</c> and the membership repositories;
/// Cerbos materializes it through principal attributes. Describing the subject once is what makes a
/// disagreement between the two providers observable instead of hidden behind two different setups.
/// </para>
/// </summary>
public enum ParitySubject
{
    /// <summary>No authenticated subject at all.</summary>
    Anonymous,

    /// <summary>Authenticated, holding no administrative authority anywhere.</summary>
    StandardUser,

    /// <summary>Administrator of <see cref="ParityCorpus.TenantId"/>.</summary>
    TenantAdmin,

    /// <summary>Administrator of <see cref="ParityCorpus.OrganizationId"/> only.</summary>
    OrganizationAdmin,

    /// <summary>Owner of <see cref="ParityCorpus.EventId"/> through an event-scoped role.</summary>
    EventOwner,

    /// <summary>Manager of <see cref="ParityCorpus.EventId"/> through an event-scoped role.</summary>
    EventManager,

    /// <summary>Instance administrator.</summary>
    InstanceAdmin,

    /// <summary>A machine principal acting through an API key.</summary>
    MachineCaller,

    /// <summary>Event owner assignment without an admission permission.</summary>
    EventOwnerWithoutAdmissionPermission,

    /// <summary>Check-in staff assignment with view-only admission permission.</summary>
    AdmissionViewer,

    /// <summary>Check-in staff assignment with admission management permission.</summary>
    AdmissionManager,
}

/// <summary>
/// Which providers evaluate a scenario. Most scenarios run on both; a few describe adapter or runtime
/// behaviour that never reaches a policy engine and therefore has no Cerbos counterpart.
/// </summary>
[Flags]
public enum ParityLane
{
    /// <summary>
    /// Neither provider evaluates this scenario. Reserved for categories the Phase 0 corpus requires but
    /// which are not policy questions — the scenario still documents the requirement and points at the
    /// suite that does verify it, so the category cannot quietly disappear.
    /// </summary>
    None = 0,
    Local = 1,
    Cerbos = 2,
    Both = Local | Cerbos,
}

/// <summary>
/// One provider-neutral authorization question and the answer both providers must give.
/// </summary>
/// <param name="Id">Stable identifier used in diagnostics and evidence artifacts.</param>
/// <param name="Category">Phase 0 corpus category this scenario covers.</param>
/// <param name="Subject">The authority the caller holds.</param>
/// <param name="ResourceKind">Resource kind from <see cref="ResourceKinds"/>.</param>
/// <param name="Action">Action from <see cref="AuthorizationActions"/>.</param>
/// <param name="Facts">The trusted facts a resolver would have produced, or <c>null</c> for none.</param>
/// <param name="ExpectedAllowed">The decision both lanes must reach.</param>
/// <param name="Lanes">Which providers evaluate this scenario.</param>
/// <param name="Rationale">Why the expected outcome is correct. Required, so a future edit that flips an
/// expectation has to argue with a stated reason rather than silently rewrite a boolean.</param>
public sealed record ParityScenario(
    string Id,
    string Category,
    ParitySubject Subject,
    string ResourceKind,
    string Action,
    IAuthorizationFacts? Facts,
    bool ExpectedAllowed,
    ParityLane Lanes,
    string Rationale)
{
    public string Capability => $"{ResourceKind}:{Action}";
}

/// <summary>
/// The shared Phase 0 corpus. Identifiers are fixed so both lanes describe the same world, and every
/// category named in the Phase 0 plan is represented.
/// </summary>
public static class ParityCorpus
{
    public const string ApprovePublishAction = "approve-publish";

    public static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid OtherTenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid OtherOrganizationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid EventId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid ActorId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public static readonly Guid StorageObjectId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid SupportSessionId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    /// <summary>Every Phase 0 category the corpus is required to cover.</summary>
    public static readonly IReadOnlyList<string> RequiredCategories =
    [
        "normal-allow",
        "normal-deny",
        "administrator",
        "machine-caller",
        "support-session",
        "missing-subject",
        "missing-tenant",
        "wrong-tenant",
        "missing-resource",
        "consent-contact-sharing",
        "guest-capability",
        "public-visibility",
        "provider-failure",
        "hal-suppression",
    ];

    /// <summary>
    /// Event facts for an event owned by <see cref="OrganizationId"/> inside <see cref="TenantId"/>.
    /// </summary>
    public static EventAuthorizationFacts OrganizationOwnedEvent(
        Guid? tenantId = null,
        Guid? organizationId = null) => new(
        tenantId ?? TenantId,
        EventId,
        ActorId,
        UserId: null,
        OrganizationId: organizationId ?? OrganizationId,
        GroupId: null,
        OrganizerActorId: null,
        OrganizerUserId: null,
        OrganizerOrganizationId: organizationId ?? OrganizationId,
        OrganizerGroupId: null,
        ProvenanceType: null,
        SubmittedByUserId: null);

    /// <summary>Event facts for an event a specific user personally owns.</summary>
    public static EventScopedAuthorizationFacts AdmissionEvent() => new(TenantId, EventId);

    public static EventAuthorizationFacts UserOwnedEvent(Guid ownerUserId) => new(
        TenantId,
        EventId,
        ActorId,
        UserId: ownerUserId,
        OrganizationId: null,
        GroupId: null,
        OrganizerActorId: ActorId,
        OrganizerUserId: ownerUserId,
        OrganizerOrganizationId: null,
        OrganizerGroupId: null,
        ProvenanceType: null,
        SubmittedByUserId: null);

    public static IReadOnlyList<ParityScenario> Scenarios { get; } =
    [
        // ---- normal allow / deny -------------------------------------------------------------------
        new(
            Id: "tenant-admin-views-own-tenant-event",
            Category: "normal-allow",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.View,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "A tenant administrator may read any event inside the tenant they administer."),

        new(
            Id: "standard-user-cannot-update-foreign-event",
            Category: "normal-deny",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Update,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Authoring an event requires authority over it; bare authentication grants none."),

        // ---- administrator -------------------------------------------------------------------------
        new(
            Id: "instance-admin-moderates-event",
            Category: "administrator",
            Subject: ParitySubject.InstanceAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.ModerateHeavy,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Instance administration is an oversight role and moderation is its core power."),

        new(
            Id: "instance-admin-cannot-author-event",
            Category: "administrator",
            Subject: ParitySubject.InstanceAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Update,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Oversight is not authorship: a compromised instance account must not rewrite tenant content."),

        new(
            Id: "org-admin-updates-own-organization-event",
            Category: "normal-allow",
            Subject: ParitySubject.OrganizationAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Update,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "An organization administrator authors the events their organization owns."),

        new(
            Id: "org-admin-cannot-update-other-organization-event",
            Category: "normal-deny",
            Subject: ParitySubject.OrganizationAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Update,
            Facts: OrganizationOwnedEvent(organizationId: OtherOrganizationId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Organization authority is scoped to the organization that owns the event."),

        // ---- tenant isolation ----------------------------------------------------------------------
        new(
            Id: "tenant-admin-cannot-reach-other-tenant-event",
            Category: "wrong-tenant",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Update,
            Facts: OrganizationOwnedEvent(tenantId: OtherTenantId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Tenant administration must not cross the tenant boundary."),

        new(
            Id: "event-facts-without-tenant-deny",
            Category: "missing-tenant",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Update,
            Facts: OrganizationOwnedEvent(tenantId: Guid.Empty),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "An unset tenant cannot match any administered tenant, so the check fails closed."),

        new(
            Id: "event-check-without-facts-deny",
            Category: "missing-resource",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Update,
            Facts: null,
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "With no trusted facts there is nothing to weigh, so the provider must deny."),

        // ---- admission authority parity ------------------------------------------------------------
        new(
            Id: "tenant-admin-cannot-view-admission",
            Category: "normal-deny",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.EventCheckInView,
            Facts: AdmissionEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Tenant administration is not door authority; admission requires an exact event assignment."),

        new(
            Id: "organization-admin-cannot-manage-admission",
            Category: "normal-deny",
            Subject: ParitySubject.OrganizationAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.EventCheckInManage,
            Facts: AdmissionEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Broad organization event authority must not imply admission management."),

        new(
            Id: "event-owner-without-admission-permission-cannot-view-admission",
            Category: "normal-deny",
            Subject: ParitySubject.EventOwnerWithoutAdmissionPermission,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.EventCheckInView,
            Facts: AdmissionEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "An eligible event role still needs an explicit admission permission."),

        new(
            Id: "admission-viewer-can-view-but-cannot-manage",
            Category: "normal-allow",
            Subject: ParitySubject.AdmissionViewer,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.EventCheckInView,
            Facts: AdmissionEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Check-in staff with event_check_in:view may read bounded admission state."),

        new(
            Id: "admission-viewer-cannot-manage",
            Category: "normal-deny",
            Subject: ParitySubject.AdmissionViewer,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.EventCheckInManage,
            Facts: AdmissionEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "The view permission never implies admission mutation authority."),

        new(
            Id: "admission-manager-can-view-through-manage-permission",
            Category: "normal-allow",
            Subject: ParitySubject.AdmissionManager,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.EventCheckInView,
            Facts: AdmissionEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Cerbos explicitly lets event_check_in:manage satisfy admission view."),

        new(
            Id: "admission-manager-can-manage",
            Category: "normal-allow",
            Subject: ParitySubject.AdmissionManager,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.EventCheckInManage,
            Facts: AdmissionEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Check-in staff with the exact manage permission may mutate admission state."),

        // ---- approval-publish privilege ------------------------------------------------------------
        new(
            Id: "tenant-admin-approves-publication-in-own-tenant",
            Category: "administrator",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: ApprovePublishAction,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Tenant approval policy is administered by the matching tenant administrator."),

        new(
            Id: "instance-admin-approves-publication",
            Category: "administrator",
            Subject: ParitySubject.InstanceAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: ApprovePublishAction,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Instance moderation authority may approve publication without acquiring event authorship."),

        new(
            Id: "event-owner-cannot-approve-publication",
            Category: "normal-deny",
            Subject: ParitySubject.EventOwner,
            ResourceKind: ResourceKinds.Event,
            Action: ApprovePublishAction,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Event ownership is delegable authorship and cannot satisfy an independent approval boundary."),

        new(
            Id: "event-manager-cannot-approve-publication",
            Category: "normal-deny",
            Subject: ParitySubject.EventManager,
            ResourceKind: ResourceKinds.Event,
            Action: ApprovePublishAction,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Event management authority cannot approve the manager's own publication."),

        new(
            Id: "actor-owner-cannot-approve-publication",
            Category: "normal-deny",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.Event,
            Action: ApprovePublishAction,
            Facts: UserOwnedEvent(UserId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Owning the event actor grants authorship, not privileged publication approval."),

        new(
            Id: "organization-admin-cannot-approve-publication",
            Category: "normal-deny",
            Subject: ParitySubject.OrganizationAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: ApprovePublishAction,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Organization administration remains inside the submitting authority and cannot self-approve."),

        new(
            Id: "tenant-admin-cannot-approve-other-tenant-publication",
            Category: "wrong-tenant",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: ApprovePublishAction,
            Facts: OrganizationOwnedEvent(tenantId: OtherTenantId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Approval authority is tenant-qualified and must fail closed across tenant boundaries."),

        new(
            Id: "machine-caller-cannot-approve-publication",
            Category: "machine-caller",
            Subject: ParitySubject.MachineCaller,
            ResourceKind: ResourceKinds.Event,
            Action: ApprovePublishAction,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Approval is a privileged human moderation decision and is never delegated to an API key."),

        // ---- ordinary publication controls ---------------------------------------------------------
        new(
            Id: "event-owner-retains-ordinary-publish",
            Category: "normal-allow",
            Subject: ParitySubject.EventOwner,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.Publish,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Tenants without approval requirements retain existing event-owner publication authority."),

        new(
            Id: "event-manager-retains-ordinary-publish",
            Category: "normal-allow",
            Subject: ParitySubject.EventManager,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.Publish,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Tenants without approval requirements retain existing event-manager publication authority."),

        new(
            Id: "actor-owner-retains-ordinary-publish",
            Category: "normal-allow",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.Publish,
            Facts: UserOwnedEvent(UserId),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Tenants without approval requirements retain existing actor-owner publication authority."),

        new(
            Id: "organization-admin-retains-ordinary-publish",
            Category: "normal-allow",
            Subject: ParitySubject.OrganizationAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.Publish,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Tenants without approval requirements retain existing organization publication authority."),

        // ---- machine caller ------------------------------------------------------------------------
        new(
            Id: "machine-caller-cannot-create-event",
            Category: "machine-caller",
            Subject: ParitySubject.MachineCaller,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Create,
            Facts: new PreCreateAuthorizationFacts(TenantId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Event authoring is a human act; machine principals are excluded from the create rule."),

        // ---- missing subject -----------------------------------------------------------------------
        new(
            Id: "anonymous-caller-denied-before-policy",
            Category: "missing-subject",
            Subject: ParitySubject.Anonymous,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Update,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.Local,
            Rationale: "No subject means no principal to send; the adapter denies before reaching the PDP, "
                + "so there is no Cerbos counterpart to compare against."),

        // ---- support session -----------------------------------------------------------------------
        new(
            Id: "tenant-admin-reads-support-session-in-own-tenant",
            Category: "support-session",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.SupportAccessSession,
            Action: AuthorizationActions.SupportAccessSessions.View,
            Facts: new SupportAccessSessionAuthorizationFacts(
                TenantId,
                SupportSessionId,
                ActorUserId: UserId,
                Mode: null,
                Status: "ACTIVE"),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "A tenant administrator may read the support sessions targeting their own tenant."),

        new(
            Id: "standard-user-cannot-start-support-session",
            Category: "support-session",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.SupportAccessSession,
            Action: AuthorizationActions.SupportAccessSessions.Start,
            Facts: new SupportAccessSessionAuthorizationFacts(
                TenantId,
                SupportSessionId,
                ActorUserId: UserId,
                Mode: null,
                Status: null),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Starting support access is an instance-operator power, never a tenant user's."),

        // ---- consent / contact sharing -------------------------------------------------------------
        new(
            Id: "standard-user-cannot-export-shared-contacts",
            Category: "consent-contact-sharing",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.EventContactShareConsent,
            Action: AuthorizationActions.ExportSharedContacts,
            Facts: new ContactShareAuthorizationFacts(TenantId, OrganizationId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Attendee contact details are consent-bound; reading them requires organization authority."),

        // ---- guest capability ----------------------------------------------------------------------
        new(
            Id: "non-account-holder-cannot-view-registration-order",
            Category: "guest-capability",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.RegistrationOrder,
            Action: AuthorizationActions.RegistrationOrders.View,
            Facts: new RegistrationOrderAuthorizationFacts(TenantId, EventId, AccountUserId: OtherUserId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "An order is readable by its own account holder or by event registration managers; "
                + "an unrelated authenticated user is neither."),

        // ---- public visibility ---------------------------------------------------------------------
        new(
            Id: "standard-user-cannot-download-other-tenant-object",
            Category: "public-visibility",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.StorageObject,
            Action: AuthorizationActions.StorageObjects.Download,
            Facts: new PersistedStorageObjectAuthorizationFacts(
                OtherTenantId,
                StorageObjectId,
                Visibility: "PublicImage",
                LifecycleState: "Active",
                CreatedBy: null,
                OwningResourceKind: null,
                OwningResourceId: null),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Public visibility is scoped inside its own tenant; it never crosses the tenant boundary."),

        // ---- governed manual addresses -------------------------------------------------------------
        new(
            Id: "tenant-admin-manages-custom-addresses",
            Category: "normal-allow",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Location,
            Action: AuthorizationActions.Locations.ManageCustomAddresses,
            Facts: new TenantScopedAuthorizationFacts(TenantId),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Only tenant administration may enable the AdminOnly manual-address path."),

        new(
            Id: "standard-user-cannot-manage-custom-addresses",
            Category: "normal-deny",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.Location,
            Action: AuthorizationActions.Locations.ManageCustomAddresses,
            Facts: new TenantScopedAuthorizationFacts(TenantId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Authentication alone never grants tenant-wide custom-address management."),

        new(
            Id: "organization-admin-creates-organization-address",
            Category: "normal-allow",
            Subject: ParitySubject.OrganizationAdmin,
            ResourceKind: ResourceKinds.Organization,
            Action: AuthorizationActions.Locations.CreateCustomAddress,
            Facts: new OrganizationAuthorizationFacts(TenantId, OrganizationId),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "An owning organization administrator may create an organization-scoped address when settings grant it."),

        new(
            Id: "standard-user-cannot-create-organization-address",
            Category: "normal-deny",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.Organization,
            Action: AuthorizationActions.Locations.CreateCustomAddress,
            Facts: new OrganizationAuthorizationFacts(TenantId, OrganizationId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "A bare authenticated user cannot claim organization-scoped address authority."),

        new(
            Id: "tenant-admin-approves-tenant-address",
            Category: "normal-allow",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Location,
            Action: AuthorizationActions.Locations.ApproveTenantAddress,
            Facts: new TenantScopedAuthorizationFacts(TenantId),
            ExpectedAllowed: true,
            Lanes: ParityLane.Both,
            Rationale: "Tenant-wide address reuse requires explicit tenant moderation authority."),

        new(
            Id: "standard-user-cannot-approve-tenant-address",
            Category: "normal-deny",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.Location,
            Action: AuthorizationActions.Locations.ApproveTenantAddress,
            Facts: new TenantScopedAuthorizationFacts(TenantId),
            ExpectedAllowed: false,
            Lanes: ParityLane.Both,
            Rationale: "Authentication alone cannot promote an address to tenant-wide reuse."),

        // ---- provider failure ----------------------------------------------------------------------
        // Provider unavailability is a runtime routing concern, not a policy question: there is no PDP
        // answer to compare against because the PDP is precisely what is unreachable.
        new(
            Id: "provider-failure-fails-closed",
            Category: "provider-failure",
            Subject: ParitySubject.TenantAdmin,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.View,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.None,
            Rationale: "Covered by RuntimeAuthorizationProviderTests, which injects the transport failure "
                + "the Local evaluator cannot itself produce."),

        // ---- HAL suppression -----------------------------------------------------------------------
        // A denied decision must remove the affordance. That is an evaluator-pipeline behaviour rather
        // than a provider decision, so it is verified where the pipeline lives.
        new(
            Id: "denied-decision-suppresses-affordance",
            Category: "hal-suppression",
            Subject: ParitySubject.StandardUser,
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Update,
            Facts: OrganizationOwnedEvent(),
            ExpectedAllowed: false,
            Lanes: ParityLane.None,
            Rationale: "Covered by HateoasAuthorizationEvaluatorTests, which asserts link omission rather "
                + "than a provider decision."),
    ];

    /// <summary>Scenarios a given lane is responsible for evaluating.</summary>
    public static IReadOnlyList<ParityScenario> For(ParityLane lane) =>
        Scenarios.Where(scenario => scenario.Lanes.HasFlag(lane)).ToArray();
}
