// ABOUTME: Central route name catalog for API endpoint metadata and HAL link generation.
// ABOUTME: Keeps controller route names stable and discoverable for OpenAPI and clients.

namespace Explore.API.Hateoas;

/// <summary>
/// Route name constants for HATEOAS link generation.
/// These must match the Name property on route attributes in controllers.
/// </summary>
public static class RouteNames
{
    public const string LoginLocalIdentity = nameof(LoginLocalIdentity);
    public const string RegisterLocalIdentity = nameof(RegisterLocalIdentity);
    public const string CreateSetupTargetEnrollment = nameof(CreateSetupTargetEnrollment);
    public const string GetSetupTargetEnrollment = nameof(GetSetupTargetEnrollment);
    public const string RevokeSetupTargetEnrollment = nameof(RevokeSetupTargetEnrollment);
    public const string RotateSetupTargetEnrollmentCapability =
        nameof(RotateSetupTargetEnrollmentCapability);
    public const string GetSetupSecretBindingReadiness =
        nameof(GetSetupSecretBindingReadiness);
    public const string WriteSetupSecretBinding = nameof(WriteSetupSecretBinding);
    public const string GetSetupSecretBindingOperation =
        nameof(GetSetupSecretBindingOperation);
    public const string GetTicketingDeploymentCapabilities =
        nameof(GetTicketingDeploymentCapabilities);
    public const string GetEventAddOnCatalog = nameof(GetEventAddOnCatalog);
    public const string GetEventAddOnManagement = nameof(GetEventAddOnManagement);
    public const string CreateEventAddOnCatalogDraft = nameof(CreateEventAddOnCatalogDraft);
    public const string AddEventAddOnCatalogItem = nameof(AddEventAddOnCatalogItem);
    public const string PublishEventAddOnCatalog = nameof(PublishEventAddOnCatalog);
    public const string RetireEventAddOnCatalog = nameof(RetireEventAddOnCatalog);
    public const string GetRegistrationOrderAddOns = nameof(GetRegistrationOrderAddOns);
    public const string ReserveRegistrationOrderAddOns = nameof(ReserveRegistrationOrderAddOns);
    public const string FulfillRegistrationOrderAddOn = nameof(FulfillRegistrationOrderAddOn);
    public const string RefundRegistrationOrderAddOn = nameof(RefundRegistrationOrderAddOn);
    public const string RequestAdmissionTicketRecovery = nameof(RequestAdmissionTicketRecovery);
    public const string ConsumeAdmissionTicketRecovery = nameof(ConsumeAdmissionTicketRecovery);
    public const string GetCurrentAdmissionTickets = nameof(GetCurrentAdmissionTickets);
    public const string GetCurrentAdmissionTicket = nameof(GetCurrentAdmissionTicket);
    public const string ReissueCurrentAdmissionTicketQr = nameof(ReissueCurrentAdmissionTicketQr);
    public const string ReissueCurrentAdmissionTicketPrint = nameof(ReissueCurrentAdmissionTicketPrint);
    public const string ListAdmissionScannerCapabilities = nameof(ListAdmissionScannerCapabilities);
    public const string IssueAdmissionScannerCapability = nameof(IssueAdmissionScannerCapability);
    public const string RevokeAdmissionScannerCapability = nameof(RevokeAdmissionScannerCapability);
    public const string CheckInAdmission = nameof(CheckInAdmission);
    public const string GetAdmissionCheckIn = nameof(GetAdmissionCheckIn);
    public const string BatchCheckInAdmissions = nameof(BatchCheckInAdmissions);
    public const string UndoAdmissionCheckIn = nameof(UndoAdmissionCheckIn);
    public const string GetAdmissionCheckInSummary = nameof(GetAdmissionCheckInSummary);
    public const string GetAdmissionCheckInAudit = nameof(GetAdmissionCheckInAudit);
    public const string ScannerCheckInAdmission = nameof(ScannerCheckInAdmission);
    public const string ScannerBatchCheckInAdmissions = nameof(ScannerBatchCheckInAdmissions);
    public const string ScannerUndoAdmissionCheckIn = nameof(ScannerUndoAdmissionCheckIn);
    public const string GetAdmissionCheckInHealth = nameof(GetAdmissionCheckInHealth);
    public const string StopAdmissionCheckIn = nameof(StopAdmissionCheckIn);
    public const string RestoreAdmissionCheckIn = nameof(RestoreAdmissionCheckIn);
    public const string ReconcileAdmissionCheckIn = nameof(ReconcileAdmissionCheckIn);
    public const string GetParticipantReadiness = nameof(GetParticipantReadiness);
    public const string CompleteParticipantReadiness = nameof(CompleteParticipantReadiness);
    public const string ApproveParticipantReadiness = nameof(ApproveParticipantReadiness);
    public const string RevokeParticipantReadiness = nameof(RevokeParticipantReadiness);
    public const string GetTicketTransfer = nameof(GetTicketTransfer);
    public const string GetFairReturnWaitlist = nameof(GetFairReturnWaitlist);
    public const string JoinFairReturnWaitlist = nameof(JoinFairReturnWaitlist);
    public const string LeaveFairReturnWaitlist = nameof(LeaveFairReturnWaitlist);
    public const string AcceptFairReturnOffer = nameof(AcceptFairReturnOffer);
    public const string WithdrawFairReturnSupply = nameof(WithdrawFairReturnSupply);
    public const string OfferTicketTransfer = nameof(OfferTicketTransfer);
    public const string AcceptTicketTransfer = nameof(AcceptTicketTransfer);
    public const string CancelTicketTransfer = nameof(CancelTicketTransfer);
    public const string CorrectTicketTransfer = nameof(CorrectTicketTransfer);
    public const string ReissueTransferredTicket = nameof(ReissueTransferredTicket);

    #region Managed Event Routes

    public const string GetManagementCapabilities = nameof(GetManagementCapabilities);
    public const string TriggerManagementRegistration = nameof(TriggerManagementRegistration);
    public const string GetManagedEventInstance = nameof(GetManagedEventInstance);
    public const string GetManagementVersion = nameof(GetManagementVersion);
    public const string GetManagementHealth = nameof(GetManagementHealth);
    public const string EvaluateManagementUpgradePreflight = nameof(EvaluateManagementUpgradePreflight);
    public const string VerifyManagementUpgradePostflight = nameof(VerifyManagementUpgradePostflight);
    public const string RotateManagedControlPlaneCredential = nameof(RotateManagedControlPlaneCredential);
    public const string RevokeManagedControlPlaneRegistration = nameof(RevokeManagedControlPlaneRegistration);
    public const string EvaluateManagedTenantProvisioningPreflight = nameof(EvaluateManagedTenantProvisioningPreflight);
    public const string ScheduleManagedTenantProvisioning = nameof(ScheduleManagedTenantProvisioning);
    public const string GetManagedTenantProvisioningOperation = nameof(GetManagedTenantProvisioningOperation);
    public const string CancelManagedTenantProvisioningOperation = nameof(CancelManagedTenantProvisioningOperation);

    #endregion

    #region SEO Routes

    public const string GetSitemap = nameof(GetSitemap);

    #endregion

    #region Organization Routes

    public const string GetOrganizations = nameof(GetOrganizations);
    public const string GetOrganizationById = nameof(GetOrganizationById);
    public const string GetMyOrganizations = nameof(GetMyOrganizations);
    public const string CreateOrganization = nameof(CreateOrganization);
    public const string UpdateOrganization = nameof(UpdateOrganization);
    public const string UpdateOrganizationApprovalStatus = nameof(UpdateOrganizationApprovalStatus);
    public const string GetOrganizationTenantEvidenceCollection = nameof(GetOrganizationTenantEvidenceCollection);
    public const string GetOrganizationTenantEvidence = nameof(GetOrganizationTenantEvidence);
    public const string CreateOrganizationTenantEvidenceUploadSession = nameof(CreateOrganizationTenantEvidenceUploadSession);
    public const string SubmitOrganizationTenantEvidence = nameof(SubmitOrganizationTenantEvidence);
    public const string ReviewOrganizationTenantEvidence = nameof(ReviewOrganizationTenantEvidence);
    public const string GetOrganizationNotificationPreferences = nameof(GetOrganizationNotificationPreferences);
    public const string UpdateOrganizationNotificationPreferences = nameof(UpdateOrganizationNotificationPreferences);
    public const string SetOrganizationNotificationPreferenceMute = nameof(SetOrganizationNotificationPreferenceMute);
    public const string DeleteOrganization = nameof(DeleteOrganization);
    #endregion

    #region Event Routes

