// ABOUTME: Architecture guardrails for mutating MediatR requests and anonymous mutation surfaces.
// ABOUTME: Enforces the authorization surface directly from compiled reflection discovery without session artifacts.

namespace Event.Architecture.Tests;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

public sealed class AuthorizationSurfaceGuardrailTests
{
    private static readonly Assembly ApplicationAssembly = typeof(AuthorizeResourceAttribute).Assembly;
    private static readonly Assembly ApiAssembly = typeof(EndpointClassificationAttribute).Assembly;

    private static readonly InventoryEntry[] NamedMediatRExceptions =
    [
        new(
            "Explore.Application.Features.ConfigurationManifest.Requests.Commands.ApplyConfigurationManifestCommand",
            "host-local-bootstrap",
            "Dispatched only by ConfigurationManifestStartupRunner through IConfigurationManifestApplier inside the trusted deployment boundary; no controller or route sends it, no user principal exists at startup, and its authority is the operator-owned manifest file mounted read-only into the bootstrap host."),
        new(
            "Explore.Application.Features.Integrations.Listmonk.Requests.Commands.ResolveIntegrationSyncAmbiguityCommand",
            "handler-contained-admin",
            "Tenant administrator authorization is enforced inside ResolveIntegrationSyncAmbiguityCommandHandler before repository mutation."),
        new(
            "Explore.Application.Features.Authentication.Local.Requests.Commands.ChangeLocalPasswordCommand",
            "handler-current-local-session",
            "Fresh ordinary Local session and exact Ready binding are checked before native current-password/stamp Serializable CAS. Evidence: LocalIdentityLifecycleHttpTests.OrdinaryPasswordChangeRemainsProtectedAndWorksWithoutSmtp; LocalCredentialFirstUseTests."),
        new(
            "Explore.Application.Features.Authentication.Local.Requests.Commands.CompleteLocalPasswordRecoveryCommand",
            "native-recovery-purpose-token",
            "Recovery-purpose validation and exact native token/receipt CAS authorize one password mutation; mirror retry retains the original token, never login authority. Evidence: LocalIdentityLifecycleHttpTests.RecoveryDeduplicatesDeliveryAndReplayCannotReplacePasswordAgain and ExternalIdentityMirrorFailureRetriesOnlySynchronizationWithOriginalToken."),
        new(
            "Explore.Application.Features.Authentication.Local.Requests.Commands.ConfirmLocalEmailCommand",
            "native-email-purpose-token",
            "Native token binds operation, subject, actor, external link, email purpose and generation; current stamp/receipt CAS precedes serialized SyncUser mirror repair. Evidence: LocalIdentityLifecycleHttpTests.VerificationChangesOnlyExactNativeAndDomainBindingWithoutSigningIn and PublicInputsRejectExtraAuthorityOversizedValuesAndWrongPurpose."),
        new(
            "Explore.Application.Features.Authentication.Local.Handlers.Commands.ReconcileLocalIdentityLifecycleMirrorCommand",
            "trusted-worker-current-consumed-receipt",
            "Only LocalIdentityLifecycleDeliveryWorker dispatches this pointer-only production command, never HTTP. Native synchronization checks the current consumed receipt, result stamp and exact binding independently of public token expiry. Evidence: LocalIdentityLifecycleDeliveryWorkerTests; LocalIdentityLifecycleHttpTests.TrustedRepairAfterExpirySynchronizesMirrorAndEvictsCachedProfileWithoutPublicPointerAuthority."),
        new(
            "Explore.Application.Features.Authentication.Local.Requests.Commands.RequestLocalEmailVerificationCommand",
            "handler-public-admission-or-current-local-session",
            "Bounded anonymous intake requests only current-address verification via native lookup; proposed email requires fresh ordinary Local authority and rejects alternate account selection. Evidence: LocalIdentityLifecycleHttpTests.ProposedAddressRequiresCurrentLocalSessionAndConsumesItsExactPendingAddress; LocalIdentityLifecycleEmailServiceTests."),
        new(
            "Explore.Application.Features.Authentication.Local.Requests.Commands.RequestLocalPasswordRecoveryCommand",
            "handler-non-enumerating-local-admission",
            "Bounded native lookup restricts recovery to verified email and Ready linked Local credentials; uniform acceptance hides eligibility/delivery outcomes without creating accounts or login authority. Evidence: LocalIdentityLifecycleHttpTests.MissingRecoveryTargetIsAcceptedWithoutSessionOrAccountCreation and IneligibleRecoveryNeverUsesMatchingDomainEmailAsLocalAuthority; LocalIdentityLifecycleEmailServiceTests."),
        new(
            "Explore.Application.Features.Authentication.Local.Requests.Commands.CompleteLocalCredentialReplacementCommand",
            "native-restricted-replacement-proof",
            "Dedicated replacement authority is not an ordinary Local session; selected-store ReplaceAsync validates the exact current first-use operation. Evidence: LocalCredentialReplacementHttpTests; LocalCredentialFirstUseTests; LocalBffCredentialReplacementTests."),
        new(
            "Explore.Application.Features.Authentication.Local.Requests.Commands.CreateLocalIdentityCommand",
            "handler-current-instance-administrator",
            "Canonical user and fresh persisted platform-admin membership are rechecked before mutation and one-time disclosure; creation/reconciliation cannot replay plaintext. Evidence: LocalCredentialAdministrationHttpTests.EveryRouteRequiresCurrentPersistedInstanceAuthority and PlaintextIsOneTimeAndNeverStoredByGenericIdempotency."),
        new(
            "Explore.Application.Features.Authentication.Local.Requests.Commands.ReconcileLocalCredentialOperationCommand",
            "handler-current-instance-administrator",
            "Fresh instance-admin authority and exact provisioning receipt graph gate Serializable binding and separate Identity activation; conflicting partial binding is rejected. Unlike worker repair this requires administrator authority. Evidence: LocalCredentialAdministrationHttpTests.ReconcileCannotAdoptConflictingPartialBinding and ReconcileIdempotencyReplayCannotRestoreRevokedAdministratorAuthority."),
        new(
            "Explore.Application.Features.Authentication.Local.Requests.Commands.ResetLocalCredentialCommand",
            "handler-current-instance-administrator",
            "Fresh persisted platform-admin authority is rechecked before selected-store reset and disclosure; exact predecessor operation/stamp and current ChangeRequired receipt are required. Evidence: LocalCredentialAdministrationHttpTests.NonLocalAccountCannotReceiveLocalReset and PlaintextIsOneTimeAndNeverStoredByGenericIdempotency."),
        new(
            "Explore.Application.Features.EmailDispatch.Requests.Commands.DisableEmailDeliveryCommand",
            "handler-current-scope-administrator",
            "Current platform admin or exact ambient tenant/admin grant is rechecked after the ordered SMTP lease inside Serializable execution; writer binds actor, target, revision, protected confirmation and acknowledgement. Evidence: EmailDeliveryDisableCommandHandlerTests.AuthorityRevokedWhileAcquiringLeaseIsRechecked and ConfirmedTokenCannotReplayImmediatelyOrAfterReenable; EmailDeliveryDisableHttpTests."),
        new(
            "Explore.Application.Features.EmailDispatch.Requests.Queries.PreviewEmailDeliveryDisableQuery",
            "handler-current-scope-administrator-proof-preview",
            "Command-response discovery deliberately includes this Query: fresh scoped admin is checked before and within the lease/Serializable snapshot; only actionable impact issues confirmation. Evidence: EmailDeliveryDisableCommandHandlerTests.UnauthorizedTargetsCannotPreviewOrDisable and LockedOrNoOpPreviewNeverIssuesConfirmation."),
        new(
            "Explore.Application.Features.InstanceOnboarding.Requests.Commands.CompleteLocalInstanceOnboardingCommand",
            "handler-active-setup-secret-bootstrap",
            "Authenticated SetupSecret principal, durable active setup mode, active Local primary, server deployment mode, manual validation and preflight precede native bootstrap convergence. Evidence: LocalInstanceOnboardingHttpTests; LocalBootstrapConvergenceTests; LocalBootstrapSecretContractTests."),
        new(
            "Explore.Application.Features.RegistrationOrders.Commands.CancelConfirmedGuestRegistrationCommand",
            "native-current-limited-guest-capability",
            "Exact tenant/event/order capability and live promise are freshly fenced before eligibility, aggregate transition, admission revocation and exact hold release in one Serializable transaction. Authorized duplicates revalidate capability/deadline without generic replay. Evidence: GuestRegistrationStatusHttpTests cancellation partials; AnonymousCancellationConcurrencyTests; AnonymousCancellationRulesTests."),
        new(
            "Explore.Application.Features.RegistrationOrders.Commands.ConsumeAnonymousRegistrationChallengeCommand",
            "trusted-adapter-bound-proof-consumption",
            "AnonymousRegistrationChallengeBoundary bounds/canonicalizes transport; native validation binds tenant/event/digest/key/envelope/nonce and snapshots intended typed selection before allocation or cached disclosure. Evidence: AnonymousRegistrationChallengeHttpTests.GuestStartWithoutProofFailsBeforeAllocationOrReplay and ChangedTenantAndCapabilityScopeCannotDiscloseCommittedReplay; AnonymousRegistrationChallengeServiceTests."),
        new(
            "Explore.Application.Features.RegistrationOrders.Commands.IssueAnonymousRegistrationChallengeCommand",
            "handler-bounded-public-proof-issuance",
            "Current tenant, publicly eligible published event, guest visitor/participation mode, finite live end promise, bounded difficulty and transactional tenant/event quotas gate issuance under the settings lease without allocation. Evidence: AnonymousRegistrationChallengeIssueTests; AnonymousRegistrationChallengeQuotaTests; AnonymousRegistrationChallengeHttpTests.EffectiveIpBudgetCoversIssuanceAndStartWithoutLoopbackBypass."),
    ];
    private static readonly string[] NamedMediatRViolations =
    [
        "Explore.Application.Features.Actors.Requests.Commands.CreateActorCommand",
        "Explore.Application.Features.Actors.Requests.Commands.DeleteActorCommand",
        "Explore.Application.Features.Admissions.Requests.Commands.AcceptTicketTransferCommand",
        "Explore.Application.Features.Admissions.Requests.Commands.CancelTicketTransferCommand",
        "Explore.Application.Features.Admissions.Requests.Commands.CompleteParticipantAdmissionCommand",
        "Explore.Application.Features.Admissions.Requests.Commands.CorrectTicketTransferCommand",
        "Explore.Application.Features.Admissions.Requests.Commands.OfferTicketTransferCommand",
        "Explore.Application.Features.Admissions.Requests.Commands.ReissueTransferredTicketCommand",
        "Explore.Application.Features.AdmissionTickets.Requests.Commands.RedeemAdmissionTicketRecoveryCommand",
        "Explore.Application.Features.AdmissionTickets.Requests.Commands.ReissueCurrentAdmissionTicketPrintCommand",
        "Explore.Application.Features.AdmissionTickets.Requests.Commands.ReissueCurrentAdmissionTicketQrCommand",
        "Explore.Application.Features.AdmissionTickets.Requests.Commands.RequestAdmissionTicketRecoveryCommand",
        "Explore.Application.Features.AiAssistant.Requests.Commands.GrantAiConsentCommand",
        "Explore.Application.Features.AiAssistant.Requests.Commands.RevokeAiConsentCommand",
        "Explore.Application.Features.AiAssistant.Requests.Commands.RunAiRetentionCleanupCommand",
        "Explore.Application.Features.Appearance.Requests.Commands.CreateUiThemeCommand",
        "Explore.Application.Features.Appearance.Requests.Commands.DeleteUiThemeCommand",
        "Explore.Application.Features.Appearance.Requests.Commands.UpdateCurrentUserAppearancePreferencesCommand",
        "Explore.Application.Features.Appearance.Requests.Commands.UpdateUiThemeCommand",
        "Explore.Application.Features.Authentication.Atproto.Requests.Commands.BootstrapAtprotoSessionCommand",
        "Explore.Application.Features.Authentication.Atproto.Requests.Commands.RefreshAtprotoSessionCommand",
        "Explore.Application.Features.Authentication.Atproto.Requests.Commands.RevokeAtprotoSessionCommand",
        "Explore.Application.Features.Authentication.Local.Requests.Commands.LocalLoginCommand",
        "Explore.Application.Features.CategoryTypeCategories.Requests.Commands.CreateCategoryTypeCategoriesCommand",
        "Explore.Application.Features.CategoryTypeCategories.Requests.Commands.DeleteCategoryTypeCategoriesCommand",
        "Explore.Application.Features.CategoryTypeCategories.Requests.Commands.UpdateCategoryTypeCategoriesCommand",
        "Explore.Application.Features.ContactShareConsents.Requests.Commands.WithdrawContactShareConsentCommand",
        "Explore.Application.Features.EmailUnsubscribe.Requests.Commands.UnsubscribeFromEmailCategoryCommand",
        "Explore.Application.Features.EventAddOns.Requests.Commands.AddEventAddOnCatalogItemCommand",
        "Explore.Application.Features.EventAddOns.Requests.Commands.CreateEventAddOnCatalogDraftCommand",
        "Explore.Application.Features.EventAddOns.Requests.Commands.FulfillRegistrationOrderAddOnCommand",
        "Explore.Application.Features.EventAddOns.Requests.Commands.PublishEventAddOnCatalogCommand",
        "Explore.Application.Features.EventAddOns.Requests.Commands.RefundRegistrationOrderAddOnCommand",
        "Explore.Application.Features.EventAddOns.Requests.Commands.ReserveRegistrationOrderAddOnsCommand",
        "Explore.Application.Features.EventAddOns.Requests.Commands.RetireEventAddOnCatalogCommand",
        "Explore.Application.Features.EventPublicActions.Requests.Commands.RecordEventPublicActionEngagementCommand",
        "Explore.Application.Features.EventReporting.Requests.Commands.ProcessCoopDecisionCallbackCommand",
        "Explore.Application.Features.EventReporting.Requests.Commands.RecordOspreySignalCallbackCommand",
        "Explore.Application.Features.EventReporting.Requests.Commands.SubmitEventReportCommand",
        "Explore.Application.Features.EventReporting.Requests.Commands.UpdateMyReportCommunicationConsentCommand",
        "Explore.Application.Features.EventRoleAssignments.Requests.Commands.AssignEventRoleByEmailCommand",
        "Explore.Application.Features.EventRoleAssignments.Requests.Commands.AssignEventRoleCommand",
        "Explore.Application.Features.EventRoleAssignments.Requests.Commands.RevokeEventRoleAssignmentCommand",
        "Explore.Application.Features.EventRoleAssignments.Requests.Commands.TransferEventOwnershipCommand",
        "Explore.Application.Features.EventRoleAssignments.Requests.Commands.UpdateEventRoleAssignmentWindowCommand",
        "Explore.Application.Features.EventSeries.Requests.Commands.CreateEventSeriesCommand",
        "Explore.Application.Features.EventSeries.Requests.Commands.DeleteEventSeriesCommand",
        "Explore.Application.Features.ExternalApiKeys.Requests.Commands.CreateExternalApiKeyCommand",
        "Explore.Application.Features.ExternalApiKeys.Requests.Commands.RevokeExternalApiKeyCommand",
        "Explore.Application.Features.ExternalApiKeys.Requests.Commands.UpdateExternalApiKeyPolicyCommand",
        "Explore.Application.Features.Federation.Atproto.Requests.Commands.ImportAtprotoFederatedEventCommand",
        "Explore.Application.Features.Federation.Atproto.Requests.Commands.ReconcileAtprotoPdsSnapshotsCommand",
        "Explore.Application.Features.Footer.Requests.Commands.UpdateFooterGovernanceSettingsCommand",
        "Explore.Application.Features.GroupMembers.Requests.Commands.AddGroupMemberCommand",
        "Explore.Application.Features.GroupMembers.Requests.Commands.DeleteGroupMemberCommand",
        "Explore.Application.Features.GroupMembers.Requests.Commands.UpdateGroupMemberRoleCommand",
        "Explore.Application.Features.Groups.Requests.Commands.CreateGroupCommand",
        "Explore.Application.Features.Groups.Requests.Commands.DeleteGroupCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.ApplyKeycloakRealmSyncCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.BootstrapKeycloakRealmCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.CompleteInstanceOnboardingCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.RecalculateInstanceStorageUsageCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.RotateKeycloakClientSecretCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.SaveInstanceOnboardingProfileCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.SyncAuthorizationPolicyPackageCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateAdminPortalSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateAiAssistantGovernanceSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateAnalyticsGovernanceSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateAuthProviderConfigurationCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateAuthProviderConfigurationDuringSetupCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateAuthorizationProviderConfigurationCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateAuthorizationProviderConfigurationDuringSetupCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateBrandingSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateDomainSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateEventPolicyCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateInstanceSmtpSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateInstanceStorageSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateMcpGovernanceSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateModuleSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateOrganizationPolicyCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateRenderPolicySettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateResolverConfigurationCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.UpdateTenantDelegationSettingsCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Commands.VerifyCerbosEndpointCommand",
        "Explore.Application.Features.InstanceOnboarding.Requests.Queries.GetActiveTenantCountQuery",
        "Explore.Application.Features.InstanceOnboarding.Requests.Queries.RunKeycloakRealmDoctorQuery",
        "Explore.Application.Features.Integrations.Listmonk.Requests.Commands.TestListmonkConnectionCommand",
        "Explore.Application.Features.Integrations.Listmonk.Requests.Commands.UpdateListmonkIntegrationSettingsCommand",
        "Explore.Application.Features.Localization.Requests.Commands.ExportFromTmsCommand",
        "Explore.Application.Features.Localization.Requests.Commands.ImportLocalizationBundleCommand",
        "Explore.Application.Features.Localization.Requests.Commands.TestTmsConnectionCommand",
        "Explore.Application.Features.Localization.Requests.Commands.UpdateLocalizationGovernanceCommand",
        "Explore.Application.Features.Localization.Requests.Queries.GetLocalizationTmsApiKeyConfiguredQuery",
        "Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands.EnsureManagedProviderClientProvisionedCommand",
        "Explore.Application.Features.Management.Requests.Commands.CancelManagedTenantProvisioningOperationCommand",
        "Explore.Application.Features.Management.Requests.Commands.ProcessManagedTenantProvisioningOperationCommand",
        "Explore.Application.Features.Management.Requests.Commands.ReconcileManagedTenantProvisioningDeadLetterCommand",
        "Explore.Application.Features.Management.Requests.Commands.RevokeManagedControlPlaneRegistrationCommand",
        "Explore.Application.Features.Management.Requests.Commands.RotateManagedControlPlaneCredentialCommand",
        "Explore.Application.Features.Management.Requests.Commands.ScheduleManagedTenantProvisioningCommand",
        "Explore.Application.Features.Management.Requests.Commands.TriggerManagedControlPlaneRegistrationCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.ArchiveNotificationCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.DeleteNotificationCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.MarkAllNotificationsAsReadCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.MarkNotificationAsReadCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.SetCurrentUserNotificationPreferenceMuteCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.SnoozeNotificationCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.SubscribeCurrentUserWebPushSubscriptionCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.UnsubscribeCurrentUserWebPushSubscriptionCommand",
        "Explore.Application.Features.Notifications.Requests.Commands.UpdateCurrentUserNotificationPreferenceMatrixCommand",
        "Explore.Application.Features.OrganizationMembers.Requests.Commands.AcceptInvitationCommand",
        "Explore.Application.Features.OrganizationMembers.Requests.Commands.DeclineInvitationCommand",
        "Explore.Application.Features.OrganizationReviews.Commands.CreateOrganizationReview.CreateOrganizationReviewCommand",
        "Explore.Application.Features.OrganizerPaymentConnections.Commands.CreateOrganizerPaymentOnboardingLinkCommand",
        "Explore.Application.Features.OrganizerPaymentConnections.Commands.DisableOrganizerPaymentConnectionCommand",
        "Explore.Application.Features.OrganizerPaymentConnections.Commands.RecordOrganizerPaymentConnectionCommand",
        "Explore.Application.Features.OrganizerPaymentConnections.Commands.ReplaceOrganizerPaymentConnectionCommand",
        "Explore.Application.Features.Promotions.Requests.Commands.ApplyAuthenticatedPromotionCodeToRegistrationOrderCommand",
        "Explore.Application.Features.Promotions.Requests.Commands.ApplyGuestPromotionCodeToRegistrationOrderCommand",
        "Explore.Application.Features.Promotions.Requests.Commands.ApplyPromotionCodeToRegistrationOrderCommand",
        "Explore.Application.Features.Promotions.Requests.Commands.RemoveAuthenticatedPromotionFromRegistrationOrderCommand",
        "Explore.Application.Features.Promotions.Requests.Commands.RemoveGuestPromotionFromRegistrationOrderCommand",
        "Explore.Application.Features.Promotions.Requests.Commands.RemovePromotionFromRegistrationOrderCommand",
        "Explore.Application.Features.PublicExperience.Requests.Commands.RelayAnalyticsEventCommand",
        "Explore.Application.Features.RegistrationAnswerFiles.Commands.ReleaseRegistrationAnswerFileCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.AddRegistrationParticipantCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.ApproveRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.AssignRegistrationTicketCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.BulkAssignRegistrationTicketsCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.BulkDeferRegistrationTicketsCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.CancelAuthenticatedRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.CancelGuestRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.CancelRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.ClaimGuestRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.ContinueAuthenticatedRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.ContinueGuestRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.CreateRegistrationOrderWithHoldCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.DeferRegistrationTicketCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.FinalizeAuthenticatedRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.FinalizeFreeRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.FinalizeGuestRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.LaunchAuthenticatedNativeRegistrationAttemptCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.LaunchAuthenticatedRegistrationProviderAttemptCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.LaunchGuestNativeRegistrationAttemptCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.LaunchGuestRegistrationProviderAttemptCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.MutateAuthenticatedRegistrationParticipantsCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.MutateGuestRegistrationParticipantsCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.ReadyRegistrationOrderForCheckoutCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.RejectRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.ReserveAuthenticatedTicketPurchaseCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.ReserveGuestTicketPurchaseCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.ReserveTicketPurchaseCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.RetryAuthenticatedRegistrationPaymentCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.RetryGuestRegistrationPaymentCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.SkipAuthenticatedNativeRegistrationRequirementCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.SkipGuestNativeRegistrationRequirementCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.StartAuthenticatedRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.StartAuthenticatedRegistrationPaymentCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.StartGuestRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.StartGuestRegistrationPaymentCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.SubmitAuthenticatedNativeRegistrationAttemptCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.SubmitGuestNativeRegistrationAttemptCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.SubmitRegistrationOrderCommand",
        "Explore.Application.Features.RegistrationOrders.Requests.Commands.UpdateRegistrationParticipantCommand",
        "Explore.Application.Features.RegistrationProviders.Commands.ImportRegistrationProviderSchemaRevisionCommand",
        "Explore.Application.Features.RegistrationProviders.Commands.PublishRegistrationProviderBindingCommand",
        "Explore.Application.Features.RegistrationProviders.Commands.ReplaceDraftRegistrationProviderMappingsCommand",
        "Explore.Application.Features.RegistrationSubmissions.Commands.DrainRegistrationFinalizationEffectsCommand",
        "Explore.Application.Features.RegistrationSubmissions.Commands.GetNativeRegistrationRequirementProgressQuery",
        "Explore.Application.Features.RegistrationSubmissions.Commands.LaunchNativeRegistrationAttemptCommand",
        "Explore.Application.Features.RegistrationSubmissions.Commands.LaunchRegistrationProviderAttemptCommand",
        "Explore.Application.Features.RegistrationSubmissions.Commands.NormalizeRegistrationSubmissionCommand",
        "Explore.Application.Features.RegistrationSubmissions.Commands.ProcessProviderSubmissionEffectCommand",
        "Explore.Application.Features.RegistrationSubmissions.Commands.RecordRegistrationRequirementFulfillmentCommand",
        "Explore.Application.Features.RegistrationSubmissions.Commands.SkipNativeRegistrationRequirementCommand",
        "Explore.Application.Features.RegistrationSubmissions.Commands.SubmitNativeRegistrationAttemptCommand",
        "Explore.Application.Features.Roles.Requests.Commands.CreateCustomRoleCommand",
        "Explore.Application.Features.Roles.Requests.Commands.DeleteCustomRoleCommand",
        "Explore.Application.Features.Roles.Requests.Commands.UpdateRolePermissionsCommand",
        "Explore.Application.Features.Settings.Requests.Commands.LockSettingCommand",
        "Explore.Application.Features.Settings.Requests.Commands.ResetSettingCommand",
        "Explore.Application.Features.Settings.Requests.Commands.UnlockSettingCommand",
        "Explore.Application.Features.Settings.Requests.Commands.UpdateSettingBatchCommand",
        "Explore.Application.Features.Settings.Requests.Commands.UpdateSettingCommand",
        "Explore.Application.Features.Settings.Requests.Queries.ResolveSettingGroupQuery",
        "Explore.Application.Features.TagTypeTags.Requests.Commands.CreateTagTypeTagsCommand",
        "Explore.Application.Features.TagTypeTags.Requests.Commands.DeleteTagTypeTagsCommand",
        "Explore.Application.Features.TagTypeTags.Requests.Commands.UpdateTagTypeTagsCommand",
        "Explore.Application.Features.TenantOnboarding.Requests.Commands.CompleteTenantOnboardingCommand",
        "Explore.Application.Features.TenantOnboarding.Requests.Commands.SaveTenantOnboardingStepCommand",
        "Explore.Application.Features.TenantStorageSettings.Requests.Commands.PatchTenantStorageSettingsCommand",
        "Explore.Application.Features.Tenants.Requests.Commands.CreateTenantNavLink.CreateTenantNavLinkCommand",
        "Explore.Application.Features.Tenants.Requests.Commands.DeleteTenantNavLink.DeleteTenantNavLinkCommand",
        "Explore.Application.Features.Tenants.Requests.Commands.ReorderTenantNavLinks.ReorderTenantNavLinksCommand",
        "Explore.Application.Features.Users.Requests.Commands.SyncUserCommand",
        "Explore.Application.Features.Users.Requests.Commands.UpdateUserLastActiveTenantCommand",
        "Explore.Application.Features.Users.Requests.Queries.CheckUserExistsQuery",
        "Explore.Application.Features.Users.Requests.Queries.ResolveCurrentUserIdByIdentityRequest",
        "Explore.Application.Features.Users.Requests.Queries.ResolveUserTenantRedirectionRequest",
        "Explore.Application.Features.Waitlist.Requests.Commands.AcceptFairReturnOfferCommand",
        "Explore.Application.Features.Waitlist.Requests.Commands.JoinFairReturnWaitlistCommand",
        "Explore.Application.Features.Waitlist.Requests.Commands.LeaveFairReturnWaitlistCommand",
        "Explore.Application.Features.Waitlist.Requests.Commands.WithdrawFairReturnSupplyCommand",
        "Explore.Application.Services.Registration.Commands.DrainRegistrationProviderSubmissionWriteEffectsCommand",
    ];
    private static readonly InventoryEntry[] NamedAnonymousMutationExceptions =
    [
        new("AdmissionTicketRecoveryController.Consume", "PublicOrSignatureGated", "One-time admission recovery is gated by a tenant-bound keyed capability and dedicated bounded rate policy; idempotency replay is deliberately forbidden."),
        new("AnalyticsRelayController.Relay", "PublicOrSignatureGated", "Existing anonymous mutation surface explicitly preserved by Phase 0 inventory; Task 0.3 must verify public/signature/setup boundary or add authorization."),
        new("EmailUnsubscribeController.Post", "PublicOrSignatureGated", "Existing anonymous mutation surface explicitly preserved by Phase 0 inventory; Task 0.3 must verify public/signature/setup boundary or add authorization."),
        new("IncomingWebhooksController.RecordStripeConnectCallback", "PublicOrSignatureGated", "Existing anonymous mutation surface explicitly preserved by Phase 0 inventory; Task 0.3 must verify public/signature/setup boundary or add authorization."),
        new("IncomingWebhooksController.RecordSvixOperationalCallback", "PublicOrSignatureGated", "Existing anonymous mutation surface explicitly preserved by Phase 0 inventory; Task 0.3 must verify public/signature/setup boundary or add authorization."),
        new("InstanceOnboardingController.ValidateSecret", "PublicOrSignatureGated", "Existing anonymous mutation surface explicitly preserved by Phase 0 inventory; Task 0.3 must verify public/signature/setup boundary or add authorization."),
        new("LocalAuthController.Login", "PublicOrSignatureGated", "Anonymous credential verification is protected by the dedicated write rate policy, generic failures, runtime primary-provider gating, and lockout; idempotency replay would retain issued credentials."),
        new("LocalAuthController.Register", "PublicOrSignatureGated", "Anonymous account creation is protected by validation, normalized uniqueness, the dedicated write rate policy, runtime primary-provider gating, and rollback on token/sync failure."),
        new("RegistrationProviderCallbackController.RecordCallback", "PublicOrSignatureGated", "Existing anonymous mutation surface explicitly preserved by Phase 0 inventory; Task 0.3 must verify public/signature/setup boundary or add authorization."),
    ];
    private static readonly InventoryEntry[] NamedAnonymousMutationViolations = [];

