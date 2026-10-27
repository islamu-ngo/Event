using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class ApplyModerationQualificationCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<ApplyModerationQualificationCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        ApplyModerationQualificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var result = await sources.ExecuteAsync(command.Mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.Mutation.OperationId, result);
    }
}

public sealed class ModerationQualificationValidator : AbstractValidator<ApplyModerationQualificationCommand>
{
    public ModerationQualificationValidator()
    {
        RuleFor(command => command.Mutation).NotNull().Custom((mutation, context) =>
        {
            if (mutation is null) return;
            if (!IsUuidV7(mutation.OperationId) || mutation.Target is null || mutation.ExpectedQualificationRevision < 1)
                context.AddFailure("Qualification requires an operation identity, target and expected generation.");

            var valid = mutation switch
            {
                RecordModerationOperationalBudgetSource or AdoptModerationOperationalBudgetSource
                    or WithdrawModerationOperationalBudgetAdoption => ModerationOperationalBudgetQualification.IsValid(mutation),
                RecordModerationParticipantIncidentResponsibilitySource investigation =>
                    ModerationParticipantInvestigationQualification.IsValid(investigation),
                RecordModerationCaseNarrativePolicySource or AdoptModerationCaseNarrativePolicySource
                    or WithdrawModerationCaseNarrativeAdoption => ModerationCaseNarrativeQualification.IsValid(mutation),
                EnrollModerationMeasurementIssuerSource source => IsUuidV7(source.EnrollmentId) && IsUuidV7(source.KeyId)
                    && source.EnrollmentId != source.KeyId && source.Target?.Kind == ModerationScopeKind.Tenant
                    && source.Issuer is { Length: > 0 and <= 128 }
                    && source.Issuer.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
                    && source.PublicKeySpkiBase64 is { Length: > 0 and <= 1024 }
                    && Hex(source.RunnerDigest) && Hex(source.CatalogueDigest) && Hex(source.TestBundleDigest)
                    && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && Instant(source.ValidUntil) && source.ValidUntil.Offset == TimeSpan.Zero,
                AcceptModerationConsumerMeasurementSource source => IsUuidV7(source.ReceiptId) && IsUuidV7(source.ChallengeId)
                    && IsUuidV7(source.IssuerEnrollmentId) && source.Target?.Kind == ModerationScopeKind.Tenant
                    && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && source.Binding is { } acceptedBinding && IsUuidV7(acceptedBinding.PolicyId)
                    && IsUuidV7(acceptedBinding.PublicationId) && acceptedBinding.PolicyRevision is > 0 and < long.MaxValue
                    && acceptedBinding.Jurisdiction is not null && acceptedBinding.Rule is not null
                    && acceptedBinding.Rule.OwnerId == acceptedBinding.Jurisdiction.Id
                    && Enum.IsDefined(acceptedBinding.Capability)
                    && acceptedBinding.Mode == Explore.Application.Services.Moderation.ModerationNativeMode.Embedded
                    && source.SignedToken is { Length: > 0 and <= 65536 }
                    && source.SignedToken.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'),
                RevokeModerationMeasurementIssuerSource source => IsUuidV7(source.EnrollmentId)
                    && source.Target?.Kind == ModerationScopeKind.Tenant && source.ExpectedEnrollmentRevision == 1
                    && source.ExpectedQualificationRevision < long.MaxValue - 1,
                PrepareModerationConsumerMeasurementSource source => IsUuidV7(source.ChallengeId)
                    && IsUuidV7(source.InstallationId) && source.ExpectedInstallationRevision == 1
                    && ModerationConsumerInstallation.IsConsumerId(source.ConsumerId) && source.Binding is { } binding
                    && IsUuidV7(binding.PolicyId) && IsUuidV7(binding.PublicationId) && binding.PolicyRevision is > 0 and < long.MaxValue
                    && binding.Jurisdiction is not null && binding.Rule is not null
                    && binding.Rule.OwnerId == binding.Jurisdiction.Id && Enum.IsDefined(binding.Capability)
                    && binding.Mode == Explore.Application.Services.Moderation.ModerationNativeMode.Embedded
                    && source.ExpectedRuntimeGeneration is { Length: 64 } && source.ExpectedRuntimeGeneration.All(char.IsAsciiHexDigit)
                    && source.ExpectedInventoryFingerprint is { Length: 64 } && source.ExpectedInventoryFingerprint.All(char.IsAsciiHexDigit)
                    && source.Target?.Kind == ModerationScopeKind.Tenant && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && Instant(source.ValidUntil) && source.ValidUntil.Offset == TimeSpan.Zero,
                InstallModerationConsumerSource source => IsUuidV7(source.InstallationId)
                    && ModerationConsumerInstallation.IsConsumerId(source.ConsumerId)
                    && source.Mode == Explore.Application.Services.Moderation.ModerationNativeMode.Embedded
                    && source.ExpectedRuntimeGeneration is { Length: 64 } && source.ExpectedRuntimeGeneration.All(char.IsAsciiHexDigit)
                    && source.ExpectedInventoryFingerprint is { Length: 64 } && source.ExpectedInventoryFingerprint.All(char.IsAsciiHexDigit)
                    && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && source.Target?.Kind == ModerationScopeKind.Tenant
                    && Instant(source.ValidUntil) && source.ValidUntil.Offset == TimeSpan.Zero,
                WithdrawModerationConsumerSource source => IsUuidV7(source.InstallationId)
                    && source.ExpectedInstallationRevision == 1 && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && source.Target?.Kind == ModerationScopeKind.Tenant,
                AdoptModerationProfileSource source => IsUuidV7(source.AdoptionId) && IsUuidV7(source.SelectionId)
                    && IsUuidV7(source.CaseId) && Enum.IsDefined(source.Mode)
                    && source.ExpectedCaseRevision is > 0 and < long.MaxValue
                    && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && Instant(source.ValidUntil) && source.ValidUntil.Offset == TimeSpan.Zero,
                RevokeModerationProfileSource source => IsUuidV7(source.AdoptionId) && IsUuidV7(source.CaseId)
                    && source.ExpectedAdoptionRevision == 1 && source.ExpectedCaseRevision is > 0 and < long.MaxValue,
                AdoptModerationEventTargetSource source => IsUuidV7(source.AdoptionId) && IsUuidV7(source.SelectionId)
                    && IsUuidV7(source.GenerationId) && IsUuidV7(source.CaseId) && source.BindingOrdinal is >= 0 and < 64
                    && source.ExpectedGenerationRevision is > 0 and < long.MaxValue
                    && source.ExpectedCaseRevision is > 0 and < long.MaxValue
                    && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && Instant(source.ValidUntil) && source.ValidUntil.Offset == TimeSpan.Zero,
                RevokeModerationEventTargetSource source => IsUuidV7(source.AdoptionId) && IsUuidV7(source.CaseId)
                    && source.ExpectedAdoptionRevision == 1 && source.ExpectedCaseRevision is > 0 and < long.MaxValue,
                AdoptModerationResourceTargetSource source => IsUuidV7(source.AdoptionId) && IsUuidV7(source.SelectionId)
                    && IsUuidV7(source.GenerationId) && IsUuidV7(source.CaseId) && source.BindingOrdinal is >= 0 and < 64
                    && source.ExpectedGenerationRevision is > 0 and < long.MaxValue
                    && source.ExpectedCaseRevision is > 0 and < long.MaxValue
                    && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && Instant(source.ValidUntil) && source.ValidUntil.Offset == TimeSpan.Zero,
                RevokeModerationResourceTargetSource source => IsUuidV7(source.AdoptionId) && IsUuidV7(source.CaseId)
                    && source.ExpectedAdoptionRevision == 1 && source.ExpectedCaseRevision is > 0 and < long.MaxValue,
                RecordModerationEventGenerationSource source => IsUuidV7(source.SourceId) && source.EventId != Guid.Empty
                    && source.ExpectedEventRevision is >= 0 and < long.MaxValue - 1
                    && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && Instant(source.ValidUntil) && source.ValidUntil.Offset == TimeSpan.Zero,
                RecordModerationResourceGenerationSource source => IsUuidV7(source.SourceId) && source.ResourceId != Guid.Empty
                    && source.ExpectedResourceRevision is >= 0 and < long.MaxValue - 1
                    && source.ExpectedQualificationRevision < long.MaxValue - 1
                    && Instant(source.ValidUntil) && source.ValidUntil.Offset == TimeSpan.Zero,
                RecordModerationContributionSelectionSource source => IsUuidV7(source.SelectionId) && IsUuidV7(source.EffectId)
                    && IsUuidV7(source.CaseId) && IsUuidV7(source.DecisionId) && IsUuidV7(source.PolicyId)
                    && IsUuidV7(source.PublicationId) && IsUuidV7(source.ProfileId)
                    && source.ExpectedCaseRevision is > 0 and < long.MaxValue
                    && source.ExpectedDecisionRevision is > 1 and < long.MaxValue
                    && source.ExpectedPolicyRevision is > 0 and < long.MaxValue
                    && source.ExpectedQualificationRevision < long.MaxValue && source.Contribution is not null
                    && !source.Contribution.Effects.IsDefaultOrEmpty && !source.Contribution.Consequences.IsDefaultOrEmpty
                    && source.Contribution.Effects.Length + source.Contribution.Consequences.Length <= 64
                    && source.Contribution.Effects.All(row => row is not null && row.Effect is not null
                        && row.Target is { TargetRevision: > 0 and < long.MaxValue })
                    && source.Contribution.Consequences.All(row => row is not null
                        && row.Target is { TargetRevision: > 0 and < long.MaxValue })
                    && Instant(source.ValidUntil) && source.ValidUntil.Offset == TimeSpan.Zero,
                ApproveModerationNoticeDisclosureSource source => Disclosure(source),
                RevokeModerationNoticeDisclosureSource source => IsUuidV7(source.ApprovalId) && IsUuidV7(source.CaseId)
                    && source.ExpectedCaseRevision is > 0 and < long.MaxValue && source.ExpectedApprovalRevision == 1,
                PublishModerationQualificationPolicySource source => IsUuidV7(source.SourceId) && IsUuidV7(source.CaseId)
                    && source.ExpectedCaseRevision > 0 && source.ExpectedSourceRevision >= 0
                    && Lifetime(source.MaximumReviewerLifetime) && Lifetime(source.MaximumUrgencyLifetime)
                    && Instant(source.ValidUntil),
                AdoptModerationQualificationPolicySource source => IsUuidV7(source.SourceId) && IsUuidV7(source.AdoptionId)
                    && source.SourceId != source.AdoptionId && IsUuidV7(source.CaseId) && source.ExpectedCaseRevision > 0
                    && source.ExpectedSourceRevision > 0 && source.ExpectedWriterGeneration > 0 && Instant(source.ValidUntil),
                RevokeModerationQualificationPolicyAdoption source => IsUuidV7(source.SourceId) && IsUuidV7(source.AdoptionId)
                    && source.SourceId != source.AdoptionId && IsUuidV7(source.CaseId) && source.ExpectedCaseRevision > 0
                    && source.ExpectedSourceRevision > 0 && source.ExpectedAdoptionRevision == 1,
                OpenModerationCaseSource source => IsUuidV7(source.CaseId) && source.SubjectId != Guid.Empty
                    && IsUuidV7(source.OccurrenceId) && Enum.IsDefined(source.SubjectKind),
                RecordModerationCaseResponse source => IsUuidV7(source.EvidenceId) && IsUuidV7(source.CaseId)
                    && source.ExpectedCaseRevision > 0 && Text(source.Statement),
                AssignModerationReviewerSource source => IsUuidV7(source.AssignmentId) && IsUuidV7(source.CaseId)
                    && source.ReviewerHumanId != Guid.Empty && source.ExpectedCaseRevision > 0,
                QualifyModerationReviewerController source => IsUuidV7(source.AssignmentId)
                    && source.ControllerHumanId != Guid.Empty && IsUuidV7(source.EvidenceId)
                    && source.ExpectedAssignmentRevision > 0 && Text(source.Justification) && Instant(source.QualifiedUntil),
                AssessModerationReviewerConflict source => IsUuidV7(source.AssignmentId) && IsUuidV7(source.EvidenceId)
                    && source.ExpectedAssignmentRevision > 0 && Text(source.Justification)
                    && source.State is ModerationReviewerConflictState.Independent or ModerationReviewerConflictState.Conflicted,
                WithdrawModerationReviewerSource source => IsUuidV7(source.AssignmentId) && source.ExpectedAssignmentRevision > 0,
                QualifyModerationCaseUrgency source => IsUuidV7(source.EvidenceId) && IsUuidV7(source.CaseId)
                    && source.ExpectedCaseRevision > 0 && Text(source.Justification) && Instant(source.ValidUntil),
                FinalizeModerationFindingSource source => IsUuidV7(source.FindingId) && IsUuidV7(source.CaseId)
                    && IsUuidV7(source.PolicyId) && IsUuidV7(source.EvidenceId) && source.ExpectedCaseRevision > 0
                    && source.ExpectedPolicyRevision > 0 && source.Rule is not null && source.Reason is not null
                    && Text(source.Justification),
                ReverseModerationFindingSource source => IsUuidV7(source.FindingId) && IsUuidV7(source.ReversalReferenceId)
                    && source.ExpectedFindingRevision > 0 && Text(source.Justification),
                _ => false
            };
            if (!valid)
                context.AddFailure("Qualification source intent is incomplete or semantically invalid.");
        });
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
    private static bool Hex(string? value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    private static bool Disclosure(ApproveModerationNoticeDisclosureSource source)
    {
        if (source.Intent is null || source.ExpectedQualificationRevision == long.MaxValue) return false;
        try
        {
            source.Intent.Validate();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4000
        && value.Any(char.IsLetterOrDigit);
    private static bool Instant(DateTimeOffset value) => value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
    private static bool Lifetime(TimeSpan value) => value > TimeSpan.Zero && value != TimeSpan.MaxValue;
}