    public const string GetEvents = nameof(GetEvents);
    public const string GetEventById = nameof(GetEventById);
    public const string GetEventByPublicCode = nameof(GetEventByPublicCode);
    public const string GetEventOpenGraphImage = nameof(GetEventOpenGraphImage);
    public const string GetEventManagementDetails = nameof(GetEventManagementDetails);
    public const string GetEventModerationHistory = nameof(GetEventModerationHistory);
    public const string GetManagedEventsByActor = nameof(GetManagedEventsByActor);
    public const string GetEventCalendar = nameof(GetEventCalendar);
    public const string GetAttendeeEventCalendar = nameof(GetAttendeeEventCalendar);
    public const string GetMyEvents = nameof(GetMyEvents);
    public const string GetEventCreationContext = nameof(GetEventCreationContext);
    public const string GetEventSessionCreateContext = nameof(GetEventSessionCreateContext);
    public const string GetEventProgramSummary = nameof(GetEventProgramSummary);
    public const string GetManagedEventProgramSummary = nameof(GetManagedEventProgramSummary);
    public const string GetEventPublishReadiness = nameof(GetEventPublishReadiness);
    public const string GetEventPublicActions = nameof(GetEventPublicActions);
    public const string GetEventPublicAction = nameof(GetEventPublicAction);
    public const string RedirectEventPublicAction = nameof(RedirectEventPublicAction);
    public const string CreateEventPublicAction = nameof(CreateEventPublicAction);
    public const string UpdateEventPublicAction = nameof(UpdateEventPublicAction);
    public const string DeleteEventPublicAction = nameof(DeleteEventPublicAction);
    public const string ConfigureEventParticipation = nameof(ConfigureEventParticipation);
    public const string AttachRegistrationRequirement = nameof(AttachRegistrationRequirement);
    public const string DetachRegistrationRequirement = nameof(DetachRegistrationRequirement);
    public const string GetOptionalQuestionnaire = nameof(GetOptionalQuestionnaire);
    public const string GetRegistrationWorkflow = nameof(GetRegistrationWorkflow);
    public const string GetRegistrationAnswerAnalytics = nameof(GetRegistrationAnswerAnalytics);
    public const string LaunchAuthenticatedNativeRegistrationAttempt = nameof(LaunchAuthenticatedNativeRegistrationAttempt);
    public const string LaunchAuthenticatedRegistrationProviderAttempt = nameof(LaunchAuthenticatedRegistrationProviderAttempt);
    public const string SubmitAuthenticatedNativeRegistrationAttempt = nameof(SubmitAuthenticatedNativeRegistrationAttempt);
    public const string SkipAuthenticatedNativeRegistrationRequirement = nameof(SkipAuthenticatedNativeRegistrationRequirement);
    public const string GetAuthenticatedNativeRegistrationRequirementProgress = nameof(GetAuthenticatedNativeRegistrationRequirementProgress);
    public const string LaunchGuestNativeRegistrationAttempt = nameof(LaunchGuestNativeRegistrationAttempt);
    public const string LaunchGuestRegistrationProviderAttempt = nameof(LaunchGuestRegistrationProviderAttempt);
    public const string SubmitGuestNativeRegistrationAttempt = nameof(SubmitGuestNativeRegistrationAttempt);
    public const string SkipGuestNativeRegistrationRequirement = nameof(SkipGuestNativeRegistrationRequirement);
    public const string GetGuestNativeRegistrationRequirementProgress = nameof(GetGuestNativeRegistrationRequirementProgress);
    public const string CreateRegistrationWorkflow = nameof(CreateRegistrationWorkflow);
    public const string UpdateRegistrationWorkflow = nameof(UpdateRegistrationWorkflow);
    public const string CreateRegistrationRequirement = nameof(CreateRegistrationRequirement);
    public const string UpdateRegistrationRequirement = nameof(UpdateRegistrationRequirement);
    public const string DeleteRegistrationRequirement = nameof(DeleteRegistrationRequirement);
    public const string GetRegistrationForm = nameof(GetRegistrationForm);
    public const string CreateRegistrationForm = nameof(CreateRegistrationForm);
    public const string GetRegistrationFormVersion = nameof(GetRegistrationFormVersion);
    public const string CreateRegistrationFormVersion = nameof(CreateRegistrationFormVersion);
    public const string AddRegistrationFormSection = nameof(AddRegistrationFormSection);
    public const string UpdateRegistrationFormSection = nameof(UpdateRegistrationFormSection);
    public const string ReorderRegistrationFormSections = nameof(ReorderRegistrationFormSections);
    public const string DeleteRegistrationFormSection = nameof(DeleteRegistrationFormSection);
    public const string AddRegistrationFormField = nameof(AddRegistrationFormField);
    public const string UpdateRegistrationFormField = nameof(UpdateRegistrationFormField);
    public const string ReorderRegistrationFormFields = nameof(ReorderRegistrationFormFields);
    public const string DeleteRegistrationFormField = nameof(DeleteRegistrationFormField);
    public const string AddRegistrationFormFieldOption = nameof(AddRegistrationFormFieldOption);
    public const string UpdateRegistrationFormFieldOption = nameof(UpdateRegistrationFormFieldOption);
    public const string RetireRegistrationFormFieldOption = nameof(RetireRegistrationFormFieldOption);
    public const string AddRegistrationFormRule = nameof(AddRegistrationFormRule);
    public const string UpdateRegistrationFormRule = nameof(UpdateRegistrationFormRule);
    public const string DeleteRegistrationFormRule = nameof(DeleteRegistrationFormRule);
    public const string GetRegistrationFormPublishPreflight = nameof(GetRegistrationFormPublishPreflight);
    public const string PublishRegistrationFormVersion = nameof(PublishRegistrationFormVersion);
    public const string GetRegistrationFormTemplates = nameof(GetRegistrationFormTemplates);
    public const string GetRegistrationFormTemplate = nameof(GetRegistrationFormTemplate);
    public const string CreateRegistrationFormTemplate = nameof(CreateRegistrationFormTemplate);
    public const string InstantiateRegistrationFormTemplate = nameof(InstantiateRegistrationFormTemplate);
    public const string GetRegistrationAnswerFile = nameof(GetRegistrationAnswerFile);
    public const string ReleaseRegistrationAnswerFile = nameof(ReleaseRegistrationAnswerFile);
    public const string GetRegistrationProviderHealth = nameof(GetRegistrationProviderHealth);
    public const string GetRegistrationProviderQueue = nameof(GetRegistrationProviderQueue);
    public const string GetRegistrationProviderConnections = nameof(GetRegistrationProviderConnections);
    public const string GetRegistrationProviderConnection = nameof(GetRegistrationProviderConnection);
    public const string CreateRegistrationProviderConnection = nameof(CreateRegistrationProviderConnection);
    public const string UpdateRegistrationProviderConnection = nameof(UpdateRegistrationProviderConnection);
    public const string DeleteRegistrationProviderConnection = nameof(DeleteRegistrationProviderConnection);
    public const string ReplaceRegistrationProviderApprovedOrigins = nameof(ReplaceRegistrationProviderApprovedOrigins);
    public const string ImportExternalRegistrationProviderFormVersion = nameof(ImportExternalRegistrationProviderFormVersion);
    public const string GetRegistrationProviderBindings = nameof(GetRegistrationProviderBindings);
    public const string GetRegistrationProviderBinding = nameof(GetRegistrationProviderBinding);
    public const string CreateRegistrationProviderBinding = nameof(CreateRegistrationProviderBinding);
    public const string UpdateRegistrationProviderBinding = nameof(UpdateRegistrationProviderBinding);
    public const string DeleteRegistrationProviderBinding = nameof(DeleteRegistrationProviderBinding);
    public const string PublishRegistrationProviderBinding = nameof(PublishRegistrationProviderBinding);
    public const string ReplaceRegistrationProviderMappings = nameof(ReplaceRegistrationProviderMappings);
    public const string GetRegistrationChannels = nameof(GetRegistrationChannels);
    public const string CreateRegistrationChannel = nameof(CreateRegistrationChannel);
    public const string UpdateRegistrationChannel = nameof(UpdateRegistrationChannel);
    public const string DeleteRegistrationChannel = nameof(DeleteRegistrationChannel);
    public const string GetRegistrationProviderLaunchDescriptor = nameof(GetRegistrationProviderLaunchDescriptor);
    public const string PollRegistrationProviderReconciliation = nameof(PollRegistrationProviderReconciliation);
    public const string QueueManualRegistrationProviderImport = nameof(QueueManualRegistrationProviderImport);
    public const string RetryRegistrationProviderParkedItem = nameof(RetryRegistrationProviderParkedItem);
    public const string ResolveRegistrationProviderQueueItem = nameof(ResolveRegistrationProviderQueueItem);
    public const string GetEventTicketCatalogManagement = nameof(GetEventTicketCatalogManagement);
    public const string CreateEventTicketCatalogDraft = nameof(CreateEventTicketCatalogDraft);
    public const string CloneEventTicketCatalogDraft = nameof(CloneEventTicketCatalogDraft);
    public const string CreateEventTicketType = nameof(CreateEventTicketType);
    public const string UpdateEventTicketType = nameof(UpdateEventTicketType);
    public const string DeleteEventTicketType = nameof(DeleteEventTicketType);
    public const string CreateEventCapacityPool = nameof(CreateEventCapacityPool);
    public const string UpdateEventCapacityPool = nameof(UpdateEventCapacityPool);
    public const string DeleteEventCapacityPool = nameof(DeleteEventCapacityPool);
    public const string GetPaidEventPublicationPreflight = nameof(GetPaidEventPublicationPreflight);
    public const string GetPaidCheckoutSaleControl = nameof(GetPaidCheckoutSaleControl);
    public const string StopPaidCheckoutSales = nameof(StopPaidCheckoutSales);
    public const string RequestPaidCheckoutResume = nameof(RequestPaidCheckoutResume);
    public const string ReviewPaidCheckoutResume = nameof(ReviewPaidCheckoutResume);
    public const string RequestPaidCheckoutReview = nameof(RequestPaidCheckoutReview);
    public const string DecidePaidCheckoutReview = nameof(DecidePaidCheckoutReview);
    public const string UpdateEventTicketCatalogCommercialDisclosures = nameof(UpdateEventTicketCatalogCommercialDisclosures);
    public const string GetEventOrganizerPaymentConnection = nameof(GetEventOrganizerPaymentConnection);
    public const string StartEventOrganizerPaymentOnboarding = nameof(StartEventOrganizerPaymentOnboarding);
    public const string ReturnEventOrganizerPaymentOnboarding = nameof(ReturnEventOrganizerPaymentOnboarding);
    public const string RefreshEventOrganizerPaymentOnboarding = nameof(RefreshEventOrganizerPaymentOnboarding);
    public const string PublishEventTicketCatalog = nameof(PublishEventTicketCatalog);
    public const string GetEventPromotions = nameof(GetEventPromotions);
    public const string GetEventPromotion = nameof(GetEventPromotion);
    public const string CreateEventPromotionDraft = nameof(CreateEventPromotionDraft);
    public const string ReviseEventPromotion = nameof(ReviseEventPromotion);
    public const string PublishEventPromotion = nameof(PublishEventPromotion);
    public const string RevokeEventPromotion = nameof(RevokeEventPromotion);
    public const string RotateEventPromotionCode = nameof(RotateEventPromotionCode);
    public const string GetEventOrganizerClaims = nameof(GetEventOrganizerClaims);
    public const string GetEventOrganizerClaim = nameof(GetEventOrganizerClaim);
    public const string GetClaimantOrganizerClaims = nameof(GetClaimantOrganizerClaims);
    public const string SubmitEventOrganizerClaim = nameof(SubmitEventOrganizerClaim);
    public const string WithdrawEventOrganizerClaim = nameof(WithdrawEventOrganizerClaim);
    public const string ReviewEventOrganizerClaim = nameof(ReviewEventOrganizerClaim);
    public const string CreateEvent = nameof(CreateEvent);
    public const string ImportEvent = nameof(ImportEvent);
    public const string PublishEvent = nameof(PublishEvent);
    public const string ApprovePublishEvent = nameof(ApprovePublishEvent);
    public const string ModerateEventLight = nameof(ModerateEventLight);
    public const string ModerateEventHeavy = nameof(ModerateEventHeavy);
    public const string UnmoderateEvent = nameof(UnmoderateEvent);
    public const string UpdateEvent = nameof(UpdateEvent);
    public const string ArchiveEvent = nameof(ArchiveEvent);
    public const string CancelEvent = nameof(CancelEvent);
    public const string DeleteEvent = nameof(DeleteEvent);
    public const string GetEventSessions = nameof(GetEventSessions);
    public const string GetEventTemplateSyncDiff = nameof(GetEventTemplateSyncDiff);
    public const string ApplyEventTemplateSync = nameof(ApplyEventTemplateSync);
    public const string GetEventTemplateSyncHistory = nameof(GetEventTemplateSyncHistory);
    #endregion

    #region Event Location Routes