    [Test]
    [Category("AuthorizationSurfaceGuardrail")]
    [DisplayName("Configured-administrator claims use purpose-bound bootstrap authority")]
    public async Task ConfiguredAdministratorClaim_UsesPurposeBoundBootstrapAuthority()
    {
        Type requestType = typeof(ClaimConfiguredInstanceAdministratorCommand);
        var inventory = AuthorizationSurfaceInventory.DiscoverMutatingRequests([requestType]);

        await Assert.That(
            requestType.GetCustomAttribute<AuthorizeConfiguredAdministratorClaimAttribute>())
            .IsNotNull();
        await Assert.That(requestType.GetCustomAttribute<AuthorizeResourceAttribute>())
            .IsNull()
            .Because("initial bootstrap authority cannot depend on an administrator already existing");
        await Assert.That(inventory.ProtectedMutatingRequests.Select(item => item.Id))
            .Contains(requestType.FullName!);
        await Assert.That(inventory.UnprotectedMutatingRequests).IsEmpty();
    }

    [Test]
    [Category("AuthorizationSurfaceGuardrail")]
    public async Task NamedHandlerAuthorities_MustResolveToCurrentDiscoveredRequests()
    {
        var requests = AuthorizationSurfaceInventory.DiscoverMutatingRequests(ApplicationAssembly.GetTypes())
            .UnprotectedMutatingRequests.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        await Assert.That(NamedMediatRExceptions.Where(entry => !requests.Contains(entry.Id))).IsEmpty();
        await Assert.That(NamedMediatRExceptions.Select(entry => entry.Id).Distinct().Count())
            .IsEqualTo(NamedMediatRExceptions.Length);
        await Assert.That(NamedMediatRExceptions.Where(entry => NamedMediatRViolations.Contains(entry.Id))).IsEmpty();
    }

