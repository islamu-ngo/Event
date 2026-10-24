using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class OpenModerationCaseCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<OpenModerationCaseCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        OpenModerationCaseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationCommandValidator<OpenModerationCaseCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var mutation = command.ToMutation();
        var result = await sources.ExecuteAsync(mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class RecordModerationCaseResponseCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationCaseResponseCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationCaseResponseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationCommandValidator<RecordModerationCaseResponseCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var mutation = command.ToMutation();
        var result = await sources.ExecuteAsync(mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class AssignModerationReviewerCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AssignModerationReviewerCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AssignModerationReviewerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationCommandValidator<AssignModerationReviewerCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var mutation = command.ToMutation();
        var result = await sources.ExecuteAsync(mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class QualifyModerationReviewerControllerCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<QualifyModerationReviewerControllerCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        QualifyModerationReviewerControllerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationCommandValidator<QualifyModerationReviewerControllerCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var mutation = command.ToMutation();
        var result = await sources.ExecuteAsync(mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class AssessModerationReviewerConflictCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AssessModerationReviewerConflictCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AssessModerationReviewerConflictCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationCommandValidator<AssessModerationReviewerConflictCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var mutation = command.ToMutation();
        var result = await sources.ExecuteAsync(mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class WithdrawModerationReviewerCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationReviewerCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationReviewerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationCommandValidator<WithdrawModerationReviewerCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var mutation = command.ToMutation();
        var result = await sources.ExecuteAsync(mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class QualifyModerationCaseUrgencyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<QualifyModerationCaseUrgencyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        QualifyModerationCaseUrgencyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationCommandValidator<QualifyModerationCaseUrgencyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var mutation = command.ToMutation();
        var result = await sources.ExecuteAsync(mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class FinalizeModerationFindingCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<FinalizeModerationFindingCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        FinalizeModerationFindingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationCommandValidator<FinalizeModerationFindingCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var mutation = command.ToMutation();
        var result = await sources.ExecuteAsync(mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class ReverseModerationFindingCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<ReverseModerationFindingCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        ReverseModerationFindingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationCommandValidator<ReverseModerationFindingCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var mutation = command.ToMutation();
        var result = await sources.ExecuteAsync(mutation, cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

/// <summary>Checks authorable structure only; current identity, independence, policy and validity remain repository-owned.</summary>
public sealed class ModerationQualificationCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationQualificationCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(IsUuidV7);
        RuleFor(command => command.Target).NotNull();
        RuleFor(command => command.ExpectedQualificationRevision).GreaterThan(0);
        RuleFor(command => command).Custom((command, context) =>
        {
            var valid = command switch
            {
                OpenModerationCaseCommand source => IsUuidV7(source.CaseId)
                    && source.SubjectId != Guid.Empty && IsUuidV7(source.OccurrenceId)
                    && source.SubjectKind is ModerationSubjectKind.Human or ModerationSubjectKind.Service
                        or ModerationSubjectKind.Organization or ModerationSubjectKind.Content,
                RecordModerationCaseResponseCommand source => IsUuidV7(source.EvidenceId) && IsUuidV7(source.CaseId)
                    && source.ExpectedCaseRevision > 0 && EvidenceText(source.Statement),
                AssignModerationReviewerCommand source => IsUuidV7(source.AssignmentId) && IsUuidV7(source.CaseId)
                    && source.ReviewerHumanId != Guid.Empty && source.ExpectedCaseRevision > 0,
                QualifyModerationReviewerControllerCommand source => IsUuidV7(source.AssignmentId)
                    && source.ControllerHumanId != Guid.Empty && IsUuidV7(source.EvidenceId)
                    && source.ExpectedAssignmentRevision > 0 && EvidenceText(source.Justification) && Instant(source.QualifiedUntil),
                AssessModerationReviewerConflictCommand source => IsUuidV7(source.AssignmentId) && IsUuidV7(source.EvidenceId)
                    && source.ExpectedAssignmentRevision > 0 && EvidenceText(source.Justification)
                    && source.State is ModerationReviewerConflictState.Independent or ModerationReviewerConflictState.Conflicted,
                WithdrawModerationReviewerCommand source => IsUuidV7(source.AssignmentId) && source.ExpectedAssignmentRevision > 0,
                QualifyModerationCaseUrgencyCommand source => IsUuidV7(source.EvidenceId) && IsUuidV7(source.CaseId)
                    && source.ExpectedCaseRevision > 0 && EvidenceText(source.Justification) && Instant(source.ValidUntil),
                FinalizeModerationFindingCommand source => IsUuidV7(source.FindingId) && IsUuidV7(source.CaseId)
                    && IsUuidV7(source.PolicyId) && IsUuidV7(source.EvidenceId) && source.ExpectedCaseRevision > 0
                    && source.ExpectedPolicyRevision > 0 && source.Rule is not null && source.Reason is not null
                    && FindingText(source.Justification),
                ReverseModerationFindingCommand source => IsUuidV7(source.FindingId) && IsUuidV7(source.ReversalReferenceId)
                    && source.ExpectedFindingRevision > 0 && FindingText(source.Justification),
                _ => false
            };
            if (!valid)
                context.AddFailure("Qualification source intent is incomplete or semantically invalid.");
        });
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
    private static bool EvidenceText(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= 4000 && value.Any(char.IsLetterOrDigit);
    private static bool FindingText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4000;
    private static bool Instant(DateTimeOffset value) => value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