    public const string GetPublicEventLocations = nameof(GetPublicEventLocations);
    public const string GetAttendeeEventLocations = nameof(GetAttendeeEventLocations);
    public const string GetManagementEventLocation = nameof(GetManagementEventLocation);
    public const string GetManagementEventLocations = nameof(GetManagementEventLocations);
    public const string GetEventLocationReviewQueue = nameof(GetEventLocationReviewQueue);
    public const string UpdateEventLocationDisclosure = nameof(UpdateEventLocationDisclosure);
    public const string ConfirmEventLocationRemediation = nameof(ConfirmEventLocationRemediation);

    #endregion

    #region Event Report Routes

    public const string GetEventReportOptions = nameof(GetEventReportOptions);
    public const string SubmitEventReport = nameof(SubmitEventReport);
    public const string SubmitEventCorrection = nameof(SubmitEventCorrection);
    public const string SubmitUnsafeExternalLinkReport = nameof(SubmitUnsafeExternalLinkReport);
    public const string SubmitLegalOrCopyrightComplaint = nameof(SubmitLegalOrCopyrightComplaint);
    public const string GetMyEventReports = nameof(GetMyEventReports);
    public const string GetMyEventReport = nameof(GetMyEventReport);
    public const string UpdateMyEventReportCommunicationConsent = nameof(UpdateMyEventReportCommunicationConsent);
    public const string GetModerationReportingRoutingState = nameof(GetModerationReportingRoutingState);
    public const string UpdateModerationReportingRoutingSettings = nameof(UpdateModerationReportingRoutingSettings);
    public const string TestModerationReportingProvider = nameof(TestModerationReportingProvider);
    public const string GetTenantModerationReportingDashboard = nameof(GetTenantModerationReportingDashboard);
    public const string GetModerationReportQueue = nameof(GetModerationReportQueue);
    public const string GetModerationReportDetail = nameof(GetModerationReportDetail);
    public const string TriageModerationReport = nameof(TriageModerationReport);
    public const string AssignModerationReport = nameof(AssignModerationReport);
    public const string DecideModerationReport = nameof(DecideModerationReport);
    public const string ExecuteModerationReportDecision = nameof(ExecuteModerationReportDecision);
    public const string ModerationIntegrationOspreyCallback = nameof(ModerationIntegrationOspreyCallback);
    public const string ModerationIntegrationCoopCallback = nameof(ModerationIntegrationCoopCallback);
    public const string IntegrationSvixOperationalCallback = nameof(IntegrationSvixOperationalCallback);
    public const string IntegrationStripeConnectCallback = nameof(IntegrationStripeConnectCallback);
    public const string RegistrationProviderCallback = nameof(RegistrationProviderCallback);

    public const string GetListmonkIntegrationSettings = nameof(GetListmonkIntegrationSettings);
    public const string ResolveIntegrationSyncAmbiguity = nameof(ResolveIntegrationSyncAmbiguity);
    public const string UpdateListmonkIntegrationSettings = nameof(UpdateListmonkIntegrationSettings);
    public const string TestListmonkIntegrationConnection = nameof(TestListmonkIntegrationConnection);

    #endregion

    #region Event Session Routes

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Preserves existing route naming convention pinned by RouteNameCoverageTests.")]
    public const string GetEventSessions_List = "GetEventSessionsList";
    public const string GetEventSessionById = nameof(GetEventSessionById);
    public const string GetManagedEventSessionById = nameof(GetManagedEventSessionById);
    public const string GetManagedEventSessionsByEvent = nameof(GetManagedEventSessionsByEvent);
    public const string CreateEventSession = nameof(CreateEventSession);
    public const string CreateDraftEventSession = nameof(CreateDraftEventSession);
    public const string ScheduleEventSession = nameof(ScheduleEventSession);
    public const string PublishEventSession = nameof(PublishEventSession);
    public const string ArchiveEventSession = nameof(ArchiveEventSession);
    public const string CancelEventSession = nameof(CancelEventSession);
    public const string CompleteEventSession = nameof(CompleteEventSession);
    public const string UpdateEventSession = nameof(UpdateEventSession);
    public const string DeleteEventSession = nameof(DeleteEventSession);
    public const string GetEventSessionLanguages = nameof(GetEventSessionLanguages);
    public const string GetManagedEventSessionLanguages = nameof(GetManagedEventSessionLanguages);
    public const string CreateEventSessionLanguage = nameof(CreateEventSessionLanguage);
    public const string UpdateEventSessionLanguage = nameof(UpdateEventSessionLanguage);
    public const string DeleteEventSessionLanguage = nameof(DeleteEventSessionLanguage);
    public const string GetEventSessionSpeakersBySession = nameof(GetEventSessionSpeakersBySession);
    public const string CreateEventSessionSpeaker = nameof(CreateEventSessionSpeaker);
    public const string UpdateEventSessionSpeaker = nameof(UpdateEventSessionSpeaker);
    public const string DeleteEventSessionSpeaker = nameof(DeleteEventSessionSpeaker);
    public const string GetEventSessionAgendaItems = nameof(GetEventSessionAgendaItems);
    public const string GetManagedEventSessionAgendaItemsBySession = nameof(GetManagedEventSessionAgendaItemsBySession);
    public const string GetEventSessionTemplateSyncDiff = nameof(GetEventSessionTemplateSyncDiff);
    public const string ApplyEventSessionTemplateSync = nameof(ApplyEventSessionTemplateSync);
    public const string GetEventSessionTemplateSyncHistory = nameof(GetEventSessionTemplateSyncHistory);

    public const string GetEventSessionGroupsByEvent = nameof(GetEventSessionGroupsByEvent);
    public const string GetEventSessionGroupById = nameof(GetEventSessionGroupById);
    public const string GetManagedEventSessionGroupsByEvent = nameof(GetManagedEventSessionGroupsByEvent);
    public const string GetManagedEventSessionGroupById = nameof(GetManagedEventSessionGroupById);
    public const string GetEventSessionGroupSessions = nameof(GetEventSessionGroupSessions);
    public const string CreateEventSessionGroup = nameof(CreateEventSessionGroup);
    public const string UpdateEventSessionGroup = nameof(UpdateEventSessionGroup);
    public const string DeleteEventSessionGroup = nameof(DeleteEventSessionGroup);
    public const string AssignEventSessionToGroup = nameof(AssignEventSessionToGroup);
    public const string UnassignEventSessionFromGroup = nameof(UnassignEventSessionFromGroup);

    #endregion

    #region Actor Routes

    public const string GetActors = nameof(GetActors);
    public const string GetActorById = nameof(GetActorById);
    public const string GetActorByDid = nameof(GetActorByDid);
    public const string GetActorsByTenant = nameof(GetActorsByTenant);
    public const string GetActorByTenant = nameof(GetActorByTenant);
    public const string SuspendActor = nameof(SuspendActor);
    public const string ReinstateActor = nameof(ReinstateActor);
    public const string SuspendAtprotoIdentity = nameof(SuspendAtprotoIdentity);
    public const string ReinstateAtprotoIdentity = nameof(ReinstateAtprotoIdentity);
    public const string GetActorSubscriptions = nameof(GetActorSubscriptions);
    public const string GetActorSubscriptionByActor = nameof(GetActorSubscriptionByActor);
    public const string SubscribeToActor = nameof(SubscribeToActor);
    public const string UpdateActorSubscriptionNotificationLevel = nameof(UpdateActorSubscriptionNotificationLevel);
    public const string UnsubscribeFromActor = nameof(UnsubscribeFromActor);
    public const string GetActorTypes = nameof(GetActorTypes);
    public const string GetActorTypeById = nameof(GetActorTypeById);

    #endregion

    #region Location Routes

    public const string GetLocations = nameof(GetLocations);
    public const string GetLocationById = nameof(GetLocationById);
    public const string CreateLocation = nameof(CreateLocation);
    public const string UpdateLocation = nameof(UpdateLocation);
    public const string DeleteLocation = nameof(DeleteLocation);
    public const string ClassifyLocationAsPrivateHome = nameof(ClassifyLocationAsPrivateHome);
    public const string AcceptPrivateHomeOwnership = nameof(AcceptPrivateHomeOwnership);
    public const string ApproveTenantAddress = nameof(ApproveTenantAddress);

    #endregion

    #region Geocoding Routes

    public const string GetAddressSuggestions = nameof(GetAddressSuggestions);

    #endregion

    #region Category Routes

    public const string GetCategories = nameof(GetCategories);
    public const string GetCategoryById = nameof(GetCategoryById);
    public const string CreateCategory = nameof(CreateCategory);
    public const string UpdateCategory = nameof(UpdateCategory);
    public const string DeleteCategory = nameof(DeleteCategory);

    #endregion

    #region Tag Routes

    public const string GetTags = nameof(GetTags);
    public const string GetTagById = nameof(GetTagById);
    public const string CreateTag = nameof(CreateTag);
    public const string UpdateTag = nameof(UpdateTag);
    public const string DeleteTag = nameof(DeleteTag);

    #endregion

    #region Registration Routes