    [Test]
    [Category("AuthorizationSurfaceGuardrail")]
    public async Task TrustedLifecycleMirrorRepair_MustNotBeAnApiDependency()
    {
        var result = NetArchTest.Rules.Types.InAssembly(ApiAssembly).ShouldNot().HaveDependencyOn(
            typeof(Explore.Application.Features.Authentication.Local.Handlers.Commands.ReconcileLocalIdentityLifecycleMirrorCommand).FullName!)
            .GetResult();
        await Assert.That(result.FailingTypeNames ?? []).IsEmpty()
            .Because("only the trusted delivery worker may dispatch pointer-only repair; HTTP retains native token authority");
    }

    [Test]
    [Category("AuthorizationSurfaceGuardrail")]
    [DisplayName("Mutating MediatR requests must be authorization-classified or named in the Phase 0 inventory")]
    public async Task MutatingMediatRRequests_MustBeAuthorizationClassifiedOrNamed()
    {
        var inventory = AuthorizationSurfaceInventory.Discover(ApplicationAssembly, ApiAssembly);
        var namedIds = NamedMediatRExceptions.Select(entry => entry.Id)
            .Concat(NamedMediatRViolations)
            .ToHashSet(StringComparer.Ordinal);

        var unclassified = inventory.UnprotectedMutatingRequests
            .Where(item => !namedIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToArray();

        await Assert.That(unclassified).IsEmpty()
            .Because("every mutating IRequest<T> must either carry [AuthorizeResource] or have an exact Phase 0 disposition with evidence");
    }

    [Test]
    [Category("AuthorizationSurfaceGuardrail")]
    [DisplayName("Anonymous mutation controller actions must be public/signature-gated or named in the Phase 0 inventory")]
    public async Task AnonymousMutationSurfaces_MustBePublicSignatureGatedOrNamed()
    {
        var inventory = AuthorizationSurfaceInventory.Discover(ApplicationAssembly, ApiAssembly);
        var namedIds = NamedAnonymousMutationExceptions.Concat(NamedAnonymousMutationViolations)
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);

        var unclassified = inventory.AnonymousMutationSurfaces
            .Where(item => !item.IsPublicTransactional && !item.IsSetupSecretGated && !item.IsReviewedLocalLifecycle)
            .Where(item => !namedIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToArray();

        await Assert.That(unclassified).IsEmpty()
            .Because("anonymous unsafe controller actions must be public-transactional, setup-secret gated, or exact named callback/public-ingestion inventory entries");
    }

    [Test]
    [Category("AuthorizationSurfaceGuardrail")]
    [DisplayName("Production authorization port must expose only typed request decisions")]
    public async Task ProductionAuthorizationPort_MustExposeOnlyTypedRequestDecisions()
    {
        var providerMethods = typeof(Explore.Application.Contracts.Infrastructure.IAuthorizationProvider)
            .GetMethods()
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(providerMethods).IsEquivalentTo(["AuthorizeAsync", "AuthorizeBatchAsync"]);
    }

    [Test]
    [Category("AuthorizationSurfaceGuardrail")]
    [DisplayName("Authorization surface inventory is derived from compiled discovery")]
    public async Task AuthorizationSurfaceInventory_ShouldBeDerivedFromCompiledDiscovery()
    {
        var inventory = AuthorizationSurfaceInventory.Discover(ApplicationAssembly, ApiAssembly);
        var report = new AuthorizationSurfaceReport(
            SchemaVersion: 1,
            GeneratedFrom: "compiled-reflection",
            ApplicationAssembly: ApplicationAssembly.GetName().Name ?? string.Empty,
            ApiAssembly: ApiAssembly.GetName().Name ?? string.Empty,
            ProtectedWrites: inventory.ProtectedMutatingRequests,
            NamedHandlerOwnedExceptions: NamedMediatRExceptions,
            MediatRDispositions: [],
            AnonymousReadOrPublicActions: inventory.AnonymousReadOrPublicActions,
            SignatureGatedActions: inventory.SignatureGatedActions,
            AnonymousMutationExceptions: [.. NamedAnonymousMutationExceptions, .. ReviewedAnonymousEndpointGovernance.LocalLifecycleExceptions],
            Violations: NamedAnonymousMutationViolations,
            UnclassifiedMutatingRequests: inventory.UnprotectedMutatingRequests
                .Where(item => !NamedMediatRExceptions.Any(entry => entry.Id == item.Id))
                .Where(item => !NamedMediatRViolations.Contains(item.Id, StringComparer.Ordinal))
                .ToArray(),
            UnclassifiedAnonymousMutationSurfaces: inventory.AnonymousMutationSurfaces
                .Where(item => !item.IsPublicTransactional && !item.IsSetupSecretGated && !item.IsReviewedLocalLifecycle)
                .Where(item => !NamedAnonymousMutationExceptions.Concat(NamedAnonymousMutationViolations).Any(entry => entry.Id == item.Id))
                .ToArray());

        await Assert.That(report.UnclassifiedMutatingRequests).IsEmpty();
        await Assert.That(report.UnclassifiedAnonymousMutationSurfaces).IsEmpty();
        await Assert.That(report.Violations).IsEmpty();
    }

    [Test]
    [Category("AuthorizationSurfaceGuardrail")]
    [DisplayName("Guardrail probes reject synthetic unclassified mutating request and anonymous mutation action")]
    public async Task GuardrailProbes_ShouldRejectSyntheticViolations()
    {
        var mutatingRequests = AuthorizationSurfaceInventory.DiscoverMutatingRequests(
            [typeof(SyntheticUnclassifiedCommand), typeof(SyntheticLocalLifecycleCommand), typeof(SyntheticProofPreviewQuery)]);
        var anonymousMutations = AuthorizationSurfaceInventory.DiscoverControllerActions(new[] { typeof(SyntheticAnonymousMutationController) })
            .AnonymousMutationSurfaces;

        await Assert.That(mutatingRequests.UnprotectedMutatingRequests.Select(item => item.Id).ToArray())
            .Contains("Event.Architecture.Tests.AuthorizationSurfaceGuardrailTests+SyntheticUnclassifiedCommand");
        await Assert.That(anonymousMutations.Select(item => item.Id).ToArray())
            .Contains("SyntheticAnonymousMutationController.Post");
        await Assert.That(mutatingRequests.UnprotectedMutatingRequests.Select(item => item.Id)).Contains(typeof(SyntheticLocalLifecycleCommand).FullName!);
        await Assert.That(mutatingRequests.UnprotectedMutatingRequests.Select(item => item.Id)).Contains(typeof(SyntheticProofPreviewQuery).FullName!);
        await Assert.That(mutatingRequests.UnprotectedMutatingRequests.Where(item =>
            NamedMediatRExceptions.Any(entry => entry.Id == item.Id) || NamedMediatRViolations.Contains(item.Id))).IsEmpty();
        await Assert.That(anonymousMutations.Any(item => item.IsReviewedLocalLifecycle)).IsFalse();
    }

    private sealed record SyntheticUnclassifiedCommand : IRequest<BaseCommandResponse<Guid>>;
    private sealed record SyntheticLocalLifecycleCommand : IRequest<BaseCommandResponse<Guid>>;
    private sealed record SyntheticProofPreviewQuery : IRequest<BaseCommandResponse<Guid>>;

    private sealed class SyntheticAnonymousMutationController : ControllerBase
    {
        [HttpPost]
        [AllowAnonymous]
        public OkResult Post() => Ok();
    }

}

internal static class AuthorizationSurfaceInventory
{
    private static readonly string[] MutatingNamePrefixes =
    [
        "Add", "Archive", "Cancel", "Clone", "Complete", "Create", "Delete", "Disable", "Drain", "Execute", "Finalize",
        "Import", "Lock", "Moderate", "Move", "Park", "Patch", "Process", "Publish", "Reconcile", "Record", "Remove",
        "Reorder", "Replace", "Replay", "Resolve", "Reset", "Revoke", "Rotate", "Run", "Set", "Skip", "Start", "Stop",
        "Submit", "Sync", "Transition", "Unarchive", "Unlock", "Unmoderate", "Update", "Withdraw"
    ];

