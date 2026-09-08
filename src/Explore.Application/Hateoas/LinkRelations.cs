// ABOUTME: Central link-relation catalog for HAL resources emitted by the API.
// ABOUTME: Separates standard IANA relations from platform custom action relations.

namespace Explore.Application.Hateoas;

/// <summary>
/// Standard link relation types as defined by IANA and custom platform relations.
/// See: https://www.iana.org/assignments/link-relations/link-relations.xhtml
/// </summary>
public static class LinkRelations
{
    public const string VisitorSignupPrefix = "signup:";
    public const string LocalIdentities = "local-identities";
    public const string CreateLocalIdentity = "create-local-identity";
    public const string IssueTemporaryCredential = "issue-temporary-credential";

    public const string CreateConfigurationImportSession =
        "create-configuration-import-session";
    public const string ExportTenantConfigurationPackage =
        "export-tenant-configuration-package";
    public const string PreviewConfigurationImport =
        "preview-configuration-import";
    public const string RefreshConfigurationImportPreview =
        "refresh-configuration-import-preview";
    public const string CancelConfigurationImport =
        "cancel-configuration-import";
    public const string ApplyConfigurationImport =
        "apply-configuration-import";
    public const string ConfigurationImportHistory =
        "configuration-import-history";
    public const string ConfigurationImportReceipt =
        "configuration-import-receipt";
    public const string CreateConfigurationImportRollback =
        "create-configuration-import-rollback";
    public const string CreateConfigurationDirectTransfer =
        "create-configuration-direct-transfer";

    public const string ManageEventAddOns = "manage-event-add-ons";
    public const string CreateEventAddOnCatalogDraft = "create-event-add-on-catalog-draft";
    public const string AddEventAddOnCatalogItem = "add-event-add-on-catalog-item";
    public const string PublishEventAddOnCatalog = "publish-event-add-on-catalog";
    public const string RetireEventAddOnCatalog = "retire-event-add-on-catalog";
    public const string ReserveEventAddOns = "reserve-event-add-ons";
    public const string FulfillEventAddOn = "fulfill-event-add-on";
    public const string RefundEventAddOn = "refund-event-add-on";

    #region IANA Standard Relations (RFC 8288)

    /// <summary>
    /// Conveys an identifier for the link's context (current resource).
    /// </summary>
    public const string Self = "self";

    /// <summary>
    /// The target IRI points to a resource which represents the collection resource.
    /// </summary>
    public const string Collection = "collection";

    /// <summary>
    /// The target IRI points to a resource that is a member of the collection.
    /// </summary>
    public const string Item = "item";

    /// <summary>
    /// An IRI that refers to the furthest preceding resource in a series.
    /// </summary>
    public const string First = "first";

    /// <summary>
    /// An IRI that refers to the furthest following resource in a series.
    /// </summary>
    public const string Last = "last";

    /// <summary>
    /// Indicates that the link's context is a part of a series, and the previous in the series is the link target.
    /// </summary>
    public const string Prev = "prev";

    /// <summary>
    /// Indicates that the link's context is a part of a series, and the next in the series is the link target.
    /// </summary>
    public const string Next = "next";

    /// <summary>
    /// Refers to a resource that can be used to edit the link's context.
    /// </summary>
    public const string Edit = "edit";

    /// <summary>
    /// Identifies a related resource.
    /// </summary>
    public const string Related = "related";

    /// <summary>
    /// Refers to a resource that can be used to search through the link's context and related resources.
    /// </summary>
    public const string Search = "search";

    /// <summary>
    /// The target resource represents the canonical URI for the context.
    /// </summary>
    public const string Canonical = "canonical";

    /// <summary>
    /// Refers to a parent document in a hierarchy of documents.
    /// </summary>
    public const string Up = "up";

    /// <summary>
    /// Points to a resource containing the version history for the context.
    /// </summary>
    public const string VersionHistory = "version-history";