    public const string StartGuestRegistrationOrder = nameof(StartGuestRegistrationOrder);
    public const string GetGuestRegistrationOrder = nameof(GetGuestRegistrationOrder);
    public const string GetRegistrationCheckoutComposition = nameof(GetRegistrationCheckoutComposition);
    public const string ContinueGuestRegistrationOrder = nameof(ContinueGuestRegistrationOrder);
    public const string FinalizeGuestRegistrationOrder = nameof(FinalizeGuestRegistrationOrder);
    public const string CancelGuestRegistrationOrder = nameof(CancelGuestRegistrationOrder);
    public const string ClaimGuestRegistrationOrder = nameof(ClaimGuestRegistrationOrder);
    public const string StartAuthenticatedRegistrationOrder = nameof(StartAuthenticatedRegistrationOrder);
    public const string ReserveAuthenticatedPurchaseAuthority = nameof(ReserveAuthenticatedPurchaseAuthority);
    public const string ReserveGuestPurchaseAuthority = nameof(ReserveGuestPurchaseAuthority);
    public const string GetCurrentRegistrationOrder = nameof(GetCurrentRegistrationOrder);
    public const string ContinueAuthenticatedRegistrationOrder = nameof(ContinueAuthenticatedRegistrationOrder);
    public const string FinalizeAuthenticatedRegistrationOrder = nameof(FinalizeAuthenticatedRegistrationOrder);
    public const string CancelAuthenticatedRegistrationOrder = nameof(CancelAuthenticatedRegistrationOrder);
    public const string GetEventRegistrationOrders = nameof(GetEventRegistrationOrders);
    public const string GetGuestRegistrationOrderParticipants = nameof(GetGuestRegistrationOrderParticipants);
    public const string AddGuestRegistrationOrderParticipant = nameof(AddGuestRegistrationOrderParticipant);
    public const string UpdateGuestRegistrationOrderParticipant = nameof(UpdateGuestRegistrationOrderParticipant);
    public const string AssignGuestRegistrationOrderTickets = nameof(AssignGuestRegistrationOrderTickets);
    public const string DeferGuestRegistrationOrderTickets = nameof(DeferGuestRegistrationOrderTickets);
    public const string GetAuthenticatedRegistrationOrderParticipants = nameof(GetAuthenticatedRegistrationOrderParticipants);
    public const string AddAuthenticatedRegistrationOrderParticipant = nameof(AddAuthenticatedRegistrationOrderParticipant);
    public const string UpdateAuthenticatedRegistrationOrderParticipant = nameof(UpdateAuthenticatedRegistrationOrderParticipant);
    public const string AssignAuthenticatedRegistrationOrderTickets = nameof(AssignAuthenticatedRegistrationOrderTickets);
    public const string ImportAuthenticatedRegistrationOrderCompanyAssignmentsCsv = nameof(ImportAuthenticatedRegistrationOrderCompanyAssignmentsCsv);
    public const string DeferAuthenticatedRegistrationOrderTickets = nameof(DeferAuthenticatedRegistrationOrderTickets);
    public const string ApplyGuestRegistrationOrderPromotion = nameof(ApplyGuestRegistrationOrderPromotion);
    public const string RemoveGuestRegistrationOrderPromotion = nameof(RemoveGuestRegistrationOrderPromotion);
    public const string ApplyAuthenticatedRegistrationOrderPromotion = nameof(ApplyAuthenticatedRegistrationOrderPromotion);
    public const string RemoveAuthenticatedRegistrationOrderPromotion = nameof(RemoveAuthenticatedRegistrationOrderPromotion);
    public const string StartGuestRegistrationPayment = nameof(StartGuestRegistrationPayment);
    public const string GetGuestRegistrationPayment = nameof(GetGuestRegistrationPayment);
    public const string GetGuestPaidOrderAcceptance = nameof(GetGuestPaidOrderAcceptance);
    public const string RetryGuestRegistrationPayment = nameof(RetryGuestRegistrationPayment);
    public const string GetGuestRegistrationPaymentCheckoutTarget = nameof(GetGuestRegistrationPaymentCheckoutTarget);
    public const string StartAuthenticatedRegistrationPayment = nameof(StartAuthenticatedRegistrationPayment);
    public const string GetAuthenticatedRegistrationPayment = nameof(GetAuthenticatedRegistrationPayment);
    public const string GetAuthenticatedPaidOrderAcceptance = nameof(GetAuthenticatedPaidOrderAcceptance);
    public const string RetryAuthenticatedRegistrationPayment = nameof(RetryAuthenticatedRegistrationPayment);
    public const string GetAuthenticatedRegistrationPaymentCheckoutTarget = nameof(GetAuthenticatedRegistrationPaymentCheckoutTarget);
    public const string GetStudioRegistrationPayment = nameof(GetStudioRegistrationPayment);
    public const string RequestAuthenticatedRegistrationRefund = nameof(RequestAuthenticatedRegistrationRefund);
    public const string RespondAuthenticatedRegistrationMaterialChange = nameof(RespondAuthenticatedRegistrationMaterialChange);
    public const string CreateStudioRegistrationRefund = nameof(CreateStudioRegistrationRefund);
    public const string RetryStudioRegistrationRefund = nameof(RetryStudioRegistrationRefund);
    public const string GetRefundCampaigns = nameof(GetRefundCampaigns);
    public const string GetRefundCampaign = nameof(GetRefundCampaign);
    public const string ResumeRefundCampaign = nameof(ResumeRefundCampaign);
    public const string GetStudioContext = nameof(GetStudioContext);

    #endregion

    #region Lookup Table Routes

    public const string GetEventTypes = nameof(GetEventTypes);
    public const string GetEventStatuses = nameof(GetEventStatuses);
    public const string GetMadhabs = nameof(GetMadhabs);
    public const string GetLanguages = nameof(GetLanguages);
    public const string GetTagTypes = nameof(GetTagTypes);
    public const string GetApprovalStatusOptions = nameof(GetApprovalStatusOptions);
    public const string GetAudienceAgeOptions = nameof(GetAudienceAgeOptions);
    public const string GetAudienceAgeOptionById = nameof(GetAudienceAgeOptionById);
    public const string GetAudienceGenderOptions = nameof(GetAudienceGenderOptions);
    public const string GetAudienceGenderOptionById = nameof(GetAudienceGenderOptionById);
    public const string GetCategoryTypeOptions = nameof(GetCategoryTypeOptions);
    public const string GetCategoryTypeOptionById = nameof(GetCategoryTypeOptionById);
    public const string GetCategoryTypeOptionsWithCategories = nameof(GetCategoryTypeOptionsWithCategories);
    public const string GetDidCustodyTypeOptions = nameof(GetDidCustodyTypeOptions);
    public const string GetDidCustodyTypeOptionById = nameof(GetDidCustodyTypeOptionById);
    public const string GetEventFormatOptions = nameof(GetEventFormatOptions);
    public const string GetEventFormatOptionById = nameof(GetEventFormatOptionById);
    public const string GetEventStatusById = nameof(GetEventStatusById);
    public const string GetLanguageById = nameof(GetLanguageById);
    public const string GetMadhabById = nameof(GetMadhabById);
    public const string GetRegistrationModes = nameof(GetRegistrationModes);
    public const string GetRegistrationModeById = nameof(GetRegistrationModeById);
    public const string GetRegistrationScopes = nameof(GetRegistrationScopes);
    public const string GetEventSessionKinds = nameof(GetEventSessionKinds);
    public const string GetEventSessionStatuses = nameof(GetEventSessionStatuses);
    public const string GetEventSessionStatusById = nameof(GetEventSessionStatusById);
    public const string GetScheduleItemKinds = nameof(GetScheduleItemKinds);
    public const string GetVisibilityTypes = nameof(GetVisibilityTypes);
    public const string GetVisibilityTypeById = nameof(GetVisibilityTypeById);
    public const string GetFileTypes = nameof(GetFileTypes);
    public const string GetFileTypeById = nameof(GetFileTypeById);
    public const string GetGroupPositions = nameof(GetGroupPositions);
    public const string GetGroupPositionById = nameof(GetGroupPositionById);
    public const string GetOrganizationPositions = nameof(GetOrganizationPositions);
    public const string GetOrganizationPositionById = nameof(GetOrganizationPositionById);
    public const string GetTagTypeById = nameof(GetTagTypeById);
    public const string GetTagTypesWithTags = nameof(GetTagTypesWithTags);
    public const string GetMyFeatureFlags = nameof(GetMyFeatureFlags);
    public const string GetAiAssistantBootstrap = nameof(GetAiAssistantBootstrap);
    public const string GetAiAssistantModels = nameof(GetAiAssistantModels);
    public const string GetAiConversations = nameof(GetAiConversations);
    public const string CreateAiConversation = nameof(CreateAiConversation);
    public const string GetAiConversation = nameof(GetAiConversation);
    public const string SearchAiReferences = nameof(SearchAiReferences);
    public const string SendAiMessage = nameof(SendAiMessage);
    public const string ConfirmAiProposedAction = nameof(ConfirmAiProposedAction);
    public const string RejectAiProposedAction = nameof(RejectAiProposedAction);
    public const string GetAiRunStatus = nameof(GetAiRunStatus);
    public const string CancelAiRun = nameof(CancelAiRun);
    public const string GetPublicExperienceSettings = nameof(GetPublicExperienceSettings);
    public const string GetPublicExperienceShell = nameof(GetPublicExperienceShell);
    public const string GetHomeDiscovery = nameof(GetHomeDiscovery);
    public const string GetPublicLegalDocument = nameof(GetPublicLegalDocument);
    public const string RelayAnalyticsEvent = nameof(RelayAnalyticsEvent);
    public const string GetTranslationByLanguage = nameof(GetTranslationByLanguage);
    public const string GetAvailableTranslationLanguages = nameof(GetAvailableTranslationLanguages);
    public const string GetEventSeries = nameof(GetEventSeries);
    public const string GetEventSeriesById = nameof(GetEventSeriesById);
    public const string GetTopEventSeries = nameof(GetTopEventSeries);
    public const string CreateEventSeries = nameof(CreateEventSeries);
    public const string UpdateEventSeries = nameof(UpdateEventSeries);
    public const string DeleteEventSeries = nameof(DeleteEventSeries);
    public const string GetEventRegistrationPolicies = nameof(GetEventRegistrationPolicies);

    #endregion

    #region User Routes

    public const string GetCurrentUser = nameof(GetCurrentUser);
    public const string GetUserOrganizations = nameof(GetUserOrganizations);
    public const string SyncUser = nameof(SyncUser);
    public const string GetCurrentUserAdminAuthority = nameof(GetCurrentUserAdminAuthority);
    public const string UpdateCurrentUser = nameof(UpdateCurrentUser);
    public const string DeleteCurrentUser = nameof(DeleteCurrentUser);
    public const string GetPrivacyErasureStatus = nameof(GetPrivacyErasureStatus);
    public const string ResolveUserTenantRedirection = nameof(ResolveUserTenantRedirection);
    public const string UpdateUserLastActiveTenant = nameof(UpdateUserLastActiveTenant);

    #endregion

    #region UI Shell Routes

    public const string GetUiShellContext = nameof(GetUiShellContext);

    #endregion

    #region Support Access Routes

    public const string GetCurrentSupportAccessSession = nameof(GetCurrentSupportAccessSession);
    public const string ListSupportAccessSessions = nameof(ListSupportAccessSessions);
    public const string StartSupportAccessSession = nameof(StartSupportAccessSession);
    public const string StopSupportAccessSession = nameof(StopSupportAccessSession);
    public const string ForceStopSupportAccessSession = nameof(ForceStopSupportAccessSession);
    public const string GetSupportAccessAuditEvents = nameof(GetSupportAccessAuditEvents);

    #endregion

    #region Email Unsubscribe Routes

    public const string GetEmailUnsubscribe = nameof(GetEmailUnsubscribe);
    public const string OneClickEmailUnsubscribe = nameof(OneClickEmailUnsubscribe);

    #endregion

    #region Email Dispatch Admin Routes

