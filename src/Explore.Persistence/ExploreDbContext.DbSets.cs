// ABOUTME: Partial class containing all DbSet property declarations for the Explore platform.
// ABOUTME: Organized by domain area: Tenancy, Users, Auth, Actors, Organizations, Groups, Events, etc.

using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Federation;
using Explore.Domain.Modules;
using Explore.Domain.Secrets;
using Explore.Domain.SetupLive;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Views;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Persistence.Entities;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StorageObject = Explore.Domain.StorageObject;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    public DbSet<SetupTargetEnrollment> SetupTargetEnrollments { get; set; }
    public DbSet<SetupEnrollmentIssuanceClaim> SetupEnrollmentIssuanceClaims { get; set; }
    public DbSet<SetupSecretBindingOperation> SetupSecretBindingOperations { get; set; }

    public DbSet<ConfigurationImportSession> ConfigurationImportSessions { get; set; }
    public DbSet<ConfigurationImportStoredArtifact> ConfigurationImportArtifacts { get; set; }
    public DbSet<ConfigurationImportOperation> ConfigurationImportOperations { get; set; }
    public DbSet<ConfigurationDirectTransferSession> ConfigurationDirectTransferSessions { get; set; }
    public DbSet<ConfigurationDirectTransferChunk> ConfigurationDirectTransferChunks { get; set; }

    // ===== Multi-tenancy =====
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantUser> TenantUsers { get; set; }
    public DbSet<TenantUserProfile> TenantUserProfiles { get; set; }
    public DbSet<TenantUserRoleGrant> TenantUserRoleGrants { get; set; }
    public DbSet<TenantOnboardingState> TenantOnboardingStates { get; set; }
    public DbSet<TenantInvitation> TenantInvitations { get; set; }
    public DbSet<TenantLifecycleLog> TenantLifecycleLogs { get; set; }
    public DbSet<PlatformUserRole> PlatformUserRoles { get; set; }
    public DbSet<TenantNavigationLink> TenantNavigationLinks { get; set; }
    public DbSet<TenantFooterLinkGroup> TenantFooterLinkGroups { get; set; }
    public DbSet<TenantFooterLink> TenantFooterLinks { get; set; }
    public DbSet<TenantPlan> TenantPlans { get; set; }
    public DbSet<TenantPlanVersion> TenantPlanVersions { get; set; }
    public DbSet<TenantPlanVersionSetting> TenantPlanVersionSettings { get; set; }
    public DbSet<TenantPlanVersionQuota> TenantPlanVersionQuotas { get; set; }
    public DbSet<TenantPlanAssignment> TenantPlanAssignments { get; set; }
    public DbSet<TenantPlanApplicationLog> TenantPlanApplicationLogs { get; set; }
    public DbSet<InstanceBootstrapState> InstanceBootstrapStates { get; set; }
    public DbSet<SupportAccessSession> SupportAccessSessions { get; set; }
    public DbSet<SupportAccessAuditEvent> SupportAccessAuditEvents { get; set; }
    public DbSet<SupportAccessSessionStatus> SupportAccessSessionStatuses { get; set; }
    public DbSet<SupportAccessMode> SupportAccessModes { get; set; }
    public DbSet<SupportAccessEndReason> SupportAccessEndReasons { get; set; }
    public DbSet<SupportAccessAuditEventType> SupportAccessAuditEventTypes { get; set; }

    // ===== Governance Policy Aggregates =====
    public DbSet<Explore.Domain.Policies.InstancePolicySet> InstancePolicySets { get; set; }
    public DbSet<Explore.Domain.Policies.TenantPolicySet> TenantPolicySets { get; set; }
    public DbSet<Explore.Domain.Policies.OrganizationPolicySet> OrganizationPolicySets { get; set; }
    public DbSet<Explore.Domain.Policies.PolicyChangeOutbox> PolicyChangeOutbox { get; set; }

    // ===== Users & Authentication =====
    public DbSet<User> Users { get; set; }
    public DbSet<UserPii> UserPii { get; set; }
    public DbSet<AuthenticationProvider> AuthenticationProviders { get; set; }
    public DbSet<ExternalApiKey> ExternalApiKeys { get; set; }
    public DbSet<ExternalApiKeyOwnerTypeLookup> ExternalApiKeyOwnerTypes { get; set; }
    public DbSet<ExternalApiKeyStatus> ExternalApiKeyStatuses { get; set; }
    public DbSet<ExternalApiKeyCreditPeriod> ExternalApiKeyCreditPeriods { get; set; }
    public DbSet<ExternalApiKeyQuota> ExternalApiKeyQuotas { get; set; }
    public DbSet<UserAuthenticationToken> UserAuthenticationTokens { get; set; }
    public DbSet<UserExternalLogin> UserExternalLogins { get; set; }
    public DbSet<LocalIdentityUser> LocalIdentityUsers { get; set; }
    public DbSet<LocalIdentityRole> LocalIdentityRoles { get; set; }
    public DbSet<IdentityUserRole<Guid>> IdentityUserRoles { get; set; }
    public DbSet<IdentityUserClaim<Guid>> IdentityUserClaims { get; set; }
    public DbSet<IdentityRoleClaim<Guid>> IdentityRoleClaims { get; set; }
    public DbSet<IdentityUserLogin<Guid>> IdentityUserLogins { get; set; }
    public DbSet<IdentityUserToken<Guid>> IdentityUserTokens { get; set; }
    public DbSet<ExternalBinding> ExternalBindings { get; set; }
    public DbSet<ManagedControlPlaneRegistration> ManagedControlPlaneRegistrations { get; set; }
    public DbSet<ManagedTenantProvisioningOperation> ManagedTenantProvisioningOperations { get; set; }
    public DbSet<ConfigurationManifestOperation> ConfigurationManifestOperations { get; set; }
    public DbSet<ConfigurationManifestTenantResult> ConfigurationManifestTenantResults { get; set; }
    public DbSet<LegalDocument> LegalDocuments { get; set; }
    public DbSet<LegalDocumentVersion> LegalDocumentVersions { get; set; }
    public DbSet<LegalDocumentLocalizedSource> LegalDocumentLocalizedSources { get; set; }
    public DbSet<LegalDocumentPublication> LegalDocumentPublications { get; set; }

    // ===== Authorization (RBAC) =====
    public DbSet<Role> Roles { get; set; }
    public DbSet<RoleScope> RoleScopes { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<EventRoleAssignment> EventRoleAssignments { get; set; }

    // ===== Actors (Federation/ATProto) =====
    public DbSet<Actor> Actors { get; set; }
    public DbSet<ActorPii> ActorPii { get; set; }
    public DbSet<AtprotoIdentity> AtprotoIdentities { get; set; }
    public DbSet<ExternalActorSubject> ExternalActorSubjects { get; set; }
    public DbSet<ServicePrincipal> ServicePrincipals { get; set; }
    public DbSet<ActorMerge> ActorMerges { get; set; }
    public DbSet<ActorModerationRecord> ActorModerationRecords { get; set; }
    public DbSet<AtprotoIdentityModerationRecord> AtprotoIdentityModerationRecords { get; set; }
    public DbSet<ActorType> ActorTypes { get; set; }
    public DbSet<DidCustodyType> DidCustodyTypes { get; set; }
    public DbSet<ActorKeyStore> ActorKeyStores { get; set; }
    public DbSet<ActorSubscription> ActorSubscriptions { get; set; }
    public DbSet<ActorSubscriptionStatus> ActorSubscriptionStatuses { get; set; }
    public DbSet<ActorSubscriptionNotificationLevel> ActorSubscriptionNotificationLevels { get; set; }

    // ===== Organizations =====
    public DbSet<Organization> Organizations { get; set; }
    public DbSet<OrganizationTenant> OrganizationTenants { get; set; }
    public DbSet<OrganizationTenantEvidence> OrganizationTenantEvidence { get; set; }
    public DbSet<OrganizationPii> OrganizationPii { get; set; }
    public DbSet<OrganizationMember> OrganizationMembers { get; set; }
    public DbSet<OrganizationPosition> OrganizationPositions { get; set; }
    public DbSet<OrganizationReview> OrganizationReviews { get; set; }

    // ===== Group Entities =====
    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupTenant> GroupTenants { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }
    public DbSet<GroupPosition> GroupPositions { get; set; }

    // ===== Custom Properties (EAV) =====
    public DbSet<CustomPropertyDefinition> CustomPropertyDefinitions { get; set; }
    public DbSet<CustomPropertyOption> CustomPropertyOptions { get; set; }
    public DbSet<CustomPropertyValue> CustomPropertyValues { get; set; }
    public DbSet<EventTemplate> EventTemplates { get; set; }
    public DbSet<EventTemplateCustomPropertyDefinition> EventTemplateCustomPropertyDefinitions { get; set; }
    public DbSet<EventTemplateCustomPropertyOption> EventTemplateCustomPropertyOptions { get; set; }
    public DbSet<EventCustomPropertyDefinition> EventCustomPropertyDefinitions { get; set; }
    public DbSet<EventCustomPropertyOption> EventCustomPropertyOptions { get; set; }
    public DbSet<EventCustomPropertyValue> EventCustomPropertyValues { get; set; }
    public DbSet<EventCustomPropertyProjection> EventCustomPropertyProjections { get; set; }
    public DbSet<EventWithSessionsView> EventsWithSessions => Set<EventWithSessionsView>();
    public DbSet<CustomPropertyProjectionStatus> CustomPropertyProjectionStatuses { get; set; }
    public DbSet<CustomPropertyProjectionDirtyScope> CustomPropertyProjectionDirtyScopes { get; set; }

    // ===== Events =====
    public DbSet<Event> Events { get; set; }
    public DbSet<EventParticipationConfiguration> EventParticipationConfigurations { get; set; }
    public DbSet<ParticipationRequirementAttachment> ParticipationRequirementAttachments { get; set; }
    public DbSet<EventPublicAction> EventPublicActions { get; set; }
    public DbSet<EventOrganizerClaim> EventOrganizerClaims { get; set; }
    public DbSet<EventSession> EventSessions { get; set; }
    public DbSet<EventSessionGroup> EventSessionGroups { get; set; }
    public DbSet<EventSessionGroupSession> EventSessionGroupSessions { get; set; }
    public DbSet<EventSessionIslamicAspect> EventSessionIslamicAspects { get; set; }
    public DbSet<EventRegistration> EventRegistrations { get; set; }
    public DbSet<EventModerationRecord> EventModerationRecords { get; set; }
    public DbSet<EventReport> EventReports { get; set; }
    public DbSet<EventReportTarget> EventReportTargets { get; set; }
    public DbSet<EventReportEvidence> EventReportEvidenceItems { get; set; }
    public DbSet<EventReportCase> EventReportCases { get; set; }
    public DbSet<EventReportSignal> EventReportSignals { get; set; }
    public DbSet<EventReportDecision> EventReportDecisions { get; set; }
    public DbSet<EventReportDecisionExecution> EventReportDecisionExecutions { get; set; }
    public DbSet<EventReportExternalLink> EventReportExternalLinks { get; set; }
    public DbSet<EventSessionLanguage> EventSessionLanguages { get; set; }
    public DbSet<EventSessionSpeaker> EventSessionSpeakers { get; set; }
    public DbSet<EventSessionAgendaItem> EventSessionAgendaItems { get; set; }
    public DbSet<EventSessionTemplate> EventSessionTemplates { get; set; }
    public DbSet<EventSessionTemplateCustomPropertyDefinition> EventSessionTemplateCustomPropertyDefinitions { get; set; }
    public DbSet<EventSessionTemplateCustomPropertyOption> EventSessionTemplateCustomPropertyOptions { get; set; }
    public DbSet<EventSessionCustomPropertyDefinition> EventSessionCustomPropertyDefinitions { get; set; }
    public DbSet<EventSessionCustomPropertyOption> EventSessionCustomPropertyOptions { get; set; }
    public DbSet<EventSessionCustomPropertyValue> EventSessionCustomPropertyValues { get; set; }
    public DbSet<EventSessionCustomPropertyProjection> EventSessionCustomPropertyProjections { get; set; }
    public DbSet<EventIslamicAspect> EventIslamicAspects { get; set; }
    public DbSet<EventTechAspect> EventTechAspects { get; set; }

    // ===== Event Scheduling Refactor (Phase 1 additive) =====
    public DbSet<EventDay> EventDays { get; set; }
    public DbSet<EventAgendaItem> EventAgendaItems { get; set; }
    public DbSet<LocationRoom> LocationRooms { get; set; }
    public DbSet<EventSessionCategory> EventSessionCategories { get; set; }
    public DbSet<EventSessionTag> EventSessionTags { get; set; }
    public DbSet<EventTicketCatalogVersion> EventTicketCatalogVersions { get; set; }
    public DbSet<EventTicketType> EventTicketTypes { get; set; }
    public DbSet<EventAddOnCatalogVersion> EventAddOnCatalogVersions { get; set; }
    public DbSet<EventAddOnCatalogItem> EventAddOnCatalogItems { get; set; }
    public DbSet<TicketTypeEntitlement> TicketTypeEntitlements { get; set; }
    public DbSet<EventCapacityPool> EventCapacityPools { get; set; }
    public DbSet<RegistrationOrder> RegistrationOrders { get; set; }
    public DbSet<TicketPurchasePolicyVersion> TicketPurchasePolicyVersions { get; set; }
    public DbSet<TicketPurchaseAuthorityUsage> TicketPurchaseAuthorityUsages { get; set; }
    public DbSet<TicketPurchaseOperation> TicketPurchaseOperations { get; set; }
    public DbSet<RegistrationOrderLine> RegistrationOrderLines { get; set; }
    public DbSet<RegistrationOrderAddOnLine> RegistrationOrderAddOnLines { get; set; }
    public DbSet<EventAddOnInventoryAllocation> EventAddOnInventoryAllocations { get; set; }
    public DbSet<EventAddOnFulfillment> EventAddOnFulfillments { get; set; }
    public DbSet<EventAddOnRefundAllocation> EventAddOnRefundAllocations { get; set; }
    public DbSet<TicketingRecoveryCheckpoint> TicketingRecoveryCheckpoints { get; set; }
    public DbSet<TicketingRecoveryReissueIntent> TicketingRecoveryReissueIntents { get; set; }
    public DbSet<RegistrationOrderPii> RegistrationOrderPii { get; set; }
    public DbSet<RegistrationOrderPlatformContribution> RegistrationOrderPlatformContributions { get; set; }
    public DbSet<PromotionDefinition> PromotionDefinitions { get; set; }
    public DbSet<PromotionCode> PromotionCodes { get; set; }
    public DbSet<PromotionReservation> PromotionReservations { get; set; }
    public DbSet<RegistrationInventoryHold> RegistrationInventoryHolds { get; set; }
    public DbSet<RegistrationParticipant> RegistrationParticipants { get; set; }
    public DbSet<RegistrationParticipantPii> RegistrationParticipantPii { get; set; }
    public DbSet<RegistrationTicketAssignment> RegistrationTicketAssignments { get; set; }
    public DbSet<RegistrationAmendment> RegistrationAmendments { get; set; }
    public DbSet<RegistrationWorkflow> RegistrationWorkflows { get; set; }
    public DbSet<RegistrationRequirement> RegistrationRequirements { get; set; }
    public DbSet<RegistrationChannel> RegistrationChannels { get; set; }
    public DbSet<RegistrationProviderConnection> RegistrationProviderConnections { get; set; }
    public DbSet<RegistrationProviderApprovedOrigin> RegistrationProviderApprovedOrigins { get; set; }
    public DbSet<RegistrationProviderBinding> RegistrationProviderBindings { get; set; }
    public DbSet<RegistrationProviderSubscriptionState> RegistrationProviderSubscriptionStates { get; set; }
    public DbSet<RegistrationProviderCapability> RegistrationProviderCapabilities { get; set; }
    public DbSet<RegistrationProviderFieldMapping> RegistrationProviderFieldMappings { get; set; }
    public DbSet<RegistrationProviderOptionMapping> RegistrationProviderOptionMappings { get; set; }
    public DbSet<RegistrationProviderSchemaRevision> RegistrationProviderSchemaRevisions { get; set; }
    public DbSet<RegistrationForm> RegistrationForms { get; set; }
    public DbSet<RegistrationFormTemplate> RegistrationFormTemplates { get; set; }
    public DbSet<RegistrationFormVersion> RegistrationFormVersions { get; set; }
    public DbSet<RegistrationFormSection> RegistrationFormSections { get; set; }
    public DbSet<RegistrationFormField> RegistrationFormFields { get; set; }
    public DbSet<RegistrationFormFieldOption> RegistrationFormFieldOptions { get; set; }
    public DbSet<RegistrationFormRule> RegistrationFormRules { get; set; }
    public DbSet<RegistrationAttempt> RegistrationAttempts { get; set; }
    public DbSet<RegistrationSubmission> RegistrationSubmissions { get; set; }
    public DbSet<RegistrationSubmissionRevision> RegistrationSubmissionRevisions { get; set; }
    public DbSet<RegistrationSubmissionIssue> RegistrationSubmissionIssues { get; set; }
    public DbSet<RegistrationAnswer> RegistrationAnswers { get; set; }
    public DbSet<RegistrationConsentRecord> RegistrationConsentRecords { get; set; }
    public DbSet<RegistrationAnswerFile> RegistrationAnswerFiles { get; set; }
    public DbSet<RegistrationAnswerFileRelease> RegistrationAnswerFileReleases { get; set; }
    public DbSet<RegistrationSensitiveAnswerValue> RegistrationSensitiveAnswerValues { get; set; }
    public DbSet<RegistrationRequirementFulfillment> RegistrationRequirementFulfillments { get; set; }
    public DbSet<ParticipantAdmissionEligibility> ParticipantAdmissionEligibilities { get; set; }
    public DbSet<TicketTransferPolicy> TicketTransferPolicies { get; set; }
    public DbSet<AdmissionTicketTransfer> AdmissionTicketTransfers { get; set; }
    public DbSet<AdmissionTransferDeliveryIntent> AdmissionTransferDeliveryIntents { get; set; }
    public DbSet<FairReturnSupplyPolicy> FairReturnSupplyPolicies { get; set; }
    public DbSet<FairReturnSupplyUnit> FairReturnSupplyUnits { get; set; }
    public DbSet<EventWaitlistEntry> EventWaitlistEntries { get; set; }
    public DbSet<EventWaitlistOffer> EventWaitlistOffers { get; set; }
    public DbSet<FairReturnSourceBinding> FairReturnSourceBindings { get; set; }
    public DbSet<WaitlistProviderObservation> WaitlistProviderObservations { get; set; }
    public DbSet<WaitlistPaymentIntent> WaitlistPaymentIntents { get; set; }
    public DbSet<WaitlistRefundIntent> WaitlistRefundIntents { get; set; }
    public DbSet<FairReturnOrchestrationEffect> FairReturnOrchestrationEffects { get; set; }
    public DbSet<RegistrationFinalizationEffect> RegistrationFinalizationEffects { get; set; }
    public DbSet<AdmissionTicket> AdmissionTickets { get; set; }
    public DbSet<AdmissionTicketCredential> AdmissionTicketCredentials { get; set; }
    public DbSet<AdmissionTarget> AdmissionTargets { get; set; }
    public DbSet<AdmissionCheckInPolicy> AdmissionCheckInPolicies { get; set; }
    public DbSet<AdmissionCheckInEvent> AdmissionCheckInEvents { get; set; }
    public DbSet<AdmissionCheckInState> AdmissionCheckInStates { get; set; }
    public DbSet<AdmissionScannerCapability> AdmissionScannerCapabilities { get; set; }
    public DbSet<AdmissionRecoveryCapability> AdmissionRecoveryCapabilities { get; set; }
    public DbSet<AdmissionRecoveryRequestIntent> AdmissionRecoveryRequestIntents { get; set; }
    public DbSet<AdmissionRecoveryDeliveryIntent> AdmissionRecoveryDeliveryIntents { get; set; }
    public DbSet<Explore.Application.Contracts.Admissions.AdmissionDeliveryIntent> AdmissionDeliveryIntents { get; set; }
    public DbSet<RegistrationProviderSubmissionWriteEffect> RegistrationProviderSubmissionWriteEffects { get; set; }
    public DbSet<RegistrationAnswerSubjectType> RegistrationAnswerSubjectTypes { get; set; }
    public DbSet<ContactShareConsentSubjectType> ContactShareConsentSubjectTypes { get; set; }
    public DbSet<RegistrationRetentionPolicy> RegistrationRetentionPolicies { get; set; }

    // ===== Event Lookup Tables =====
    public DbSet<EventType> EventTypes { get; set; }
    public DbSet<ParticipationHandlingMode> ParticipationHandlingModes { get; set; }
    public DbSet<AdvanceRegistrationObligation> AdvanceRegistrationObligations { get; set; }
    public DbSet<IdentityAccessMode> IdentityAccessModes { get; set; }
    public DbSet<EventProvenanceType> EventProvenanceTypes { get; set; }
    public DbSet<EventPublicActionKind> EventPublicActionKinds { get; set; }
    public DbSet<EventPublicActionHealthState> EventPublicActionHealthStates { get; set; }
    public DbSet<EventOrganizerClaimStatus> EventOrganizerClaimStatuses { get; set; }
    public DbSet<EventStatus> EventStatuses { get; set; }
    public DbSet<EventSessionStatus> EventSessionStatuses { get; set; }
    public DbSet<EventFormat> EventFormats { get; set; }
    public DbSet<VisibilityType> VisibilityTypes { get; set; }
    public DbSet<RegistrationMode> RegistrationModes { get; set; }
    public DbSet<EventSessionKind> EventSessionKinds { get; set; }
    public DbSet<ScheduleItemKind> ScheduleItemKinds { get; set; }
    public DbSet<EventRegistrationPolicy> EventRegistrationPolicies { get; set; }
    public DbSet<RegistrationScope> RegistrationScopes { get; set; }
    public DbSet<TicketCatalogStatus> TicketCatalogStatuses { get; set; }
    public DbSet<TicketPricingMode> TicketPricingModes { get; set; }
    public DbSet<ParticipantDataCollectionMode> ParticipantDataCollectionModes { get; set; }
    public DbSet<EntitlementScopeType> EntitlementScopeTypes { get; set; }
    public DbSet<EntitlementSelectionRule> EntitlementSelectionRules { get; set; }
    public DbSet<CapacityHoldPolicy> CapacityHoldPolicies { get; set; }
    public DbSet<CapacityOversellPolicy> CapacityOversellPolicies { get; set; }
    public DbSet<BookingPartyType> BookingPartyTypes { get; set; }
    public DbSet<ParticipantType> ParticipantTypes { get; set; }
    public DbSet<AssignmentStatus> AssignmentStatuses { get; set; }
    public DbSet<RegistrationOrderStatus> RegistrationOrderStatuses { get; set; }
    public DbSet<AdmissionTicketStatus> AdmissionTicketStatuses { get; set; }
    public DbSet<AdmissionTicketCredentialStatus> AdmissionTicketCredentialStatuses { get; set; }
    public DbSet<AdmissionTicketTransitionReason> AdmissionTicketTransitionReasons { get; set; }
    public DbSet<RegistrationInventoryHoldStatus> RegistrationInventoryHoldStatuses { get; set; }
    public DbSet<PromotionDefinitionStatus> PromotionDefinitionStatuses { get; set; }
    public DbSet<PromotionReservationStatus> PromotionReservationStatuses { get; set; }
    public DbSet<RegistrationRequirementCriticality> RegistrationRequirementCriticalities { get; set; }
    public DbSet<RegistrationRequirementCompletionEffect> RegistrationRequirementCompletionEffects { get; set; }
    public DbSet<RegistrationAnswerSyncMode> RegistrationAnswerSyncModes { get; set; }
    public DbSet<RegistrationRequirementSubjectType> RegistrationRequirementSubjectTypes { get; set; }
    public DbSet<RegistrationFormStatus> RegistrationFormStatuses { get; set; }
    public DbSet<RegistrationFormVersionSourceKind> RegistrationFormVersionSourceKinds { get; set; }
    public DbSet<RegistrationFieldType> RegistrationFieldTypes { get; set; }
    public DbSet<RegistrationOrganizerVisibility> RegistrationOrganizerVisibilities { get; set; }
    public DbSet<RegistrationAttemptStatus> RegistrationAttemptStatuses { get; set; }
    public DbSet<RegistrationSubmissionStatus> RegistrationSubmissionStatuses { get; set; }
    public DbSet<RegistrationProviderKind> RegistrationProviderKinds { get; set; }
    public DbSet<RegistrationProviderDeploymentKind> RegistrationProviderDeploymentKinds { get; set; }
    public DbSet<RegistrationProviderSchemaAuthority> RegistrationProviderSchemaAuthorities { get; set; }
    public DbSet<RegistrationProviderPresentationMode> RegistrationProviderPresentationModes { get; set; }
    public DbSet<RegistrationProviderCollectionMode> RegistrationProviderCollectionModes { get; set; }
    public DbSet<RegistrationProviderCompletionMode> RegistrationProviderCompletionModes { get; set; }
    public DbSet<RegistrationProviderTrustLevel> RegistrationProviderTrustLevels { get; set; }
    public DbSet<RegistrationProviderDriftClass> RegistrationProviderDriftClasses { get; set; }
    public DbSet<RegistrationProviderBindingState> RegistrationProviderBindingStates { get; set; }

    // ===== Instance Monetization =====
    public DbSet<PaidEventPolicyVersion> PaidEventPolicyVersions { get; set; }
    public DbSet<PaidEventPolicyAllowedOrganizerKind> PaidEventPolicyAllowedOrganizerKinds { get; set; }
    public DbSet<PaidEventPolicyAllowedCurrency> PaidEventPolicyAllowedCurrencies { get; set; }
    public DbSet<PaidEventPolicyRefundProtection> PaidEventPolicyRefundProtections { get; set; }
    public DbSet<PaidEventPolicyCurrencyRiskLimitRow> PaidEventPolicyCurrencyRiskLimits { get; set; }
    public DbSet<OrganizerPaymentProviderAccountOperation> OrganizerPaymentProviderAccountOperations { get; set; }
    public DbSet<OrganizerPaymentProviderConnection> OrganizerPaymentProviderConnections { get; set; }
    public DbSet<OrganizerPaymentProviderConnectionSupportedCurrency> OrganizerPaymentProviderConnectionSupportedCurrencies { get; set; }
    public DbSet<PaidCheckoutSaleControl> PaidCheckoutSaleControls { get; set; }
    public DbSet<PaidCheckoutSaleControlAudit> PaidCheckoutSaleControlAudits { get; set; }
    public DbSet<PaidCheckoutReviewApproval> PaidCheckoutReviewApprovals { get; set; }
    public DbSet<PaidOrderAcceptanceSnapshot> PaidOrderAcceptanceSnapshots { get; set; }
    public DbSet<PaidOrderAcceptanceLine> PaidOrderAcceptanceLines { get; set; }
    public DbSet<PaymentAttempt> PaymentAttempts { get; set; }
    public DbSet<PaymentAttemptStatus> PaymentAttemptStatuses { get; set; }
    public DbSet<CheckoutDispatchEffect> CheckoutDispatchEffects { get; set; }
    public DbSet<PaymentReconciliationEffect> PaymentReconciliationEffects { get; set; }
    public DbSet<PaymentSucceededObservation> PaymentSucceededObservations { get; set; }
    public DbSet<RefundAttempt> RefundAttempts { get; set; }
    public DbSet<RefundLineAllocation> RefundLineAllocations { get; set; }
    public DbSet<RefundCampaign> RefundCampaigns { get; set; }
    public DbSet<RegistrationMaterialChangeChoice> RegistrationMaterialChangeChoices { get; set; }
    public DbSet<PaymentDispute> PaymentDisputes { get; set; }
    public DbSet<PlatformFeePolicy> PlatformFeePolicies { get; set; }
    public DbSet<PlatformFeeFixedCharge> PlatformFeeFixedCharges { get; set; }
    public DbSet<PlatformContributionSetting> PlatformContributionSettings { get; set; }
    public DbSet<PlatformContributionOption> PlatformContributionOptions { get; set; }

    // ===== Event Metadata =====
    public DbSet<AudienceAge> AudienceAges { get; set; }
    public DbSet<AudienceGender> AudienceGenders { get; set; }
    public DbSet<Madhab> Madhabs { get; set; }
    public DbSet<Language> Languages { get; set; }
    public DbSet<ApprovalStatus> ApprovalStatuses { get; set; }
    public DbSet<TenantStatus> TenantStatuses { get; set; }
    public DbSet<TenantPlanStatus> TenantPlanStatuses { get; set; }
    public DbSet<TenantPlanAssignmentStatus> TenantPlanAssignmentStatuses { get; set; }
    public DbSet<TenantPlanApplicationStatus> TenantPlanApplicationStatuses { get; set; }
    public DbSet<AnalyticsProvider> AnalyticsProviders { get; set; }

    // ===== Categories & Tags =====
    public DbSet<Category> Categories { get; set; }
    public DbSet<CategoryType> CategoryTypes { get; set; }
    public DbSet<CategoryTypeCategories> CategoryTypeCategories { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<TagType> TagTypes { get; set; }
    public DbSet<TagTypeTags> TagTypeTags { get; set; }
    public DbSet<EventCategories> EventCategories { get; set; }
    public DbSet<EventTags> EventTags { get; set; }

    // ===== Locations =====
    public DbSet<Location> Locations { get; set; }
    public DbSet<LocationPii> LocationPii { get; set; }
    public DbSet<LocationKind> LocationKinds { get; set; }
    public DbSet<LocationPrivacyState> LocationPrivacyStates { get; set; }
    public DbSet<LocationDisclosureAudience> LocationDisclosureAudiences { get; set; }
    public DbSet<LocationAddressSource> LocationAddressSources { get; set; }
    public DbSet<LocationAddressVisibility> LocationAddressVisibilities { get; set; }
    public DbSet<EventLocation> EventLocations { get; set; }
    public DbSet<EventLocationDisclosureAudit> EventLocationDisclosureAudits { get; set; }
    public DbSet<EventLocationExactReadAudit> EventLocationExactReadAudits { get; set; }
    public DbSet<PrivacyErasureReplayCheckpoint> PrivacyErasureReplayCheckpoints { get; set; }
    public DbSet<PrivacyErasureSaga> PrivacyErasureSagas { get; set; }
    public DbSet<PrivacyErasurePolicyCoverage> PrivacyErasurePolicyCoverage { get; set; }
    public DbSet<PrivacyErasureProviderWork> PrivacyErasureProviderWork { get; set; }

    // ===== Storage =====
    public DbSet<StorageObject> StorageObjects { get; set; }
    public DbSet<StorageUploadSession> StorageUploadSessions { get; set; }
    public DbSet<StorageUsageCounter> StorageUsageCounters { get; set; }
    public DbSet<FileType> FileTypes { get; set; }
    public DbSet<OwnerType> OwnerTypes { get; set; }

    // ===== Settings =====
    public DbSet<SystemSetting> SystemSettings { get; set; }
    public DbSet<SettingScopeLookup> SettingScopes { get; set; }
    public DbSet<SettingValueTypeLookup> SettingValueTypes { get; set; }
    public DbSet<TenantSetting> TenantSettingOverrides { get; set; }
    public DbSet<TenantSettingsDocument> TenantSettingsDocuments { get; set; }
    public DbSet<OrganizationSetting> OrganizationSettingOverrides { get; set; }
    public DbSet<GroupSetting> GroupSettingOverrides { get; set; }
    public DbSet<UserPreference> UserPreferences { get; set; }
    public DbSet<UserNotificationPreference> UserNotificationPreferences { get; set; }
    public DbSet<NotificationChannelPreference> NotificationChannelPreferences { get; set; }
    public DbSet<NotificationPreferenceProfile> NotificationPreferenceProfiles { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<SecretBinding> SecretBindings { get; set; }
    public DbSet<SecretSourceTypeLookup> SecretSourceTypes { get; set; }
    public DbSet<SecretValidationStatus> SecretValidationStatuses { get; set; }
    public DbSet<ConfigurationChangeLog> ConfigurationChangeLogs { get; set; }
    public DbSet<UiTheme> UiThemes { get; set; }
    public DbSet<UiThemePreset> UiThemePresets { get; set; }
    public DbSet<UserAppearanceProfile> UserAppearanceProfiles { get; set; }
    public DbSet<UserAppearancePreference> UserAppearancePreferences { get; set; }

    // ===== Audit & Notifications =====
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<NotificationType> NotificationTypes { get; set; }
    public DbSet<NotificationEntityType> NotificationEntityTypes { get; set; }
    public DbSet<NotificationReason> NotificationReasons { get; set; }
    public DbSet<NotificationScopeType> NotificationScopeTypes { get; set; }
    public DbSet<NotificationFanoutRun> NotificationFanoutRuns { get; set; }
    public DbSet<NotificationFanoutOccurrence> NotificationFanoutOccurrences { get; set; }
    public DbSet<NotificationFanoutProcessorState> NotificationFanoutProcessorStates { get; set; }
    public DbSet<NotificationPreferenceCategory> NotificationPreferenceCategories { get; set; }
    public DbSet<NotificationPreferenceChannel> NotificationPreferenceChannels { get; set; }
    public DbSet<WebPushSubscription> WebPushSubscriptions { get; set; }
    public DbSet<WebPushDispatchOutbox> WebPushDispatchOutbox { get; set; }
    public DbSet<NotificationIntent> NotificationIntents { get; set; }
    public DbSet<NotificationCategory> NotificationCategories { get; set; }
    public DbSet<NotificationOwnershipType> NotificationOwnershipTypes { get; set; }
    public DbSet<NotificationIntentStatus> NotificationIntentStatuses { get; set; }
    public DbSet<NotificationRecipientKind> NotificationRecipientKinds { get; set; }
    public DbSet<NotificationDelivery> NotificationDeliveries { get; set; }
    public DbSet<NotificationDeliveryStatus> NotificationDeliveryStatuses { get; set; }
    public DbSet<NotificationExternalDelegation> NotificationExternalDelegations { get; set; }
    public DbSet<NotificationExternalDelegationStatus> NotificationExternalDelegationStatuses { get; set; }
    public DbSet<ExternalWorkflowProviderKindLookup> ExternalWorkflowProviderKinds { get; set; }
    public DbSet<AccountAuthorityKindLookup> AccountAuthorityKinds { get; set; }

    // ===== Module Governance =====
    public DbSet<ModuleDefinition> ModuleDefinitions { get; set; }
    public DbSet<TenantCapability> TenantCapabilities { get; set; }

    // ===== Federation/Indexer (ATProto) =====
    public DbSet<SyncState> SyncStates { get; set; }
    public DbSet<AtprotoRecord> AtprotoRecords { get; set; }
    public DbSet<AtprotoEventProjection> AtprotoEventProjections { get; set; }
    public DbSet<AtprotoRecordTenantPresentation> AtprotoRecordTenantPresentations { get; set; }
    public DbSet<AtprotoOutboundRecordOwnership> AtprotoOutboundRecordOwnerships { get; set; }
    public DbSet<AtprotoJetstreamConsumerState> AtprotoJetstreamConsumerStates { get; set; }
    public DbSet<AtprotoJetstreamQuarantine> AtprotoJetstreamQuarantines { get; set; }

    // ===== PDS Synchronization (Outbox Pattern) =====
    public DbSet<PdsSyncOutbox> PdsSyncOutbox { get; set; }

    // ===== Generic Outbox (cross-process side effects) =====
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    // ===== Email Dispatch Outbox (Basic Dispatch Mode) =====
    public DbSet<EmailDispatchOutbox> EmailDispatchOutbox { get; set; }
    public DbSet<EmailDispatchAttempt> EmailDispatchAttempts { get; set; }
    public DbSet<EmailDispatchReceipt> EmailDispatchReceipts { get; set; }
    public DbSet<EmailDispatchTenantControl> EmailDispatchTenantControls { get; set; }
    public DbSet<EmailDispatchProcessorState> EmailDispatchProcessorStates { get; set; }

    // ===== Native Integration Sync Outbox =====
    public DbSet<IntegrationSyncOutbox> IntegrationSyncOutbox { get; set; }

    // ===== Event Series =====
    public DbSet<EventSeries> EventSeries { get; set; }

    // ===== Contact Share Consents =====
    public DbSet<EventContactShareConsent> EventContactShareConsents { get; set; }
    public DbSet<EventContactShareConsentHistory> EventContactShareConsentHistory { get; set; }
    public DbSet<EventContactShareExport> EventContactShareExports { get; set; }
    public DbSet<EventContactShareExportItem> EventContactShareExportItems { get; set; }

    // ===== Instance Authentication Transients =====
    public DbSet<AtprotoTransientRecord> AtprotoTransientRecords { get; set; }
    public DbSet<AtprotoTransientAssertionReplay> AtprotoTransientAssertionReplays { get; set; }

    // ===== Idempotency =====
    public DbSet<IdempotencyRecord> IdempotencyRecords { get; set; }

    // ===== AI Assistant =====
    public DbSet<AiConversation> AiConversations { get; set; }
    public DbSet<AiConversationStatusLookup> AiConversationStatuses { get; set; }
    public DbSet<AiMessage> AiMessages { get; set; }
    public DbSet<AiMessageRoleLookup> AiMessageRoles { get; set; }
    public DbSet<AiRun> AiRuns { get; set; }
    public DbSet<AiRunStatusLookup> AiRunStatuses { get; set; }
    public DbSet<AiConversationReference> AiConversationReferences { get; set; }
    public DbSet<AiReferenceKindLookup> AiReferenceKinds { get; set; }
    public DbSet<AiProposedAction> AiProposedActions { get; set; }
    public DbSet<AiProposedActionKindLookup> AiProposedActionKinds { get; set; }
    public DbSet<AiProposedActionStatusLookup> AiProposedActionStatuses { get; set; }
    public DbSet<AiProviderKindLookup> AiProviderKinds { get; set; }
    public DbSet<AiToolExecution> AiToolExecutions { get; set; }
    public DbSet<AiConsentGrant> AiConsentGrants { get; set; }

    // ===== Webhook Delivery =====
    public DbSet<WebhookConsumer> WebhookConsumers { get; set; }
    public DbSet<WebhookConsumerKindLookup> WebhookConsumerKinds { get; set; }
    public DbSet<WebhookConsumerStatusLookup> WebhookConsumerStatuses { get; set; }
    public DbSet<WebhookProviderModeLookup> WebhookProviderModes { get; set; }
    public DbSet<WebhookProviderKindLookup> WebhookProviderKinds { get; set; }
    public DbSet<WebhookProviderCapabilityLookup> WebhookProviderCapabilities { get; set; }
    public DbSet<WebhookConsumerProviderBinding> WebhookConsumerProviderBindings { get; set; }
    public DbSet<WebhookProviderBindingVerificationStateLookup> WebhookProviderBindingVerificationStates { get; set; }
    public DbSet<WebhookEndpointStatusLookup> WebhookEndpointStatuses { get; set; }
    public DbSet<WebhookLocalDeliveryStatusLookup> WebhookLocalDeliveryStatuses { get; set; }
    public DbSet<WebhookBulkReplayStatusLookup> WebhookBulkReplayStatuses { get; set; }
    public DbSet<WebhookBulkReplayOperation> WebhookBulkReplayOperations { get; set; }
    public DbSet<WebhookPendingWorkDecisionLookup> WebhookPendingWorkDecisions { get; set; }
    public DbSet<WebhookRetentionSubjectKindLookup> WebhookRetentionSubjectKinds { get; set; }
    public DbSet<WebhookRetentionHold> WebhookRetentionHolds { get; set; }
    public DbSet<WebhookAuditActionLookup> WebhookAuditActions { get; set; }
    public DbSet<WebhookAuditOutcomeLookup> WebhookAuditOutcomes { get; set; }
    public DbSet<WebhookAuditPrincipalKindLookup> WebhookAuditPrincipalKinds { get; set; }
    public DbSet<WebhookAuditScopeKindLookup> WebhookAuditScopeKinds { get; set; }
    public DbSet<WebhookAuditTargetKindLookup> WebhookAuditTargetKinds { get; set; }
    public DbSet<WebhookAuditEvent> WebhookAuditEvents { get; set; }
    public DbSet<WebhookDeliveryAttemptOutcomeLookup> WebhookDeliveryAttemptOutcomes { get; set; }
    public DbSet<IncomingWebhookMessageStatusLookup> IncomingWebhookMessageStatuses { get; set; }
    public DbSet<IncomingWebhookProcessingAttemptOutcomeLookup> IncomingWebhookProcessingAttemptOutcomes { get; set; }
    public DbSet<IncomingWebhookSettlementSourceLookup> IncomingWebhookSettlementSources { get; set; }
    public DbSet<IncomingWebhookRedriveResultLookup> IncomingWebhookRedriveResults { get; set; }
    public DbSet<WebhookProviderPublicationStatusLookup> WebhookProviderPublicationStatuses { get; set; }
    public DbSet<WebhookProviderPublicationAttemptOutcomeLookup> WebhookProviderPublicationAttemptOutcomes { get; set; }
    public DbSet<WebhookPayloadProvenanceLookup> WebhookPayloadProvenances { get; set; }
    public DbSet<WebhookEventType> WebhookEventTypes { get; set; }
    public DbSet<WebhookEndpoint> WebhookEndpoints { get; set; }
    public DbSet<WebhookEndpointSubscription> WebhookEndpointSubscriptions { get; set; }
    public DbSet<WebhookMessage> WebhookMessages { get; set; }
    public DbSet<WebhookDeliveryPlanSnapshot> WebhookDeliveryPlanSnapshots { get; set; }
    public DbSet<WebhookLocalTargetSnapshot> WebhookLocalTargetSnapshots { get; set; }
    public DbSet<WebhookDeliveryAttempt> WebhookDeliveryAttempts { get; set; }
    public DbSet<WebhookProviderPublication> WebhookProviderPublications { get; set; }
    public DbSet<WebhookProviderPublicationAttempt> WebhookProviderPublicationAttempts { get; set; }
    public DbSet<IncomingWebhookMessage> IncomingWebhookMessages { get; set; }
    public DbSet<IncomingWebhookEffectOutbox> IncomingWebhookEffectOutboxes { get; set; }
    public DbSet<IncomingWebhookEffectReceipt> IncomingWebhookEffectReceipts { get; set; }
    public DbSet<IncomingWebhookProcessingAttempt> IncomingWebhookProcessingAttempts { get; set; }
    public DbSet<IncomingWebhookRedriveRecord> IncomingWebhookRedriveRecords { get; set; }
}