    /// <summary>
    /// Indicates a resource where payment is accepted.
    /// </summary>
    public const string Payment = "payment";
    public const string PaymentAcceptance = "payment-acceptance";
    public const string StartPayment = "start-payment";
    public const string ReservePurchaseAuthority = "reserve-purchase-authority";
    public const string PaymentStatus = "payment-status";
    public const string CheckoutRedirect = "checkout-redirect";
    public const string RetryPayment = "retry-payment";
    public const string StudioPaymentStatus = "studio-payment-status";
    public const string RequestRefund = "request-refund";
    public const string RespondMaterialChange = "respond-material-change";
    public const string CreateRefund = "create-refund";
    public const string RetryRefund = "retry-refund";
    public const string RefundCampaigns = "refund-campaigns";
    public const string ResumeRefundCampaign = "resume-refund-campaign";
    public const string StopPaidSales = "stop-paid-sales";
    public const string RequestPaidSalesResume = "request-paid-sales-resume";
    public const string ReviewPaidSalesResume = "review-paid-sales-resume";
    public const string RequestPaidCheckoutReview = "request-paid-checkout-review";
    public const string DecidePaidCheckoutReview = "decide-paid-checkout-review";
    public const string CheckInAdmissions = "check-in-admissions";
    public const string AdmissionCheckInSummary = "admission-check-in-summary";
    public const string AdmissionCheckInAudit = "admission-check-in-audit";
    public const string IssueScannerCapability = "issue-scanner-capability";
    public const string RevokeScannerCapability = "revoke-scanner-capability";
    public const string UndoAdmissionCheckIn = "undo-admission-check-in";
    public const string AdmissionCheckInHealth = "admission-check-in-health";
    public const string StopAdmissionCheckIn = "stop-admission-check-in";
    public const string RestoreAdmissionCheckIn = "restore-admission-check-in";
    public const string ReconcileAdmissionCheckIn = "reconcile-admission-check-in";
    public const string AdmissionCheckInResults = "admission-check-in-results";
    public const string CompleteParticipantReadiness =
        "complete-participant-readiness";
    public const string ApproveParticipantReadiness =
        "approve-participant-readiness";
    public const string RevokeParticipantReadiness =
        "revoke-participant-readiness";
    public const string OfferTicketTransfer =
        "offer-ticket-transfer";
    public const string AcceptTicketTransfer =
        "accept-ticket-transfer";
    public const string CancelTicketTransfer =
        "cancel-ticket-transfer";
    public const string CorrectTicketTransfer =
        "correct-ticket-transfer";
    public const string ReissueTransferredTicket =
        "reissue-transferred-ticket";
    public const string JoinFairReturnWaitlist =
        "join-fair-return-waitlist";
    public const string LeaveFairReturnWaitlist =
        "leave-fair-return-waitlist";
    public const string AcceptFairReturnOffer =
        "accept-fair-return-offer";
    public const string WithdrawFairReturnSupply =
        "withdraw-fair-return-supply";

    public const string Instantiate = "instantiate";

    #endregion

    #region Common CRUD Operations

    /// <summary>
    /// Link to create a new resource in a collection.
    /// </summary>
    public const string Create = "create";

    /// <summary>
    /// Link to update the current resource.
    /// </summary>
    public const string Update = "update";

    /// <summary>
    /// Link to delete the current resource.
    /// </summary>
    public const string Delete = "delete";

    #endregion

    #region Platform Custom Relations

    /// <summary>
    /// Events belonging to an organization or actor.
    /// </summary>
    public const string Events = "events";

    /// <summary>
    /// Sessions belonging to an event.
    /// </summary>
    public const string Sessions = "sessions";

    /// <summary>
    /// Program overview for an event, composed from program sections and sessions.
    /// </summary>
    public const string Program = "program";

    /// <summary>
    /// Server-backed program summary for an event.
    /// </summary>
    public const string ProgramSummary = "program-summary";

    /// <summary>
    /// Deployment-mode migration runbook for deliberate instance-mode transitions.
    /// </summary>
    public const string DeploymentModeRunbook = "deployment-mode-runbook";

    /// <summary>
    /// Action link for switching an instance to multi-tenant mode through the runbook.
    /// </summary>
    public const string TransitionToMultiTenant = "transition-to-multi-tenant";