    public const string GetEmailDispatchStatus = nameof(GetEmailDispatchStatus);
    public const string PauseEmailDispatchTenant = nameof(PauseEmailDispatchTenant);
    public const string ResumeEmailDispatchTenant = nameof(ResumeEmailDispatchTenant);
    public const string ParkEmailDispatch = nameof(ParkEmailDispatch);
    public const string ReplayEmailDispatch = nameof(ReplayEmailDispatch);
    public const string ResolveEmailDispatchWithoutReplay = nameof(ResolveEmailDispatchWithoutReplay);
    public const string GetEmailDispatchProcessorControl = nameof(GetEmailDispatchProcessorControl);
    public const string PauseEmailDispatchProcessor = nameof(PauseEmailDispatchProcessor);
    public const string ResumeEmailDispatchProcessor = nameof(ResumeEmailDispatchProcessor);
    public const string SetEmailDispatchGlobalRateLimitOverride = nameof(SetEmailDispatchGlobalRateLimitOverride);
    public const string ClearEmailDispatchGlobalRateLimitOverride = nameof(ClearEmailDispatchGlobalRateLimitOverride);
    public const string ReconcileUnknownEmailDispatch = nameof(ReconcileUnknownEmailDispatch);

    #endregion

    #region Webhook Routes

    public const string GetWebhookEventTypes = nameof(GetWebhookEventTypes);
    public const string GetWebhookConsumers = nameof(GetWebhookConsumers);
    public const string GetWebhookConsumerById = nameof(GetWebhookConsumerById);
    public const string CreateWebhookConsumer = nameof(CreateWebhookConsumer);
    public const string UpdateWebhookConsumerProviderMode = nameof(UpdateWebhookConsumerProviderMode);
    public const string RepairWebhookProviderBinding = nameof(RepairWebhookProviderBinding);
    public const string GetWebhookEndpoints = nameof(GetWebhookEndpoints);
    public const string GetWebhookEndpointById = nameof(GetWebhookEndpointById);
    public const string CreateWebhookEndpoint = nameof(CreateWebhookEndpoint);
    public const string UpdateWebhookEndpoint = nameof(UpdateWebhookEndpoint);
    public const string DeleteWebhookEndpoint = nameof(DeleteWebhookEndpoint);
    public const string RotateWebhookEndpointSecret = nameof(RotateWebhookEndpointSecret);
    public const string TestWebhookEndpoint = nameof(TestWebhookEndpoint);
    public const string ResumeWebhookEndpoint = nameof(ResumeWebhookEndpoint);
    public const string PauseWebhookEndpoint = nameof(PauseWebhookEndpoint);
    public const string GetWebhookMessages = nameof(GetWebhookMessages);
    public const string GetWebhookMessageById = nameof(GetWebhookMessageById);
    public const string GetWebhookMessagePayload = nameof(GetWebhookMessagePayload);
    public const string GetWebhookDeliveryAttempts = nameof(GetWebhookDeliveryAttempts);
    public const string GetWebhookDeliveryAttemptById = nameof(GetWebhookDeliveryAttemptById);
    public const string RetryWebhookDeliveryAttempt = nameof(RetryWebhookDeliveryAttempt);
    public const string RedriveIncomingWebhook = nameof(RedriveIncomingWebhook);
    public const string GetIncomingWebhookEffectStatus = nameof(GetIncomingWebhookEffectStatus);
    public const string RedriveIncomingWebhookEffect = nameof(RedriveIncomingWebhookEffect);
    public const string GetWebhookProviderPublications = nameof(GetWebhookProviderPublications);
    public const string GetWebhookProviderPublicationById = nameof(GetWebhookProviderPublicationById);
    public const string ReconcileWebhookProviderPublication = nameof(ReconcileWebhookProviderPublication);
    public const string AbandonWebhookProviderPublication = nameof(AbandonWebhookProviderPublication);
    public const string GetWebhookBulkReplays = nameof(GetWebhookBulkReplays);
    public const string GetWebhookBulkReplayById = nameof(GetWebhookBulkReplayById);
    public const string PreviewWebhookBulkReplay = nameof(PreviewWebhookBulkReplay);
    public const string ScheduleWebhookBulkReplay = nameof(ScheduleWebhookBulkReplay);
    public const string CancelWebhookBulkReplay = nameof(CancelWebhookBulkReplay);
    public const string OpenSvixAppPortal = nameof(OpenSvixAppPortal);

    #endregion

    #region Tenant Routes

    public const string GetTenants = nameof(GetTenants);
    public const string GetTenantById = nameof(GetTenantById);
    public const string CreateTenant = nameof(CreateTenant);
    public const string UpdateTenant = nameof(UpdateTenant);
    public const string DeleteTenant = nameof(DeleteTenant);
    public const string GetActiveTenantCount = nameof(GetActiveTenantCount);

    #endregion

    #region Tenant Navigation Routes

    public const string GetTenantNavigationLinks = nameof(GetTenantNavigationLinks);
    public const string CreateTenantNavigationLink = nameof(CreateTenantNavigationLink);
    public const string UpdateTenantNavigationLink = nameof(UpdateTenantNavigationLink);
    public const string DeleteTenantNavigationLink = nameof(DeleteTenantNavigationLink);
    public const string ReorderTenantNavigationLinks = nameof(ReorderTenantNavigationLinks);

    #endregion

    #region Tenant User Role Grant Routes

    public const string GetTenantUserRoleGrants = nameof(GetTenantUserRoleGrants);
    public const string GetTenantUserRoleGrantById = nameof(GetTenantUserRoleGrantById);
    public const string CreateTenantUserRoleGrant = nameof(CreateTenantUserRoleGrant);
    public const string RevokeTenantUserRoleGrant = nameof(RevokeTenantUserRoleGrant);

    #endregion

    #region Tenant Settings Routes

    public const string GetTenantBrandingSettingsDocument = nameof(GetTenantBrandingSettingsDocument);
    public const string PatchTenantBrandingSettingsDocument = nameof(PatchTenantBrandingSettingsDocument);
    public const string GetTenantDirectoryOperatorIdentityDocument = nameof(GetTenantDirectoryOperatorIdentityDocument);
    public const string PatchTenantDirectoryOperatorIdentityDocument = nameof(PatchTenantDirectoryOperatorIdentityDocument);
    public const string GetTenantStorageSettings = nameof(GetTenantStorageSettings);
    public const string PatchTenantStorageSettings = nameof(PatchTenantStorageSettings);
    public const string TestTenantStorageConnection = nameof(TestTenantStorageConnection);
    public const string GetTenantPaidEventPolicySettings = nameof(GetTenantPaidEventPolicySettings);
    public const string UpdateTenantPaidEventPolicySettings = nameof(UpdateTenantPaidEventPolicySettings);
    public const string GetTenantReportingIntakePolicy = nameof(GetTenantReportingIntakePolicy);
    public const string UpdateTenantReportingIntakePolicy = nameof(UpdateTenantReportingIntakePolicy);

    #endregion

    #region Role Routes

    public const string GetRoles = nameof(GetRoles);
    public const string GetRoleById = nameof(GetRoleById);

    #endregion

    #region Organization Member Routes

    public const string GetOrganizationMemberById = nameof(GetOrganizationMemberById);
    public const string GetOrganizationMembersByOrganization = nameof(GetOrganizationMembersByOrganization);
    public const string AddOrganizationMember = nameof(AddOrganizationMember);
    public const string UpdateOrganizationMemberRole = nameof(UpdateOrganizationMemberRole);
    public const string DeleteOrganizationMember = nameof(DeleteOrganizationMember);
    public const string GetMyOrganizationInvitations = nameof(GetMyOrganizationInvitations);
    public const string AcceptOrganizationInvitation = nameof(AcceptOrganizationInvitation);
    public const string DeclineOrganizationInvitation = nameof(DeclineOrganizationInvitation);

    #endregion

    #region Group Routes

    public const string GetGroups = nameof(GetGroups);
    public const string GetGroupById = nameof(GetGroupById);
    public const string GetMyGroups = nameof(GetMyGroups);
    public const string CreateGroup = nameof(CreateGroup);
    public const string UpdateGroup = nameof(UpdateGroup);
    public const string UpdateGroupApprovalStatus = nameof(UpdateGroupApprovalStatus);
    public const string GetGroupNotificationPreferences = nameof(GetGroupNotificationPreferences);
    public const string UpdateGroupNotificationPreferences = nameof(UpdateGroupNotificationPreferences);
    public const string SetGroupNotificationPreferenceMute = nameof(SetGroupNotificationPreferenceMute);
    public const string DeleteGroup = nameof(DeleteGroup);
    public const string GetGroupMembers = nameof(GetGroupMembers);
    public const string GetGroupMemberById = nameof(GetGroupMemberById);
    public const string CreateGroupMember = nameof(CreateGroupMember);
    public const string UpdateGroupMember = nameof(UpdateGroupMember);
    public const string DeleteGroupMember = nameof(DeleteGroupMember);

    #endregion

    #region Event Session Agenda Item Routes

    public const string GetEventSessionAgendaItemById = nameof(GetEventSessionAgendaItemById);
    public const string CreateEventSessionAgendaItem = nameof(CreateEventSessionAgendaItem);
    public const string UpdateEventSessionAgendaItem = nameof(UpdateEventSessionAgendaItem);
    public const string DeleteEventSessionAgendaItem = nameof(DeleteEventSessionAgendaItem);
    public const string GetEventSessionAgendaItemsBySession = nameof(GetEventSessionAgendaItemsBySession);

    #endregion

    #region Storage Object Routes

    public const string GetStorageObjects = nameof(GetStorageObjects);
    public const string GetStorageObjectById = nameof(GetStorageObjectById);
    public const string GetStorageObjectContent = nameof(GetStorageObjectContent);
    public const string UpdateStorageObject = nameof(UpdateStorageObject);
    public const string DeleteStorageObject = nameof(DeleteStorageObject);
    public const string GetPublicStorageObjectImage = nameof(GetPublicStorageObjectImage);
    public const string GetStorageObjectPresignedDownloadUrl = nameof(GetStorageObjectPresignedDownloadUrl);
    public const string CreateStorageUploadSession = nameof(CreateStorageUploadSession);
    public const string UploadStorageUploadSessionContent = nameof(UploadStorageUploadSessionContent);
    public const string CancelStorageUploadSession = nameof(CancelStorageUploadSession);

    #endregion

    #region Organization Review Routes

    public const string GetOrganizationReviews = nameof(GetOrganizationReviews);
    public const string GetOrganizationReviewsByOrganization = nameof(GetOrganizationReviewsByOrganization);
    public const string GetOrganizationReviewsByUser = nameof(GetOrganizationReviewsByUser);
    public const string CreateOrganizationReview = nameof(CreateOrganizationReview);

    #endregion

    #region Notification Routes

