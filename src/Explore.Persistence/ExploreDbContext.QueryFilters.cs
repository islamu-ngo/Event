using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Federation;
using Explore.Domain.Modules;
using Explore.Domain.Settings.Documents;
using Explore.Domain.SetupLive;
using Explore.Domain.Views;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using StorageObject = Explore.Domain.StorageObject;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SetupTargetEnrollment>()
            .HasQueryFilter(QueryFilterNames.Tenant, value =>
                IsTenantFilterBypassed || value.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<SetupEnrollmentIssuanceClaim>()
            .HasQueryFilter(QueryFilterNames.Tenant, value =>
                IsTenantFilterBypassed || value.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<SetupSecretBindingOperation>()
            .HasQueryFilter(QueryFilterNames.Tenant, value =>
                IsTenantFilterBypassed || value.TenantId == TenantFilterTenantId);

        // ===== Event Entities =====
        // Entities with both Tenant and Soft Delete filters (separate named filters for selective disabling)
        modelBuilder.Entity<Event>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventParticipationConfiguration>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<ParticipationRequirementAttachment>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventTicketCatalogVersion>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventTicketType>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventAddOnCatalogVersion>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventAddOnCatalogItem>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventCapacityPool>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<PaidEventPolicyVersion>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => e.TenantId == null || IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaidEventPolicyAllowedOrganizerKind>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => e.TenantId == null || IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaidEventPolicyAllowedCurrency>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => e.TenantId == null || IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaidEventPolicyRefundProtection>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => e.TenantId == null || IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaidEventPolicyCurrencyRiskLimitRow>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => e.TenantId == null || IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<OrganizerPaymentProviderAccountOperation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<OrganizerPaymentProviderConnection>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<OrganizerPaymentProviderConnectionSupportedCurrency>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.Connection != null && !e.Connection.IsDeleted);

        modelBuilder.Entity<PaidCheckoutSaleControl>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaidCheckoutSaleControlAudit>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaidCheckoutReviewApproval>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaidOrderAcceptanceSnapshot>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaidOrderAcceptanceLine>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaymentAttempt>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RefundAttempt>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RefundLineAllocation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RefundCampaign>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationMaterialChangeChoice>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaymentDispute>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<CheckoutDispatchEffect>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaymentReconciliationEffect>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PaymentSucceededObservation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationOrder>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<TicketPurchasePolicyVersion>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<TicketPurchaseAuthorityUsage>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<TicketPurchaseOperation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationOrderLine>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationOrderAddOnLine>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventAddOnInventoryAllocation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventAddOnFulfillment>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventAddOnRefundAllocation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<TicketingRecoveryCheckpoint>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<TicketingRecoveryReissueIntent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationOrderPii>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationOrderPlatformContribution>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PromotionDefinition>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PromotionCode>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PromotionReservation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationInventoryHold>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationParticipant>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationParticipantPii>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationTicketAssignment>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !EF.Property<bool>(e, "IsDeleted"));

        modelBuilder.Entity<AdmissionTicket>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<TicketTransferPolicy>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionTicketTransfer>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionTransferDeliveryIntent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<FairReturnSupplyPolicy>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<FairReturnSupplyUnit>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<EventWaitlistEntry>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<EventWaitlistOffer>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<FairReturnSourceBinding>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<WaitlistProviderObservation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<WaitlistPaymentIntent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<WaitlistRefundIntent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<FairReturnOrchestrationEffect>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionTicketCredential>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionTarget>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionCheckInPolicy>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionCheckInEvent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionCheckInState>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionScannerCapability>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionRecoveryCapability>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionRecoveryRequestIntent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<AdmissionRecoveryDeliveryIntent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<Explore.Application.Contracts.Admissions.AdmissionDeliveryIntent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationAmendment>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationWorkflow>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationRequirement>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationChannel>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationProviderConnection>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationProviderApprovedOrigin>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted && e.Connection != null && !e.Connection.IsDeleted);

        modelBuilder.Entity<RegistrationProviderBinding>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationProviderSubscriptionState>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted && e.Binding != null && !e.Binding.IsDeleted);

        modelBuilder.Entity<RegistrationProviderCapability>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted && e.Binding != null && !e.Binding.IsDeleted);

        modelBuilder.Entity<RegistrationProviderFieldMapping>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted && e.Binding != null && !e.Binding.IsDeleted);

        modelBuilder.Entity<RegistrationProviderOptionMapping>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted && e.Binding != null && !e.Binding.IsDeleted);

        modelBuilder.Entity<RegistrationProviderSchemaRevision>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted && e.Connection != null && !e.Connection.IsDeleted);

        modelBuilder.Entity<RegistrationForm>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationFormVersion>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationFormSection>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationFormField>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationFormFieldOption>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationFormRule>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationAttempt>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationSubmission>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationSubmissionRevision>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationAnswer>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationConsentRecord>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationAnswerFile>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<RegistrationAnswerFileRelease>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationSensitiveAnswerValue>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventContactShareConsentHistory>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationSubmissionIssue>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationRequirementFulfillment>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<ParticipantAdmissionEligibility>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationFinalizationEffect>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationProviderSubmissionWriteEffect>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationAmendment>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventPublicAction>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventOrganizerClaim>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSession>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSessionGroup>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSeries>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        // Event-related entities (tenant only - no soft delete)
        modelBuilder.Entity<EventRegistration>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);
        modelBuilder.Entity<EventCategories>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<EventTags>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<EventSessionLanguage>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<EventSessionSpeaker>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<TicketTypeEntitlement>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<EventSessionAgendaItem>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventSessionGroupSession>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventRoleAssignment>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventModerationRecord>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventReport>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventReportTarget>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.Report != null && !e.Report.IsDeleted);

        modelBuilder.Entity<EventReportEvidence>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.Report != null && !e.Report.IsDeleted);

        modelBuilder.Entity<EventReportCase>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.Report != null && !e.Report.IsDeleted);

        modelBuilder.Entity<EventReportSignal>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.ReportId == null || (e.Report != null && !e.Report.IsDeleted));

        modelBuilder.Entity<EventReportDecision>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.Report != null && !e.Report.IsDeleted);

        modelBuilder.Entity<EventReportDecisionExecution>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.Report != null && !e.Report.IsDeleted);

        modelBuilder.Entity<EventReportExternalLink>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.Report != null && !e.Report.IsDeleted);

        // ===== Event Scheduling Refactor - Phase 1 additive entities =====
        modelBuilder.Entity<EventDay>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventAgendaItem>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<LocationRoom>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSessionCategory>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventSessionTag>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventContactShareConsent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventContactShareExport>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EmailDispatchOutbox>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<IntegrationSyncOutbox>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<AtprotoRecordTenantPresentation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<AtprotoOutboundRecordOwnership>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<PdsSyncOutbox>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EmailDispatchAttempt>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EmailDispatchReceipt>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EmailDispatchTenantControl>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<WebhookConsumer>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => IsTenantFilterBypassed || (e.TenantId != null && e.TenantId == TenantFilterTenantId));

        modelBuilder.Entity<WebhookConsumerProviderBinding>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => IsTenantFilterBypassed || (e.TenantId != null && e.TenantId == TenantFilterTenantId));

        modelBuilder.Entity<WebhookEndpoint>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => IsTenantFilterBypassed || (e.TenantId != null && e.TenantId == TenantFilterTenantId));

        modelBuilder.Entity<WebhookEndpointSubscription>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => IsTenantFilterBypassed || (e.TenantId != null && e.TenantId == TenantFilterTenantId));

        modelBuilder.Entity<WebhookRetentionHold>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<WebhookMessage>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<WebhookDeliveryPlanSnapshot>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<WebhookLocalTargetSnapshot>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<WebhookBulkReplayOperation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<WebhookDeliveryAttempt>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<WebhookProviderPublication>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<WebhookProviderPublicationAttempt>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<IncomingWebhookMessage>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<IncomingWebhookEffectOutbox>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<IncomingWebhookEffectReceipt>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<IncomingWebhookProcessingAttempt>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<IncomingWebhookRedriveRecord>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventSessionIslamicAspect>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => IsTenantFilterBypassed
                    || (e.EventSession != null && e.EventSession.TenantId == TenantFilterTenantId));

        // Lookup extension: global event types (TenantId = null) + tenant-specific custom event types
        modelBuilder.Entity<EventType>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => e.TenantId == null
                    || IsTenantFilterBypassed
                    || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<RegistrationFormTemplate>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => e.TenantId == null || IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        // ===== Organization Entities =====
        // Entities with both Tenant and Soft Delete filters
        modelBuilder.Entity<Organization>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<OrganizationTenant>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<OrganizationTenantEvidence>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<OrganizationMember>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        // Organization review (tenant + soft delete)
        modelBuilder.Entity<OrganizationReview>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<OrganizationSetting>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        // ===== Group Entities =====
        modelBuilder.Entity<Group>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<GroupTenant>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<GroupMember>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<GroupSetting>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<NotificationChannelPreference>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<NotificationPreferenceProfile>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<WebPushSubscription>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<WebPushDispatchOutbox>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        // ===== Custom Properties (EAV) =====
        modelBuilder.Entity<CustomPropertyDefinition>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<CustomPropertyOption>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<CustomPropertyValue>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventTemplate>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventTemplateCustomPropertyDefinition>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventTemplateCustomPropertyOption>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventCustomPropertyDefinition>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventCustomPropertyOption>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventCustomPropertyValue>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventCustomPropertyProjection>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventWithSessionsView>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<CustomPropertyProjectionStatus>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<CustomPropertyProjectionDirtyScope>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        // ===== Event Session EAV Entities =====
        modelBuilder.Entity<EventSessionTemplate>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSessionTemplateCustomPropertyDefinition>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSessionTemplateCustomPropertyOption>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSessionCustomPropertyDefinition>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSessionCustomPropertyOption>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSessionCustomPropertyValue>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventSessionCustomPropertyProjection>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        // ===== Actor Entities =====
        // Entities with both Tenant and Soft Delete filters
        modelBuilder.Entity<Actor>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<ActorPii>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.Actor != null && !e.Actor.IsDeleted);

        modelBuilder.Entity<AtprotoIdentity>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<ExternalActorSubject>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<ServicePrincipal>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        // Actor-related (tenant only - no soft delete)
        modelBuilder.Entity<ActorKeyStore>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<ActorSubscription>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        // ===== User Entity =====
        // Soft Delete only (not tenant-scoped - global entity)
        modelBuilder.Entity<User>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, u => !u.IsDeleted);

        modelBuilder.Entity<UserPii>()
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => e.User != null && !e.User.IsDeleted);

        // ===== Location Entity =====
        modelBuilder.Entity<Location>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<LocationPii>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => IsTenantFilterBypassed
                    || (e.Location != null && e.Location.TenantId == TenantFilterTenantId));

        modelBuilder.Entity<EventLocation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<EventLocationDisclosureAudit>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<EventLocationExactReadAudit>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        // ===== Storage Entity =====
        modelBuilder.Entity<StorageObject>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<StorageUploadSession>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<StorageUsageCounter>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        // ===== Category and Tag Entities =====
        modelBuilder.Entity<Category>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<Tag>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<TagTypeTags>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<CategoryTypeCategories>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        // ===== User-Related Tenant Entities =====
        modelBuilder.Entity<ExternalApiKey>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || (e.TenantId != null && e.TenantId == TenantFilterTenantId));
        modelBuilder.Entity<ExternalApiKeyQuota>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => IsTenantFilterBypassed
                    || (e.ExternalApiKey != null
                        && e.ExternalApiKey.TenantId != null
                        && e.ExternalApiKey.TenantId == TenantFilterTenantId));
        modelBuilder.Entity<UserAuthenticationToken>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<UserPreference>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<UserNotificationPreference>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        // ===== Tenant Entities =====
        modelBuilder.Entity<TenantSetting>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<TenantSettingsDocument>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<ConfigurationManifestTenantResult>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<TenantUser>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);
        modelBuilder.Entity<TenantUserProfile>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<TenantUserRoleGrant>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<TenantOnboardingState>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<TenantNavigationLink>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        // Footer link groups: instance-default groups (TenantId = null) are always visible;
        // tenant-owned groups respect the tenant filter (same pattern as EventType).
        modelBuilder.Entity<TenantFooterLinkGroup>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => e.TenantId == null
                    || IsTenantFilterBypassed
                    || e.TenantId == TenantFilterTenantId);

        // Footer links have no TenantId — isolation flows through the parent group query filter.
        // No additional filter needed; EF will respect the parent filter via navigation includes.

        modelBuilder.Entity<TenantInvitation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        // ===== Module Governance Entities =====
        modelBuilder.Entity<TenantCapability>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        // ===== Audit & Notifications =====
        modelBuilder.Entity<AuditLog>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<WebhookAuditEvent>()
            .HasQueryFilter(QueryFilterNames.Tenant,
                e => IsTenantFilterBypassed || (e.TenantId != null && e.TenantId == TenantFilterTenantId));
        modelBuilder.Entity<Notification>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);
        modelBuilder.Entity<NotificationFanoutRun>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);
        modelBuilder.Entity<NotificationFanoutOccurrence>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<NotificationIntent>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<NotificationDelivery>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<NotificationExternalDelegation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        // ===== AI Assistant =====
        modelBuilder.Entity<AiConversation>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);

        modelBuilder.Entity<AiMessage>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<AiRun>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<AiConversationReference>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<AiProposedAction>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<AiToolExecution>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId);

        modelBuilder.Entity<AiConsentGrant>()
            .HasQueryFilter(QueryFilterNames.Tenant, e => IsTenantFilterBypassed || e.TenantId == TenantFilterTenantId)
            .HasQueryFilter(QueryFilterNames.SoftDelete, e => !e.IsDeleted);
    }
}