    /// <summary>
    /// Action link for switching an instance to single-tenant mode through the runbook.
    /// </summary>
    public const string TransitionToSingleTenant = "transition-to-single-tenant";

    /// <summary>
    /// Action link for adding a session to an event program.
    /// </summary>
    public const string AddSession = "add-session";

    public const string CreateSessionDraft = "create-session-draft";

    /// <summary>
    /// Server-owned defaults and option lists for adding a session to an event program.
    /// </summary>
    public const string SessionCreateContext = "session-create-context";

    /// <summary>
    /// Action link for assigning sessions to a program section, track, devroom, or stage.
    /// </summary>
    public const string AssignSession = "assign-session";

    /// <summary>
    /// Action link for adding a program section, track, devroom, or stage to an event.
    /// </summary>
    public const string AddSessionGroup = "add-session-group";

    /// <summary>
    /// Speakers for a session.
    /// </summary>
    public const string Speakers = "speakers";

    /// <summary>
    /// Agenda items for a session.
    /// </summary>
    public const string AgendaItems = "agenda-items";

    /// <summary>
    /// Members of an organization.
    /// </summary>
    public const string Members = "members";

    /// <summary>
    /// Team members with operational roles for an event.
    /// </summary>
    public const string Team = "team";

    public const string AssignEventRole = "assign-event-role";

    /// <summary>
    /// Registration action for an event or session.
    /// </summary>
    public const string Registration = "registration";

    /// <summary>
    /// Registrations for an event or session.
    /// </summary>
    public const string Registrations = "registrations";

    /// <summary>
    /// Categories assigned to an event.
    /// </summary>
    public const string Categories = "categories";

    /// <summary>
    /// Tags assigned to an event.
    /// </summary>
    public const string Tags = "tags";

    /// <summary>
    /// Location of a session or event.
    /// </summary>
    public const string Location = "location";

    /// <summary>
    /// Parent organization.
    /// </summary>
    public const string Organization = "organization";

    /// <summary>
    /// Parent group.
    /// </summary>
    public const string Group = "group";

    /// <summary>
    /// Parent event.
    /// </summary>
    public const string Event = "event";

    /// <summary>
    /// Actor (user or organization) associated with a resource.
    /// </summary>
    public const string Actor = "actor";

    /// <summary>
    /// Featured image for a resource.
    /// </summary>
    public const string FeaturedImage = "featured-image";

    /// <summary>
    /// Parent category (for hierarchical categories).
    /// </summary>
    public const string Parent = "parent";

    /// <summary>
    /// Child categories or subcategories.
    /// </summary>
    public const string Children = "children";

    /// <summary>
    /// Program sections, tracks, devrooms, or stages for an event.
    /// </summary>
    public const string SessionGroups = "session-groups";

    /// <summary>
    /// Tag type that a tag belongs to.
    /// </summary>
    public const string TagType = "tag-type";

    /// <summary>
    /// Languages for an event session.
    /// </summary>
    public const string Languages = "languages";

    /// <summary>
    /// Publish action for draft resources.
    /// </summary>
    public const string Publish = "publish";

    public const string Schedule = "schedule";

    /// <summary>
    /// Readiness validation for publishing draft resources.
    /// </summary>
    public const string PublishReadiness = "publish-readiness";

    public const string RemediateLocation = "remediate-location";

    public const string AddressSuggestions = "address-suggestions";

    public const string ApproveTenantAddress = "approve-tenant-address";

    /// <summary>
    /// Cancel action for events or registrations.
    /// </summary>
    public const string Cancel = "cancel";

    public const string Continue = "continue";

    public const string Finalize = "finalize";

    public const string Complete = "complete";

    public const string Release = "release";

    /// <summary>
    /// Reversible administrative moderation action for hiding policy-violating events without deleting content.
    /// </summary>
    public const string ModerateLight = "moderate-light";

    /// <summary>
    /// Irreversible administrative moderation action for redacting unsafe event content and deleting uploaded media.
    /// </summary>
    public const string ModerateHeavy = "moderate-heavy";

    /// <summary>
    /// Administrative action for restoring a reversibly moderated event to the published lifecycle state.
    /// </summary>
    public const string Unmoderate = "unmoderate";

