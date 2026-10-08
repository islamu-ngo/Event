using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
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
                OpenModerationCaseSource source => IsUuidV7(source.CaseId) && IsUuidV7(source.SubjectId)
                    && IsUuidV7(source.OccurrenceId) && Enum.IsDefined(source.SubjectKind),
                RecordModerationCaseResponse source => IsUuidV7(source.EvidenceId) && IsUuidV7(source.CaseId)
                    && source.ExpectedCaseRevision > 0 && Text(source.Statement),
                AssignModerationReviewerSource source => IsUuidV7(source.AssignmentId) && IsUuidV7(source.CaseId)
                    && IsUuidV7(source.ReviewerHumanId) && source.ExpectedCaseRevision > 0,
                QualifyModerationReviewerController source => IsUuidV7(source.AssignmentId)
                    && IsUuidV7(source.ControllerHumanId) && IsUuidV7(source.EvidenceId)
                    && source.ExpectedAssignmentRevision > 0 && Text(source.Justification) && Instant(source.QualifiedUntil),
                AssessModerationReviewerConflict source => IsUuidV7(source.AssignmentId) && IsUuidV7(source.EvidenceId)
                    && source.ExpectedAssignmentRevision > 0 && Text(source.Justification)
                    && source.State is ModerationReviewerConflictState.Independent or ModerationReviewerConflictState.Conflicted,
                WithdrawModerationReviewerSource source => IsUuidV7(source.AssignmentId) && source.ExpectedAssignmentRevision > 0,
                QualifyModerationCaseUrgency source => IsUuidV7(source.EvidenceId) && IsUuidV7(source.CaseId)
                    && source.ExpectedCaseRevision > 0 && Text(source.Justification) && Instant(source.ValidUntil),
                FinalizeModerationFindingSource source => IsUuidV7(source.FindingId) && IsUuidV7(source.CaseId)
                    && IsUuidV7(source.PolicyId) && IsUuidV7(source.EvidenceId) && source.ExpectedCaseRevision > 0
                    && source.ExpectedPolicyRevision > 0 && source.Rule is not null && Text(source.Justification),
                ReverseModerationFindingSource source => IsUuidV7(source.FindingId) && IsUuidV7(source.ReversalReferenceId)
                    && source.ExpectedFindingRevision > 0 && Text(source.Justification),
                AdoptModerationPolicySource source => IsUuidV7(source.PolicyId) && IsUuidV7(source.PublicationId)
                    && source.ExpectedPolicyRevision >= 0 && source.Rules is { Count: > 0 }
                    && source.Rules.Select(rule => rule?.Id).Distinct().Count() == source.Rules.Count
                    && source.Rules.All(rule => rule is not null && IsUuidV7(rule.Id) && rule.Revision > 0
                        && !string.IsNullOrWhiteSpace(rule.Code) && rule.Code.Length <= 100
                        && rule.Code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                        && Text(rule.Meaning)
                        && rule.Purposes is { Count: > 0 } && rule.Purposes.All(Enum.IsDefined)
                        && rule.Capabilities is { Count: > 0 } && rule.Capabilities.All(capability =>
                            Enum.IsDefined(capability) && !EffectiveRestrictions.IsProtectedRemedy(capability))
                        && rule.MaximumFiniteDuration > TimeSpan.Zero && rule.MaximumReviewDelay > TimeSpan.Zero
                        && rule.MinimumApprovals is 1 or 2 && rule.MaximumApprovals >= rule.MinimumApprovals
                        && rule.MaximumApprovals <= 2 && (!rule.AllowsIndefinite || rule.MaximumApprovals == 2)),
                _ => false
            };
            if (!valid)
                context.AddFailure("Qualification source intent is incomplete or semantically invalid.");
        });
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4000
        && value.Any(char.IsLetterOrDigit);
    private static bool Instant(DateTimeOffset value) => value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
