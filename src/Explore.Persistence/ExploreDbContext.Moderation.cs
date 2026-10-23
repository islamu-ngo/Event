using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    internal Guid? ModerationTrustDomainId { get; set; }

    public DbSet<ModerationCase> ModerationCases => Set<ModerationCase>();
    public DbSet<ModerationFinding> ModerationFindings => Set<ModerationFinding>();
    public DbSet<ModerationReviewerAssignment> ModerationReviewerAssignments => Set<ModerationReviewerAssignment>();
    public DbSet<ModerationPolicy> ModerationPolicies => Set<ModerationPolicy>();
    public DbSet<ModerationPolicyRule> ModerationPolicyRules => Set<ModerationPolicyRule>();
    public DbSet<ModerationDecision> ModerationDecisions => Set<ModerationDecision>();
    public DbSet<ModerationSanction> ModerationSanctions => Set<ModerationSanction>();
    public DbSet<ModerationScheduleRiskReview> ModerationScheduleRiskReviews => Set<ModerationScheduleRiskReview>();
    public DbSet<ModerationProtectiveMeasure> ModerationProtectiveMeasures => Set<ModerationProtectiveMeasure>();
    public DbSet<ModerationOperationReceipt> ModerationOperationReceipts => Set<ModerationOperationReceipt>();
    public DbSet<ModerationEffectIntent> ModerationEffectIntents => Set<ModerationEffectIntent>();
    public DbSet<ModerationOriginalEffectCompletionReceipt> ModerationOriginalEffectCompletionReceipts =>
        Set<ModerationOriginalEffectCompletionReceipt>();
    public DbSet<ModerationHostBinding> ModerationHostBindings => Set<ModerationHostBinding>();
    public DbSet<ModerationMachineSubjectBinding> ModerationMachineSubjectBindings => Set<ModerationMachineSubjectBinding>();
    public DbSet<ModerationQualificationEpoch> ModerationQualificationEpochs => Set<ModerationQualificationEpoch>();
    public DbSet<ModerationQualificationEvidence> ModerationQualificationEvidence => Set<ModerationQualificationEvidence>();
    public DbSet<ModerationQualificationMutationReceipt> ModerationQualificationMutationReceipts => Set<ModerationQualificationMutationReceipt>();
    public DbSet<ModerationQualificationPolicySource> ModerationQualificationPolicySources => Set<ModerationQualificationPolicySource>();
    public DbSet<ModerationQualificationPolicyAdoption> ModerationQualificationPolicyAdoptions => Set<ModerationQualificationPolicyAdoption>();
    public DbSet<ModerationNoticeDisclosureApproval> ModerationNoticeDisclosureApprovals => Set<ModerationNoticeDisclosureApproval>();
    public DbSet<ModerationNoticeNotificationReceipt> ModerationNoticeNotificationReceipts => Set<ModerationNoticeNotificationReceipt>();
    public DbSet<ModerationContributionSelection> ModerationContributionSelections => Set<ModerationContributionSelection>();
    public DbSet<ModerationResourceGeneration> ModerationResourceGenerations => Set<ModerationResourceGeneration>();
    public DbSet<ModerationEventGeneration> ModerationEventGenerations => Set<ModerationEventGeneration>();
    public DbSet<ModerationEventTargetAdoption> ModerationEventTargetAdoptions => Set<ModerationEventTargetAdoption>();
    public DbSet<ModerationProfileAdoption> ModerationProfileAdoptions => Set<ModerationProfileAdoption>();
    public DbSet<ModerationConsumerInstallation> ModerationConsumerInstallations => Set<ModerationConsumerInstallation>();
    public DbSet<ModerationConsumerMeasurementChallenge> ModerationConsumerMeasurementChallenges => Set<ModerationConsumerMeasurementChallenge>();
    public DbSet<ModerationMeasurementIssuerEnrollment> ModerationMeasurementIssuerEnrollments => Set<ModerationMeasurementIssuerEnrollment>();
    public DbSet<ModerationConsumerMeasurementReceipt> ModerationConsumerMeasurementReceipts => Set<ModerationConsumerMeasurementReceipt>();
    public DbSet<ModerationResourceTargetAdoption> ModerationResourceTargetAdoptions => Set<ModerationResourceTargetAdoption>();
    public DbSet<ModerationCaseTargetReceipt> ModerationCaseTargetReceipts => Set<ModerationCaseTargetReceipt>();
    public DbSet<ModerationCaseIntakeAcknowledgement> ModerationCaseIntakeAcknowledgements => Set<ModerationCaseIntakeAcknowledgement>();
    public DbSet<ModerationCaseNarrativePolicySource> ModerationCaseNarrativePolicySources => Set<ModerationCaseNarrativePolicySource>();
    public DbSet<ModerationCaseNarrativeAdoption> ModerationCaseNarrativeAdoptions => Set<ModerationCaseNarrativeAdoption>();
    public DbSet<ModerationCaseNarrative> ModerationCaseNarratives => Set<ModerationCaseNarrative>();
    public DbSet<ModerationParticipantIncidentResponsibilitySource> ModerationParticipantIncidentResponsibilitySources =>
        Set<ModerationParticipantIncidentResponsibilitySource>();
    public DbSet<ModerationEventRightsSource> ModerationEventRightsSources => Set<ModerationEventRightsSource>();
    public DbSet<ModerationEventRightsAdoption> ModerationEventRightsAdoptions => Set<ModerationEventRightsAdoption>();

    private void ApplyModerationQueryFilters(ModelBuilder builder)
    {
        builder.Entity<ModerationOperationalBudgetSource>().HasQueryFilter("ModerationAuthority",
            entity => !IsTenantFilterBypassed && ModerationTrustDomainId != null && TenantFilterTenantId != null
                && entity.TrustDomainId == ModerationTrustDomainId && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationOperationalBudgetAdoption>().HasQueryFilter("ModerationAuthority",
            entity => !IsTenantFilterBypassed && ModerationTrustDomainId != null && TenantFilterTenantId != null
                && entity.TrustDomainId == ModerationTrustDomainId && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationClassificationControlSelection>().HasQueryFilter("ModerationAuthority",
            entity => !IsTenantFilterBypassed && ModerationTrustDomainId != null && TenantFilterTenantId != null
                && entity.TrustDomainId == ModerationTrustDomainId && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationClassificationControlBinding>().HasQueryFilter("ModerationAuthority",
            entity => Set<ModerationClassificationControlSelection>().Any(selection => selection.Id == entity.SelectionId));
        builder.Entity<ModerationClassificationEventTargetAdoption>().HasQueryFilter("ModerationAuthority",
            entity => !IsTenantFilterBypassed && ModerationTrustDomainId != null && TenantFilterTenantId != null
                && entity.TrustDomainId == ModerationTrustDomainId && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationProfileCollectionSubjectObservation>().HasQueryFilter("ModerationProfileSubjectObservation",
            entity => !IsTenantFilterBypassed && ModerationTrustDomainId != null && TenantFilterTenantId != null
                && entity.TrustDomainId == ModerationTrustDomainId && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationClassificationMaterialSource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationClassificationMaterialReview>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationClassificationMaterialEntry>().HasQueryFilter("ModerationAuthority",
            entity => Set<ModerationClassificationMaterialSource>().Any(source => source.Id == entity.SourceId));
        builder.Entity<ModerationClassificationMaterialComponent>().HasQueryFilter("ModerationAuthority",
            entity => Set<ModerationClassificationMaterialSource>().Any(source => source.Id == entity.SourceId));
        builder.Entity<ModerationClassificationMaterialCategory>().HasQueryFilter("ModerationAuthority",
            entity => Set<ModerationClassificationMaterialEntry>().Any(entry => entry.Id == entity.EntryId));
        builder.Entity<ModerationScheduleRiskReview>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.EventScopeTrustDomainId == ModerationTrustDomainId
                && entity.EventScopeTenantId == TenantFilterTenantId);
        builder.Entity<ModerationOriginalEffectCompletionReceipt>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseNarrativePolicySource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseNarrativeAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseNarrative>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationParticipantIncidentResponsibilitySource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationMeasuredOperationsSource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationMeasuredOperationsAcceptance>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationEventRightsSource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationEventRightsAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationClassificationTaxonomySource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationClassificationTaxonomyAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationClassificationTaxonomyCategory>().HasQueryFilter("ModerationAuthority",
            entity => Set<ModerationClassificationTaxonomySource>().Any(source => source.Id == entity.SourceId));
        builder.Entity<ModerationChildAgePolicySource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationChildAgePolicyAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationProfileSubjectConsent>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && TenantFilterTenantId != null
                && entity.Source.TrustDomainId == ModerationTrustDomainId
                && entity.Source.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationProfileCollectionAssessmentSource>().HasQueryFilter("ModerationProfileAssessment",
            entity => ModerationTrustDomainId != null && TenantFilterTenantId != null
                && entity.ProcedureSource.TrustDomainId == ModerationTrustDomainId
                && entity.ProcedureSource.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationProfileCollectionAssessmentAdoption>().HasQueryFilter("ModerationProfileAssessment",
            entity => ModerationTrustDomainId != null && TenantFilterTenantId != null
                && entity.Source.ProcedureSource.TrustDomainId == ModerationTrustDomainId
                && entity.Source.ProcedureSource.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationRecognitionPrivacySource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationRecognitionPrivacyAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationRecognitionPrivacyRetention>().HasQueryFilter("ModerationAuthority",
            entity => Set<ModerationRecognitionPrivacySource>().Any(source => source.Id == entity.SourceId));
        builder.Entity<ModerationConstitutionSource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationConstitutionAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationConstitutionAvailability>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationConstitutionReviewer>().HasQueryFilter("ModerationAuthority",
            entity => Set<ModerationConstitutionSource>().Any(source => source.Id == entity.SourceId));
        builder.Entity<ModerationDeploymentSource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationDeploymentAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationDeploymentConsumer>().HasQueryFilter("ModerationAuthority",
            entity => Set<ModerationDeploymentSource>().Any(source => source.Id == entity.SourceId));
        builder.Entity<ModerationConsumerMeasurementReceipt>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationMeasurementIssuerEnrollment>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationConsumerMeasurementChallenge>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationConsumerInstallation>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationProfileAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationEventTargetAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationEventGeneration>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationResourceTargetAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationResourceGeneration>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationContributionSelection>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationContributionBinding>().HasQueryFilter("ModerationAuthority",
            entity => ModerationContributionSelections.Any(selection => selection.Id == entity.SelectionId));
        builder.Entity<ModerationNoticeDisclosureApproval>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationNoticeNotificationReceipt>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationQualificationPolicySource>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationQualificationPolicyAdoption>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseIntakeAcknowledgement>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseTargetReceipt>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TargetOriginTrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationQualificationEpoch>().HasQueryFilter("ModerationQualification",
            entity => entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationQualificationEvidence>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationQualificationMutationReceipt>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCase>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationFinding>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationReviewerAssignment>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && (entity.CaseTenantId == null || entity.CaseTenantId == TenantFilterTenantId));
        builder.Entity<ModerationPolicy>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.JurisdictionTrustDomainId == ModerationTrustDomainId
                && (entity.JurisdictionTenantId == null || entity.JurisdictionTenantId == TenantFilterTenantId));
        builder.Entity<ModerationPolicyRule>().HasQueryFilter("ModerationAuthority",
            entity => ModerationPolicies.Any(policy => policy.Id == entity.PolicyId));
        builder.Entity<ModerationDecision>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationSanction>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationProtectiveMeasure>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationOperationReceipt>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId);
        builder.Entity<ModerationEffectIntent>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId);
    }
}