    /// <summary>
    /// Management-only audit history for event moderation lifecycle actions.
    /// </summary>
    public const string ModerationHistory = "moderation-history";

    /// <summary>
    /// Reporter-facing event-report option catalog for an event.
    /// </summary>
    public const string EventReportOptions = "event-report-options";

    /// <summary>
    /// Authenticated action for submitting a report about published event content.
    /// </summary>
    public const string ReportEvent = "report-event";

    public const string PublicActions = "public-actions";
    public const string ManagePublicActions = "manage-public-actions";
    public const string ConfigureParticipation = "configure-participation";
    public const string ManageRegistrationWorkflow = "manage-registration-workflow";
    public const string ManageRegistrationChannels = "manage-registration-channels";
    public const string ViewRegistrationProviderHealth = "view-registration-provider-health";
    public const string ProviderCreate = "provider-create";
    public const string ManualImport = "manual-import";
    public const string Poll = "poll";
    public const string Origins = "origins";
    public const string Mappings = "mappings";
    public const string Requirements = "requirements";
    public const string CreateRequirement = "create-requirement";
    public const string CreateForm = "create-form";
    public const string Form = "form";
    public const string Versions = "versions";
    public const string CreateVersion = "create-version";
    public const string Sections = "sections";
    public const string AddSection = "add-section";
    public const string ReorderSections = "reorder-sections";
    public const string ReorderFields = "reorder-fields";
    public const string Reorder = "reorder";
    public const string Fields = "fields";
    public const string AddField = "add-field";
    public const string Options = "options";
    public const string AddOption = "add-option";
    public const string Retire = "retire";
    public const string Rules = "rules";
    public const string AddRule = "add-rule";
    public const string Preflight = "preflight";
    public const string ManageTicketTypes = "manage-ticket-types";
    public const string ManageCapacityPools = "manage-capacity-pools";
    public const string TicketTypes = "ticket-types";
    public const string CapacityPools = "capacity-pools";
    public const string CreateDraft = "create-draft";
    public const string CloneDraft = "clone-draft";
    public const string CreateTicketType = "create-type";
    public const string CreateCapacityPool = "create-pool";
    public const string CommercialDisclosures = "commercial-disclosures";
    public const string PaymentConnection = "payment-connection";
    public const string StartOnboarding = "start-onboarding";
    public const string Promotions = "promotions";
    public const string CreatePromotion = "create-promotion";
    public const string RevisePromotion = "revise-promotion";
    public const string RotatePromotionCode = "rotate-promotion-code";
    public const string ApplyPromotion = "apply-promotion";
    public const string RemovePromotion = "remove-promotion";
    public const string StartRegistration = "start-registration";
    public const string StartGuestRegistration = "start-guest-registration";
    public const string SignInToRegister = "sign-in-to-register";
    public const string ClaimRegistrationOrder = "claim-registration-order";
    public const string GuestStatus = "guest-status";
    public const string CancelRegistration = "cancel-registration";
    public const string Calendar = "calendar";
    public const string ViewRegistrationOrders = "view-registration-orders";
    public const string ViewParticipants = "view-participants";
    public const string ViewRegistrationAnalytics = "view-registration-analytics";
    public const string ExportAttendees = "export-attendees";
    public const string AddParticipant = "add-participant";
    public const string UpdateParticipant = "update-participant";
    public const string AssignTickets = "assign-tickets";
    public const string ImportCompanyAssignmentsCsv = "import-company-assignments-csv";
    public const string DeferTickets = "defer-tickets";
    public const string ViewOriginalSource = "view-original-source";
    public const string ExternalEventPage = "external-event-page";
    public const string ExternalRegistration = "external-registration";
    public const string OptionalQuestionnaire = "optional-questionnaire";
    public const string RequirementProgress = "requirement-progress";
    public const string Submit = "submit";
    public const string Skip = "skip";
    public const string LaunchAttempt = "launch-attempt";
    public const string LaunchProviderAttempt = "launch-provider-attempt";
    public const string Attach = "attach";
    public const string Detach = "detach";
    public const string Livestream = "livestream";
    public const string OrganizerContact = "organizer-contact";
    public const string ClaimEvent = "claim-event";
    public const string OrganizerClaims = "organizer-claims";
    public const string WithdrawClaim = "withdraw-claim";
    public const string ReviewClaim = "review-claim";
    public const string LegitimacyEvidence = "legitimacy-evidence";
    public const string PrepareEvidenceUpload = "prepare-evidence-upload";
    public const string SubmitEvidence = "submit-evidence";
    public const string ReviewEvidence = "review-evidence";
    public const string Document = "document";
    public const string SuggestCorrection = "suggest-correction";
    public const string ReportExternalLink = "report-external-link";
    public const string ReportLegalOrCopyright = "report-legal-or-copyright";