    private static readonly HashSet<string> UnsafeHttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    public static AuthorizationSurfaceDiscovery Discover(Assembly applicationAssembly, Assembly apiAssembly)
    {
        var mutatingRequests = DiscoverMutatingRequests(applicationAssembly.GetTypes());
        var controllerActions = DiscoverControllerActions(apiAssembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract));

        return new AuthorizationSurfaceDiscovery(
            mutatingRequests.ProtectedMutatingRequests,
            mutatingRequests.UnprotectedMutatingRequests,
            controllerActions.AnonymousMutationSurfaces,
            controllerActions.AnonymousReadOrPublicActions,
            controllerActions.SignatureGatedActions);
    }

    public static MutatingRequestDiscovery DiscoverMutatingRequests(IEnumerable<Type> requestTypes)
    {
        var protectedRequests = new List<RequestInventoryItem>();
        var unprotectedRequests = new List<RequestInventoryItem>();

        foreach (var type in requestTypes.Where(IsConcreteMediatRRequest).Where(IsMutatingRequest).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var item = new RequestInventoryItem(
                Id: type.FullName ?? type.Name,
                Name: type.Name,
                ResponseType: GetResponseType(type)?.FullName ?? string.Empty,
                ClassificationReason: GetMutatingRequestReason(type));

            if (HasAuthorizationClassification(type))
            {
                protectedRequests.Add(item);
            }
            else
            {
                unprotectedRequests.Add(item);
            }
        }

        return new MutatingRequestDiscovery(protectedRequests.ToArray(), unprotectedRequests.ToArray());
    }