    public const string GetNotifications = nameof(GetNotifications);
    public const string GetNotificationById = nameof(GetNotificationById);
    public const string GetUnreadNotificationCount = nameof(GetUnreadNotificationCount);
    public const string GetNotificationRefreshStream = nameof(GetNotificationRefreshStream);
    public const string MarkNotificationAsRead = nameof(MarkNotificationAsRead);
    public const string MarkAllNotificationsAsRead = nameof(MarkAllNotificationsAsRead);
    public const string ArchiveNotification = nameof(ArchiveNotification);
    public const string SnoozeNotification = nameof(SnoozeNotification);
    public const string DeleteNotification = nameof(DeleteNotification);
    public const string GetCurrentUserNotificationPreferences = nameof(GetCurrentUserNotificationPreferences);
    public const string UpdateCurrentUserNotificationPreferences = nameof(UpdateCurrentUserNotificationPreferences);
    public const string SetCurrentUserNotificationPreferenceMute = nameof(SetCurrentUserNotificationPreferenceMute);
    public const string GetWebPushConfiguration = nameof(GetWebPushConfiguration);
    public const string GetVapidPublicKey = nameof(GetVapidPublicKey);
    public const string GetCurrentUserWebPushSubscription = nameof(GetCurrentUserWebPushSubscription);
    public const string SubscribeCurrentUserWebPushSubscription = nameof(SubscribeCurrentUserWebPushSubscription);
    public const string UnsubscribeCurrentUserWebPushSubscription = nameof(UnsubscribeCurrentUserWebPushSubscription);

    #endregion

    #region User Appearance Routes

    public const string GetCurrentUserAppearancePreferences = nameof(GetCurrentUserAppearancePreferences);
    public const string UpdateCurrentUserAppearancePreferences = nameof(UpdateCurrentUserAppearancePreferences);
    public const string GetAvailableThemes = nameof(GetAvailableThemes);
    public const string GetUserAppearanceProfiles = nameof(GetUserAppearanceProfiles);
    public const string ClonePresetToProfile = nameof(ClonePresetToProfile);
    public const string CreateCustomAppearanceProfile = nameof(CreateCustomAppearanceProfile);
    public const string UpdateAppearanceProfile = nameof(UpdateAppearanceProfile);
    public const string SetActiveAppearanceProfile = nameof(SetActiveAppearanceProfile);
    public const string SetAppearanceThemeMode = nameof(SetAppearanceThemeMode);
    public const string GenerateAppearancePalette = nameof(GenerateAppearancePalette);
    public const string ArchiveAppearanceProfile = nameof(ArchiveAppearanceProfile);
    public const string DuplicateAppearanceProfile = nameof(DuplicateAppearanceProfile);

    #endregion

    #region UI Theme Admin Routes

    public const string GetUiThemeCatalog = nameof(GetUiThemeCatalog);
    public const string GetUiThemeDetails = nameof(GetUiThemeDetails);
    public const string CreateUiTheme = nameof(CreateUiTheme);
    public const string UpdateUiTheme = nameof(UpdateUiTheme);
    public const string DeleteUiTheme = nameof(DeleteUiTheme);

    #endregion

    #region ATProto Routes

    public const string CreateAtprotoTransient = nameof(CreateAtprotoTransient);
    public const string ReadAtprotoTransient = nameof(ReadAtprotoTransient);
    public const string ConsumeAtprotoTransient = nameof(ConsumeAtprotoTransient);
    public const string ProbeAtprotoTransient = nameof(ProbeAtprotoTransient);
    public const string BootstrapAtprotoSession = nameof(BootstrapAtprotoSession);
    public const string GetCurrentAtprotoSession = nameof(GetCurrentAtprotoSession);
    public const string RefreshCurrentAtprotoSession = nameof(RefreshCurrentAtprotoSession);
    public const string DeleteCurrentAtprotoSession = nameof(DeleteCurrentAtprotoSession);
    public const string GetAtprotoEventSource = nameof(GetAtprotoEventSource);

    #endregion

    #region Contact Share Consent Routes

    public const string GetUserContactShareConsents = nameof(GetUserContactShareConsents);
    public const string CheckConsentForOrganizer = nameof(CheckConsentForOrganizer);
    public const string WithdrawContactShareConsent = nameof(WithdrawContactShareConsent);
    public const string GetOrganizationSharedContacts = nameof(GetOrganizationSharedContacts);
    public const string ExportOrganizationSharedContacts = nameof(ExportOrganizationSharedContacts);

    #endregion

    #region Localization Admin Routes

    public const string CheckLocalizationBundleHealth = nameof(CheckLocalizationBundleHealth);
    public const string GetLocalizationConfiguration = nameof(GetLocalizationConfiguration);
    public const string ExportLocalizationBundle = nameof(ExportLocalizationBundle);
    public const string ImportLocalizationBundle = nameof(ImportLocalizationBundle);
    public const string ExportLocalizationFromTms = nameof(ExportLocalizationFromTms);
    public const string UpdateLocalizationGovernance = nameof(UpdateLocalizationGovernance);
    public const string TestLocalizationTmsConnection = nameof(TestLocalizationTmsConnection);

    #endregion

    #region Custom Property Definition Routes

    public const string GetCustomPropertyDefinitions = nameof(GetCustomPropertyDefinitions);
    public const string GetCustomPropertyDefinitionById = nameof(GetCustomPropertyDefinitionById);
    public const string CreateCustomPropertyDefinition = nameof(CreateCustomPropertyDefinition);
    public const string UpdateCustomPropertyDefinition = nameof(UpdateCustomPropertyDefinition);
    public const string DeleteCustomPropertyDefinition = nameof(DeleteCustomPropertyDefinition);
    public const string PurgeCustomPropertyDefinition = nameof(PurgeCustomPropertyDefinition);

    #endregion

    #region Event Template Routes

    public const string GetEventTemplates = nameof(GetEventTemplates);
    public const string GetEventTemplateById = nameof(GetEventTemplateById);
    public const string CreateEventTemplate = nameof(CreateEventTemplate);
    public const string UpdateEventTemplate = nameof(UpdateEventTemplate);
    public const string DeleteEventTemplate = nameof(DeleteEventTemplate);

    #endregion

    #region Event Custom Property Routes

    public const string GetEventCustomPropertyDefinitions = nameof(GetEventCustomPropertyDefinitions);
    public const string GetEventCustomPropertyDefinitionById = nameof(GetEventCustomPropertyDefinitionById);
    public const string CreateEventCustomPropertyDefinition = nameof(CreateEventCustomPropertyDefinition);
    public const string UpdateEventCustomPropertyDefinition = nameof(UpdateEventCustomPropertyDefinition);
    public const string DeleteEventCustomPropertyDefinition = nameof(DeleteEventCustomPropertyDefinition);
    public const string PurgeEventCustomPropertyDefinition = nameof(PurgeEventCustomPropertyDefinition);
    public const string GetEventCustomPropertyValues = nameof(GetEventCustomPropertyValues);
    public const string SetEventCustomPropertyValue = nameof(SetEventCustomPropertyValue);
    public const string SetEventCustomPropertyMultiValues = nameof(SetEventCustomPropertyMultiValues);

    #endregion

    #region Event Session Template Routes

    public const string GetEventSessionTemplates = nameof(GetEventSessionTemplates);
    public const string GetEventSessionTemplateById = nameof(GetEventSessionTemplateById);
    public const string CreateEventSessionTemplate = nameof(CreateEventSessionTemplate);
    public const string UpdateEventSessionTemplate = nameof(UpdateEventSessionTemplate);
    public const string DeleteEventSessionTemplate = nameof(DeleteEventSessionTemplate);

    #endregion

    #region Event Session Custom Property Routes

    public const string GetEventSessionCustomPropertyDefinitions = nameof(GetEventSessionCustomPropertyDefinitions);
    public const string GetEventSessionCustomPropertyDefinitionById = nameof(GetEventSessionCustomPropertyDefinitionById);
    public const string CreateEventSessionCustomPropertyDefinition = nameof(CreateEventSessionCustomPropertyDefinition);
    public const string UpdateEventSessionCustomPropertyDefinition = nameof(UpdateEventSessionCustomPropertyDefinition);
    public const string DeleteEventSessionCustomPropertyDefinition = nameof(DeleteEventSessionCustomPropertyDefinition);
    public const string PurgeEventSessionCustomPropertyDefinition = nameof(PurgeEventSessionCustomPropertyDefinition);
    public const string GetEventSessionCustomPropertyValues = nameof(GetEventSessionCustomPropertyValues);
    public const string SetEventSessionCustomPropertyValue = nameof(SetEventSessionCustomPropertyValue);
    public const string SetEventSessionCustomPropertyMultiValues = nameof(SetEventSessionCustomPropertyMultiValues);

    #endregion

    #region Settings Routes

    public const string GetUserSettings = nameof(GetUserSettings);
    public const string UpdateUserSettingsBatch = nameof(UpdateUserSettingsBatch);
    public const string UpdateUserSetting = nameof(UpdateUserSetting);
    public const string ResetUserSetting = nameof(ResetUserSetting);
    public const string GetTenantScopedSettings = nameof(GetTenantScopedSettings);
    public const string UpdateTenantSettingsBatch = nameof(UpdateTenantSettingsBatch);
    public const string UpdateTenantSetting = nameof(UpdateTenantSetting);
    public const string ResetTenantSetting = nameof(ResetTenantSetting);
    public const string LockTenantSetting = nameof(LockTenantSetting);
    public const string UnlockTenantSetting = nameof(UnlockTenantSetting);

    #endregion

    #region Footer Routes

    public const string GetFooterConfig = nameof(GetFooterConfig);
    public const string GetFooterLinkGroups = nameof(GetFooterLinkGroups);
    public const string GetFooterLinkGroupById = nameof(GetFooterLinkGroupById);
    public const string CreateFooterLinkGroup = nameof(CreateFooterLinkGroup);
    public const string UpdateFooterLinkGroup = nameof(UpdateFooterLinkGroup);
    public const string DeleteFooterLinkGroup = nameof(DeleteFooterLinkGroup);
    public const string ReorderFooterLinkGroups = nameof(ReorderFooterLinkGroups);
    public const string CreateFooterLink = nameof(CreateFooterLink);
    public const string UpdateFooterLink = nameof(UpdateFooterLink);
    public const string DeleteFooterLink = nameof(DeleteFooterLink);
    public const string GetFooterGovernanceSettings = nameof(GetFooterGovernanceSettings);
    public const string UpdateFooterGovernanceSettings = nameof(UpdateFooterGovernanceSettings);
    public const string GetTenantFooterSettings = nameof(GetTenantFooterSettings);
    public const string PatchTenantFooterSettings = nameof(PatchTenantFooterSettings);

    #endregion

    #region Event Aspect Routes

    public const string GetEventIslamicAspect = nameof(GetEventIslamicAspect);
    public const string GetManagedEventIslamicAspect = nameof(GetManagedEventIslamicAspect);
    public const string CreateEventIslamicAspect = nameof(CreateEventIslamicAspect);
    public const string UpdateEventIslamicAspect = nameof(UpdateEventIslamicAspect);
    public const string DeleteEventIslamicAspect = nameof(DeleteEventIslamicAspect);
    public const string GetEventTechAspect = nameof(GetEventTechAspect);
    public const string GetManagedEventTechAspect = nameof(GetManagedEventTechAspect);
    public const string CreateEventTechAspect = nameof(CreateEventTechAspect);
    public const string UpdateEventTechAspect = nameof(UpdateEventTechAspect);
    public const string DeleteEventTechAspect = nameof(DeleteEventTechAspect);