    public const string UpdateCommunicationConsent = "update-communication-consent";

    public const string ModerationReports = "moderation-reports";

    public const string TriageReport = "triage-report";

    public const string AssignReport = "assign-report";

    public const string DecideReport = "decide-report";

    public const string ExecuteReportDecision = "execute-report-decision";

    /// <summary>
    /// Archive action for lifecycle-managed resources.
    /// </summary>
    public const string Archive = "archive";

    /// <summary>
    /// Revoke action for grants, invitations, or other auditable authority records.
    /// </summary>
    public const string Revoke = "revoke";

    /// <summary>
    /// Rotate an operational secret reference for a managed integration endpoint.
    /// </summary>
    public const string RotateSecret = "rotate-secret";

    /// <summary>
    /// Send or schedule a test request for a managed integration endpoint.
    /// </summary>
    public const string Test = "test";

    public const string DeliveryAttempts = "delivery-attempts";

    public const string Retry = "retry";

    public const string Resolve = "resolve";

    public const string Pause = "pause";

    public const string Resume = "resume";

    public const string ProviderPublications = "provider-publications";

    public const string Payload = "payload";

    public const string Reconcile = "reconcile";

    public const string Abandon = "abandon";

    public const string BulkReplays = "bulk-replays";

    public const string BulkReplayPreview = "bulk-replay-preview";

    public const string OpenProviderPortal = "open-provider-portal";

    public const string RepairProviderBinding = "repair-provider-binding";

    public const string ChangeProviderMode = "change-provider-mode";

    /// <summary>
    /// Action link for cancelling an AI provider run before it completes.
    /// </summary>
    public const string CancelRun = "cancel-run";

    /// <summary>
    /// Action link for sending a message into an AI assistant conversation.
    /// </summary>
    public const string SendMessage = "send-message";

    /// <summary>
    /// Action link for confirming an AI-proposed action before execution.
    /// </summary>
    public const string ConfirmAction = "confirm-action";

    /// <summary>
    /// Action link for rejecting an AI-proposed action without side effects.
    /// </summary>
    public const string RejectAction = "reject-action";

    /// <summary>
    /// Downloads instance and tenant overrides as one current-instance manifest.
    /// </summary>
    public const string ExportConfigurationOverrides = "export-configuration-overrides";

    /// <summary>
    /// Downloads flattened effective instance and tenant values as a portable manifest.
    /// </summary>
    public const string ExportConfigurationPortable = "export-configuration-portable";

    #endregion

    #region Scheduler Administration

    /// <summary>Navigation link from the scheduler overview to its scheduled-job collection.</summary>
    public const string SchedulerJobs = "jobs";

    /// <summary>Action link for moving the scheduler, or one job, out of firing state.</summary>
    public const string SchedulerPause = "pause";

    /// <summary>Action link for returning the scheduler, or one job, to firing state.</summary>
    public const string SchedulerResume = "resume";

    /// <summary>Action link for running one scheduled job immediately without changing its schedule.</summary>
    public const string SchedulerTrigger = "trigger";

    /// <summary>Action link for clearing a job's triggers out of the scheduler's error state.</summary>
    public const string SchedulerResetError = "reset-error";

    /// <summary>Action link for requesting cancellation of a job's currently executing instances.</summary>
    public const string SchedulerInterrupt = "interrupt";

    #endregion
}