    private static bool HasAuthorizationClassification(Type type) =>
        type.GetCustomAttribute<AuthorizeResourceAttribute>(inherit: true) is not null
        || type.GetCustomAttribute<AuthorizeConfiguredAdministratorClaimAttribute>(
            inherit: true) is not null;

    public static ControllerActionDiscovery DiscoverControllerActions(IEnumerable<Type> controllerTypes)
    {
        var anonymousMutations = new List<ControllerActionInventoryItem>();
        var anonymousReadsOrPublic = new List<ControllerActionInventoryItem>();
        var signatureGated = new List<ControllerActionInventoryItem>();

        foreach (var controller in controllerTypes.OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(IsHttpAction))
            {
                var item = CreateControllerActionItem(controller, action);
                if (item.IsSetupSecretGated)
                {
                    signatureGated.Add(item);
                }

                if (!item.IsAnonymous)
                {
                    continue;
                }

                if (item.HttpMethods.Any(method => UnsafeHttpMethods.Contains(method)))
                {
                    anonymousMutations.Add(item);
                }
                else
                {
                    anonymousReadsOrPublic.Add(item);
                }
            }
        }

        return new ControllerActionDiscovery(
            anonymousMutations.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            anonymousReadsOrPublic.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            signatureGated.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray());
    }

    private static bool IsConcreteMediatRRequest(Type type) =>
        type is { IsAbstract: false, IsInterface: false }
        && GetResponseType(type) is not null;

    private static Type? GetResponseType(Type type) =>
        type.GetInterfaces()
            .Where(interfaceType => interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IRequest<>))
            .Select(interfaceType => interfaceType.GetGenericArguments()[0])
            .FirstOrDefault();

    private static bool IsMutatingRequest(Type type)
    {
        var responseType = GetResponseType(type);
        return type.Name.EndsWith("Command", StringComparison.Ordinal)
            || (type.Namespace?.Contains(".Commands", StringComparison.Ordinal) ?? false)
            || MutatingNamePrefixes.Any(prefix => type.Name.StartsWith(prefix, StringComparison.Ordinal))
            || IsCommandResponse(responseType)
            || responseType == typeof(bool)
            || responseType == typeof(int);
    }

    private static string GetMutatingRequestReason(Type type)
    {
        var reasons = new List<string>();
        var responseType = GetResponseType(type);

        if (type.Name.EndsWith("Command", StringComparison.Ordinal)) reasons.Add("command-name");
        if (type.Namespace?.Contains(".Commands", StringComparison.Ordinal) ?? false) reasons.Add("commands-namespace");
        if (MutatingNamePrefixes.Any(prefix => type.Name.StartsWith(prefix, StringComparison.Ordinal))) reasons.Add("mutating-name-prefix");
        if (IsCommandResponse(responseType)) reasons.Add("command-response");
        if (responseType == typeof(bool) || responseType == typeof(int)) reasons.Add("scalar-effect-response");

        return string.Join(",", reasons.Distinct(StringComparer.Ordinal));
    }

    private static bool IsCommandResponse(Type? type) =>
        type?.IsGenericType == true && type.GetGenericTypeDefinition() == typeof(BaseCommandResponse<>);

    private static bool IsHttpAction(MethodInfo method) =>
        !method.IsSpecialName
        && method.DeclaringType != typeof(object)
        && !HasAttribute<NonActionAttribute>(method)
        && GetHttpMethods(method).Length > 0;

    private static ControllerActionInventoryItem CreateControllerActionItem(Type controller, MethodInfo action)
    {
        var classification = ResolveAttribute<EndpointClassificationAttribute>(controller, action)?.Class;
        var methods = GetHttpMethods(action).OrderBy(method => method, StringComparer.OrdinalIgnoreCase).ToArray();
        return new ControllerActionInventoryItem(
            Id: $"{controller.Name}.{action.Name}",
            Controller: controller.Name,
            Action: action.Name,
            HttpMethods: methods,
            EndpointClass: classification?.ToString() ?? string.Empty,
            IsAnonymous: HasEffectiveAttribute<AllowAnonymousAttribute>(controller, action),
            IsAuthorized: HasEffectiveAttribute<AuthorizeAttribute>(controller, action),
            IsPublicTransactional: classification == EndpointClass.PublicTransactional,
            IsSetupSecretGated: HasEffectiveAttribute<SetupSecretRequiredAttribute>(controller, action),
            IsReviewedLocalLifecycle: ReviewedAnonymousEndpointGovernance.IsLocalLifecycle(controller, action));
    }

    private static string[] GetHttpMethods(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true)
            .OfType<IActionHttpMethodProvider>()
            .SelectMany(provider => provider.HttpMethods)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HasEffectiveAttribute<TAttribute>(Type controller, MethodInfo action)
        where TAttribute : Attribute =>
        HasAttribute<TAttribute>(controller) || HasAttribute<TAttribute>(action);

    private static bool HasAttribute<TAttribute>(MemberInfo member)
        where TAttribute : Attribute =>
        member.GetCustomAttributes<TAttribute>(inherit: true).Any();

    private static TAttribute? ResolveAttribute<TAttribute>(Type controller, MethodInfo action)
        where TAttribute : Attribute =>
        action.GetCustomAttributes<TAttribute>(inherit: true).FirstOrDefault()
        ?? controller.GetCustomAttributes<TAttribute>(inherit: true).FirstOrDefault();
}