    #endregion

    #region Event Team Routes

    public const string GetEventTeam = nameof(GetEventTeam);
    public const string GetEventTeamAssignablePresets = nameof(GetEventTeamAssignablePresets);
    public const string GetCurrentUserEventPermissions = nameof(GetCurrentUserEventPermissions);
    public const string AssignEventRole = nameof(AssignEventRole);
    public const string RevokeEventRole = nameof(RevokeEventRole);

    #endregion

    #region Event Day Routes

    public const string GetEventDaysByEvent = nameof(GetEventDaysByEvent);
    public const string GetManagedEventDaysByEvent = nameof(GetManagedEventDaysByEvent);
    public const string GetEventDayById = nameof(GetEventDayById);
    public const string CreateEventDay = nameof(CreateEventDay);
    public const string UpdateEventDay = nameof(UpdateEventDay);
    public const string DeleteEventDay = nameof(DeleteEventDay);

    #endregion

    #region Event Agenda Item Routes

    public const string GetEventAgendaItemsByEvent = nameof(GetEventAgendaItemsByEvent);
    public const string GetEventAgendaItemById = nameof(GetEventAgendaItemById);
    public const string GetManagedEventAgendaItemsByEvent = nameof(GetManagedEventAgendaItemsByEvent);
    public const string GetManagedEventAgendaItemById = nameof(GetManagedEventAgendaItemById);
    public const string CreateEventAgendaItem = nameof(CreateEventAgendaItem);
    public const string UpdateEventAgendaItem = nameof(UpdateEventAgendaItem);
    public const string DeleteEventAgendaItem = nameof(DeleteEventAgendaItem);
    public const string GetEventAgendaProjection = nameof(GetEventAgendaProjection);

    #endregion

    #region Location Room Routes

    public const string GetLocationRoomsByLocation = nameof(GetLocationRoomsByLocation);
    public const string GetLocationRoomById = nameof(GetLocationRoomById);
    public const string CreateLocationRoom = nameof(CreateLocationRoom);
    public const string UpdateLocationRoom = nameof(UpdateLocationRoom);
    public const string DeleteLocationRoom = nameof(DeleteLocationRoom);

    #endregion

    #region Custom Property Projection Admin Routes

    public const string GetCustomPropertyProjectionStatus = nameof(GetCustomPropertyProjectionStatus);
    public const string RebuildCustomPropertyProjection = nameof(RebuildCustomPropertyProjection);
    public const string RebuildSingleEventCustomPropertyProjection = nameof(RebuildSingleEventCustomPropertyProjection);
    public const string DrainCustomPropertyProjectionDirtyScopes = nameof(DrainCustomPropertyProjectionDirtyScopes);
    public const string GetCustomPropertyProjectionDirtyScopes = nameof(GetCustomPropertyProjectionDirtyScopes);
    public const string GetCustomPropertyProjectionsForEvent = nameof(GetCustomPropertyProjectionsForEvent);
    public const string GetSessionCustomPropertyProjectionStatus = nameof(GetSessionCustomPropertyProjectionStatus);
    public const string RebuildSessionCustomPropertyProjection = nameof(RebuildSessionCustomPropertyProjection);
    public const string RebuildSingleSessionCustomPropertyProjection = nameof(RebuildSingleSessionCustomPropertyProjection);
    public const string GetCustomPropertyProjectionsForSession = nameof(GetCustomPropertyProjectionsForSession);
    public const string GetCustomPropertyGovernanceReport = nameof(GetCustomPropertyGovernanceReport);

    #endregion

    #region User Authentication Token Routes

    public const string GetUserAuthenticationTokens = nameof(GetUserAuthenticationTokens);
    public const string GetUserAuthenticationTokenById = nameof(GetUserAuthenticationTokenById);
    public const string DeleteUserAuthenticationToken = nameof(DeleteUserAuthenticationToken);

    #endregion

    #region External API Key Routes

    public const string GetExternalApiKeys = nameof(GetExternalApiKeys);
    public const string GetExternalApiKeyById = nameof(GetExternalApiKeyById);
    public const string CreateExternalApiKey = nameof(CreateExternalApiKey);
    public const string UpdateExternalApiKey = nameof(UpdateExternalApiKey);
    public const string DeleteExternalApiKey = nameof(DeleteExternalApiKey);
    public const string GetExternalApiKeyUsageReport = nameof(GetExternalApiKeyUsageReport);

    #endregion

    #region Managed Provider Provisioning Routes

    public const string EnsureManagedProviderClientProvisioned = nameof(EnsureManagedProviderClientProvisioned);

    #endregion

    #region Module Routes

    public const string GetAvailableModules = nameof(GetAvailableModules);
    public const string GetEnabledModules = nameof(GetEnabledModules);
    public const string CheckModuleEnabled = nameof(CheckModuleEnabled);
    public const string GetModuleSchemaUrl = nameof(GetModuleSchemaUrl);
    public const string EnableModule = nameof(EnableModule);
    public const string DisableModule = nameof(DisableModule);

    #endregion

    #region Instance Settings Routes

    public const string GetInstancePlatformMonetizationSettings = nameof(GetInstancePlatformMonetizationSettings);
    public const string UpdateInstancePlatformMonetizationSettings = nameof(UpdateInstancePlatformMonetizationSettings);
    public const string GetInstancePaidEventPolicySettings = nameof(GetInstancePaidEventPolicySettings);
    public const string UpdateInstancePaidEventPolicySettings = nameof(UpdateInstancePaidEventPolicySettings);

    public const string GetSchedulerAdminOverview = nameof(GetSchedulerAdminOverview);
    public const string GetSchedulerAdminJobs = nameof(GetSchedulerAdminJobs);
    public const string PauseScheduler = nameof(PauseScheduler);
    public const string ResumeScheduler = nameof(ResumeScheduler);
    public const string PauseSchedulerJob = nameof(PauseSchedulerJob);
    public const string ResumeSchedulerJob = nameof(ResumeSchedulerJob);
    public const string TriggerSchedulerJob = nameof(TriggerSchedulerJob);
    public const string ResetSchedulerJobErrorState = nameof(ResetSchedulerJobErrorState);
    public const string InterruptSchedulerJob = nameof(InterruptSchedulerJob);

    public const string GetControlPlaneOverview = nameof(GetControlPlaneOverview);
    public const string CreateInstanceConfigurationImportSession = nameof(CreateInstanceConfigurationImportSession);
    public const string PreviewInstanceConfigurationImportSession = nameof(PreviewInstanceConfigurationImportSession);
    public const string RefreshInstanceConfigurationImportSession = nameof(RefreshInstanceConfigurationImportSession);
    public const string CancelInstanceConfigurationImportSession = nameof(CancelInstanceConfigurationImportSession);
    public const string ApplyInstanceConfigurationImportSession = nameof(ApplyInstanceConfigurationImportSession);
    public const string ListInstanceConfigurationImportHistory = nameof(ListInstanceConfigurationImportHistory);
    public const string GetInstanceConfigurationImportReceipt = nameof(GetInstanceConfigurationImportReceipt);
    public const string CreateInstanceConfigurationRollbackSession = nameof(CreateInstanceConfigurationRollbackSession);
    public const string CreateTenantConfigurationImportSession = nameof(CreateTenantConfigurationImportSession);
    public const string ExportTenantConfigurationPackage = nameof(ExportTenantConfigurationPackage);
    public const string PreviewTenantConfigurationImportSession = nameof(PreviewTenantConfigurationImportSession);
    public const string RefreshTenantConfigurationImportSession = nameof(RefreshTenantConfigurationImportSession);
    public const string CancelTenantConfigurationImportSession = nameof(CancelTenantConfigurationImportSession);
    public const string ApplyTenantConfigurationImportSession = nameof(ApplyTenantConfigurationImportSession);
    public const string ListTenantConfigurationImportHistory = nameof(ListTenantConfigurationImportHistory);
    public const string GetTenantConfigurationImportReceipt = nameof(GetTenantConfigurationImportReceipt);
    public const string CreateTenantConfigurationRollbackSession = nameof(CreateTenantConfigurationRollbackSession);
    public const string CreateInstanceConfigurationTransfer = nameof(CreateInstanceConfigurationTransfer);
    public const string CreateTenantConfigurationTransfer = nameof(CreateTenantConfigurationTransfer);
    public const string ApproveInstanceConfigurationTransferSource = nameof(ApproveInstanceConfigurationTransferSource);
    public const string ApproveTenantConfigurationTransferSource = nameof(ApproveTenantConfigurationTransferSource);
    public const string AppendInstanceConfigurationTransferChunk = nameof(AppendInstanceConfigurationTransferChunk);
    public const string AppendTenantConfigurationTransferChunk = nameof(AppendTenantConfigurationTransferChunk);
    public const string CompleteInstanceConfigurationTransfer = nameof(CompleteInstanceConfigurationTransfer);
    public const string CompleteTenantConfigurationTransfer = nameof(CompleteTenantConfigurationTransfer);
    public const string PromoteInstanceConfigurationTransfer = nameof(PromoteInstanceConfigurationTransfer);
    public const string PromoteTenantConfigurationTransfer = nameof(PromoteTenantConfigurationTransfer);
    public const string CancelInstanceConfigurationTransfer = nameof(CancelInstanceConfigurationTransfer);
    public const string CancelTenantConfigurationTransfer = nameof(CancelTenantConfigurationTransfer);
    public const string CreateInstanceConfigurationManagedApplySchedule = nameof(CreateInstanceConfigurationManagedApplySchedule);
    public const string CreateTenantConfigurationManagedApplySchedule = nameof(CreateTenantConfigurationManagedApplySchedule);
    public const string ApproveInstanceConfigurationManagedApplySchedule = nameof(ApproveInstanceConfigurationManagedApplySchedule);
    public const string ApproveTenantConfigurationManagedApplySchedule = nameof(ApproveTenantConfigurationManagedApplySchedule);
    public const string CancelInstanceConfigurationManagedApplySchedule = nameof(CancelInstanceConfigurationManagedApplySchedule);
    public const string CancelTenantConfigurationManagedApplySchedule = nameof(CancelTenantConfigurationManagedApplySchedule);
    public const string GetControlPlaneDomains = nameof(GetControlPlaneDomains);
    public const string GetControlPlaneOperations = nameof(GetControlPlaneOperations);
    public const string GetControlPlaneDeploymentModeRunbook = nameof(GetControlPlaneDeploymentModeRunbook);
    public const string TransitionControlPlaneDeploymentMode = nameof(TransitionControlPlaneDeploymentMode);
    public const string GetControlPlaneTenants = nameof(GetControlPlaneTenants);
    public const string GetControlPlaneTenantById = nameof(GetControlPlaneTenantById);
    public const string CreateControlPlaneTenant = nameof(CreateControlPlaneTenant);
    public const string ActivateControlPlaneTenant = nameof(ActivateControlPlaneTenant);
    public const string SuspendControlPlaneTenant = nameof(SuspendControlPlaneTenant);
    public const string ArchiveControlPlaneTenant = nameof(ArchiveControlPlaneTenant);
    public const string ReactivateControlPlaneTenant = nameof(ReactivateControlPlaneTenant);
    public const string ScheduleControlPlaneTenantPurge = nameof(ScheduleControlPlaneTenantPurge);
    public const string GetControlPlaneTenantPlans = nameof(GetControlPlaneTenantPlans);
    public const string GetControlPlaneTenantPlanByKey = nameof(GetControlPlaneTenantPlanByKey);
    public const string CreateControlPlaneTenantPlanDraft = nameof(CreateControlPlaneTenantPlanDraft);
    public const string CreateControlPlaneTenantPlanVersionDraft = nameof(CreateControlPlaneTenantPlanVersionDraft);
    public const string UpdateControlPlaneTenantPlanVersionDraft = nameof(UpdateControlPlaneTenantPlanVersionDraft);
    public const string PublishControlPlaneTenantPlanVersion = nameof(PublishControlPlaneTenantPlanVersion);
    public const string ArchiveControlPlaneTenantPlanVersion = nameof(ArchiveControlPlaneTenantPlanVersion);
    public const string CloneControlPlaneTenantPlan = nameof(CloneControlPlaneTenantPlan);
    public const string ValidateControlPlaneTenantPlanDraft = nameof(ValidateControlPlaneTenantPlanDraft);
    public const string PreviewControlPlaneTenantPlanDiff = nameof(PreviewControlPlaneTenantPlanDiff);
    public const string GetControlPlaneTenantEffectiveConfiguration = nameof(GetControlPlaneTenantEffectiveConfiguration);
    public const string LockControlPlaneTenantSetting = nameof(LockControlPlaneTenantSetting);
    public const string UnlockControlPlaneTenantSetting = nameof(UnlockControlPlaneTenantSetting);
    public const string SetControlPlaneTenantSetting = nameof(SetControlPlaneTenantSetting);
    public const string GetControlPlaneTenantPlanAssignment = nameof(GetControlPlaneTenantPlanAssignment);
    public const string SwitchControlPlaneTenantPlanAssignment = nameof(SwitchControlPlaneTenantPlanAssignment);
    public const string ApplyControlPlaneTenantPlanAssignment = nameof(ApplyControlPlaneTenantPlanAssignment);
    public const string RollbackControlPlaneTenantPlanAssignment = nameof(RollbackControlPlaneTenantPlanAssignment);
    public const string GetInstanceModuleSettings = nameof(GetInstanceModuleSettings);
    public const string GetInstanceAtprotoFederationSettings = nameof(GetInstanceAtprotoFederationSettings);
    public const string UpdateInstanceAtprotoFederationSetting = nameof(UpdateInstanceAtprotoFederationSetting);
    public const string ResetInstanceAtprotoFederationSetting = nameof(ResetInstanceAtprotoFederationSetting);
    public const string LockInstanceAtprotoFederationSetting = nameof(LockInstanceAtprotoFederationSetting);
    public const string UnlockInstanceAtprotoFederationSetting = nameof(UnlockInstanceAtprotoFederationSetting);
    public const string UpdateInstanceModuleSettings = nameof(UpdateInstanceModuleSettings);
    public const string GetInstanceEventPolicy = nameof(GetInstanceEventPolicy);
    public const string UpdateInstanceEventPolicy = nameof(UpdateInstanceEventPolicy);
    public const string GetInstanceOrganizationPolicy = nameof(GetInstanceOrganizationPolicy);
    public const string UpdateInstanceOrganizationPolicy = nameof(UpdateInstanceOrganizationPolicy);
    public const string GetInstanceBrandingSettings = nameof(GetInstanceBrandingSettings);
    public const string UpdateInstanceBrandingSettings = nameof(UpdateInstanceBrandingSettings);
    public const string GetInstanceDomainSettings = nameof(GetInstanceDomainSettings);
    public const string UpdateInstanceDomainSettings = nameof(UpdateInstanceDomainSettings);
    public const string GetInstanceTenantDelegationSettings = nameof(GetInstanceTenantDelegationSettings);
    public const string UpdateInstanceTenantDelegationSettings = nameof(UpdateInstanceTenantDelegationSettings);
    public const string GetInstanceAdminPortalSettings = nameof(GetInstanceAdminPortalSettings);
    public const string UpdateInstanceAdminPortalSettings = nameof(UpdateInstanceAdminPortalSettings);
    public const string UpdateInstanceModerationReportingProviderLocks = nameof(UpdateInstanceModerationReportingProviderLocks);
    public const string GetInstanceAiAssistantGovernanceSettings = nameof(GetInstanceAiAssistantGovernanceSettings);
    public const string UpdateInstanceAiAssistantGovernanceSettings = nameof(UpdateInstanceAiAssistantGovernanceSettings);
    public const string GetInstanceMcpGovernanceSettings = nameof(GetInstanceMcpGovernanceSettings);
    public const string UpdateInstanceMcpGovernanceSettings = nameof(UpdateInstanceMcpGovernanceSettings);
    public const string GetInstanceRenderPolicySettings = nameof(GetInstanceRenderPolicySettings);
    public const string UpdateInstanceRenderPolicySettings = nameof(UpdateInstanceRenderPolicySettings);
    public const string GetInstanceDeploymentMode = nameof(GetInstanceDeploymentMode);
    public const string UpdateInstanceDeploymentMode = nameof(UpdateInstanceDeploymentMode);
    public const string GetInstanceStorageSettings = nameof(GetInstanceStorageSettings);
    public const string UpdateInstanceStorageSettings = nameof(UpdateInstanceStorageSettings);
    public const string TestInstanceStorageConnection = nameof(TestInstanceStorageConnection);
    public const string RecalculateInstanceStorageUsage = nameof(RecalculateInstanceStorageUsage);
    public const string GetInstanceSmtpSettings = nameof(GetInstanceSmtpSettings);
    public const string UpdateInstanceSmtpSettings = nameof(UpdateInstanceSmtpSettings);
    public const string TestInstanceSmtpConnection = nameof(TestInstanceSmtpConnection);
    public const string GetInstanceResolverConfiguration = nameof(GetInstanceResolverConfiguration);
    public const string UpdateInstanceResolverConfiguration = nameof(UpdateInstanceResolverConfiguration);
    public const string GetInstanceAnalyticsGovernanceSettings = nameof(GetInstanceAnalyticsGovernanceSettings);
    public const string UpdateInstanceAnalyticsGovernanceSettings = nameof(UpdateInstanceAnalyticsGovernanceSettings);
    public const string GetInstanceAuthProviderConfiguration = nameof(GetInstanceAuthProviderConfiguration);
    public const string UpdateInstanceAuthProviderConfiguration = nameof(UpdateInstanceAuthProviderConfiguration);
    public const string RunInstanceKeycloakRealmDoctor = nameof(RunInstanceKeycloakRealmDoctor);
    public const string PreviewInstanceKeycloakRealmSync = nameof(PreviewInstanceKeycloakRealmSync);
    public const string ApplyInstanceKeycloakRealmSync = nameof(ApplyInstanceKeycloakRealmSync);
    public const string RotateInstanceKeycloakClientSecret = nameof(RotateInstanceKeycloakClientSecret);
    public const string GetInstanceAuthProviderConfigurationStatus = nameof(GetInstanceAuthProviderConfigurationStatus);
    public const string GetInstanceAuthorizationProviderConfiguration = nameof(GetInstanceAuthorizationProviderConfiguration);
    public const string UpdateInstanceAuthorizationProviderConfiguration = nameof(UpdateInstanceAuthorizationProviderConfiguration);
    public const string GetInstanceAuthorizationProviderConfigurationStatus = nameof(GetInstanceAuthorizationProviderConfigurationStatus);
    public const string SyncInstanceAuthorizationPolicyPackage = nameof(SyncInstanceAuthorizationPolicyPackage);
    public const string DownloadInstanceAuthorizationPolicyPackage = nameof(DownloadInstanceAuthorizationPolicyPackage);
    public const string GetInstanceAuthorizationPolicyPackageStatus = nameof(GetInstanceAuthorizationPolicyPackageStatus);

    #endregion

    #region System Routes

    public const string GetSystemOnboardingStatus = nameof(GetSystemOnboardingStatus);
    public const string GetSystemOnboardingPreflight = nameof(GetSystemOnboardingPreflight);

    #endregion

    #region Instance Onboarding Routes

    public const string GetInstanceOnboardingStatus = nameof(GetInstanceOnboardingStatus);
    public const string SaveInstanceOnboardingProfile = nameof(SaveInstanceOnboardingProfile);
    public const string CompleteInstanceOnboarding = nameof(CompleteInstanceOnboarding);
    public const string ValidateInstanceSetupSecret = nameof(ValidateInstanceSetupSecret);
    public const string GetInstanceOnboardingAuthProviderConfiguration = nameof(GetInstanceOnboardingAuthProviderConfiguration);
    public const string GetInstanceOnboardingAuthProviderConfigurationInternal = nameof(GetInstanceOnboardingAuthProviderConfigurationInternal);
    public const string BootstrapInstanceOnboardingKeycloakRealm = nameof(BootstrapInstanceOnboardingKeycloakRealm);
    public const string GetInstanceOnboardingAuthorizationProviderConfigurationInternal = nameof(GetInstanceOnboardingAuthorizationProviderConfigurationInternal);
    public const string VerifyInstanceOnboardingAuthorizationProviderEndpoint = nameof(VerifyInstanceOnboardingAuthorizationProviderEndpoint);
    public const string SyncInstanceOnboardingAuthorizationPolicyPackage = nameof(SyncInstanceOnboardingAuthorizationPolicyPackage);
    public const string DownloadInstanceOnboardingAuthorizationPolicyPackage = nameof(DownloadInstanceOnboardingAuthorizationPolicyPackage);

    #endregion

    #region Tenant Onboarding Routes

    public const string GetTenantOnboardingStatus = nameof(GetTenantOnboardingStatus);
    public const string GetTenantOnboardingPolicySettings = nameof(GetTenantOnboardingPolicySettings);
    public const string CompleteTenantOnboarding = nameof(CompleteTenantOnboarding);
    public const string SaveTenantOnboardingStepProgress = nameof(SaveTenantOnboardingStepProgress);

    #endregion

    #region Configuration Manifest Routes

    public const string ExportConfigurationManifest = nameof(ExportConfigurationManifest);

    #endregion
}