internal sealed record InventoryEntry(string Id, string Classification, string Reason);

internal sealed record MediatRDispositionEntry(
    string RequestType,
    string Disposition,
    string Evidence,
    string Reason,
    string? Resource,
    string? Action,
    string Scenario);

internal sealed record Phase0MediatRDispositionArtifact(
    int SchemaVersion,
    string GeneratedFrom,
    string RawListSource,
    string[] ApprovedDispositions,
    int RowCount,
    Dictionary<string, int> CategoryCounts,
    int UnresolvedCount,
    MediatRDispositionEntry[] Dispositions);

internal sealed record RequestInventoryItem(string Id, string Name, string ResponseType, string ClassificationReason);

internal sealed record ControllerActionInventoryItem(
    string Id,
    string Controller,
    string Action,
    string[] HttpMethods,
    string EndpointClass,
    bool IsAnonymous,
    bool IsAuthorized,
    bool IsPublicTransactional,
    bool IsSetupSecretGated,
    bool IsReviewedLocalLifecycle);

internal sealed record MutatingRequestDiscovery(RequestInventoryItem[] ProtectedMutatingRequests, RequestInventoryItem[] UnprotectedMutatingRequests);

internal sealed record ControllerActionDiscovery(
    ControllerActionInventoryItem[] AnonymousMutationSurfaces,
    ControllerActionInventoryItem[] AnonymousReadOrPublicActions,
    ControllerActionInventoryItem[] SignatureGatedActions);

internal sealed record AuthorizationSurfaceDiscovery(
    RequestInventoryItem[] ProtectedMutatingRequests,
    RequestInventoryItem[] UnprotectedMutatingRequests,
    ControllerActionInventoryItem[] AnonymousMutationSurfaces,
    ControllerActionInventoryItem[] AnonymousReadOrPublicActions,
    ControllerActionInventoryItem[] SignatureGatedActions);

internal sealed record AuthorizationSurfaceReport(
    int SchemaVersion,
    string GeneratedFrom,
    string ApplicationAssembly,
    string ApiAssembly,
    RequestInventoryItem[] ProtectedWrites,
    InventoryEntry[] NamedHandlerOwnedExceptions,
    MediatRDispositionEntry[] MediatRDispositions,
    ControllerActionInventoryItem[] AnonymousReadOrPublicActions,
    ControllerActionInventoryItem[] SignatureGatedActions,
    InventoryEntry[] AnonymousMutationExceptions,
    InventoryEntry[] Violations,
    RequestInventoryItem[] UnclassifiedMutatingRequests,
    ControllerActionInventoryItem[] UnclassifiedAnonymousMutationSurfaces);

[JsonSerializable(typeof(AuthorizationSurfaceReport))]
[JsonSerializable(typeof(Phase0MediatRDispositionArtifact))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;
