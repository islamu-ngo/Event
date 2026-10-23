using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationResourceSafetyReviewCommandHandler(IModerationResourceQuarantineReleaseRepository repository)
    : ICommandHandler<RecordModerationResourceSafetyReviewCommand, ModerationResourceSafetyResponse>
{
    public async Task<ModerationResourceSafetyResponse> ExecuteAsync(
        RecordModerationResourceSafetyReviewCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationResourceSafetyReviewValidator().ValidateAsync(command.Intent, cancellationToken);
        if (!validation.IsValid) return ModerationResourceSafetyResponse.Invalid(validation.Errors.Select(row => row.ErrorMessage));
        var result = await repository.ReviewAsync(command.Intent, cancellationToken);
        return ModerationResourceSafetyResponse.From(result.Outcome, command.Intent.OperationId,
            result.Receipt?.Id, result.Receipt?.RecordedAt);
    }
}

public sealed class ReleaseModerationResourceQuarantineCommandHandler(IModerationResourceQuarantineReleaseRepository repository)
    : ICommandHandler<ReleaseModerationResourceQuarantineCommand, ModerationResourceSafetyResponse>
{
    public async Task<ModerationResourceSafetyResponse> ExecuteAsync(
        ReleaseModerationResourceQuarantineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationResourceQuarantineReleaseValidator().ValidateAsync(command.Intent, cancellationToken);
        if (!validation.IsValid) return ModerationResourceSafetyResponse.Invalid(validation.Errors.Select(row => row.ErrorMessage));
        var result = await repository.ReleaseAsync(command.Intent, cancellationToken);
        return ModerationResourceSafetyResponse.From(result.Outcome, command.Intent.OperationId,
            result.Receipt?.Id, result.Receipt?.ReleasedAt);
    }
}

public sealed class ModerationResourceSafetyReviewValidator : AbstractValidator<ModerationResourceSafetyReviewIntent>
{
    public ModerationResourceSafetyReviewValidator()
    {
        RuleFor(row => row.OperationId).Must(Version7);
        RuleFor(row => row.QuarantineId).Must(Version7);
        RuleFor(row => row.MaterialVersionId).Must(Version7);
        RuleFor(row => row.CaseId).Must(Version7);
        RuleFor(row => row.ReviewerAssignmentId).Must(Version7);
        RuleFor(row => row.ResourceId).NotEmpty();
        RuleFor(row => row.ExpectedCaseRevision).GreaterThan(0);
        RuleFor(row => row.ExpectedReviewerRevision).GreaterThanOrEqualTo(3);
        RuleFor(row => row.Step).IsInEnum();
        RuleFor(row => row.ValidUntil).Must(value => value.Offset == TimeSpan.Zero
            && value > DateTimeOffset.MinValue && value < DateTimeOffset.MaxValue);
        RuleFor(row => row).Must(row => row.Step == ResourceSafetyReviewStep.ProposePurpose
            ? row.PredecessorId is null && row.RequiredApprovals is 1 or 2
                && row.MaximumEvidenceLifetime > TimeSpan.Zero && row.MaximumEvidenceLifetime < TimeSpan.MaxValue
            : row.PredecessorId is { } id && Version7(id) && id != row.OperationId
                && row.RequiredApprovals == 0 && row.MaximumEvidenceLifetime == TimeSpan.Zero);
        RuleFor(row => row).Must(row => row.Step == ResourceSafetyReviewStep.AttestMaterialSafety
            ? row.Attestation is { } attestation && Enum.IsDefined(attestation)
            : row.Attestation is null);
    }

    internal static bool Version7(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
}

public sealed class ModerationResourceQuarantineReleaseValidator : AbstractValidator<ModerationResourceQuarantineReleaseIntent>
{
    public ModerationResourceQuarantineReleaseValidator()
    {
        RuleFor(row => row.OperationId).Must(ModerationResourceSafetyReviewValidator.Version7);
        RuleFor(row => row.QuarantineId).Must(ModerationResourceSafetyReviewValidator.Version7);
        RuleFor(row => row.MaterialVersionId).Must(ModerationResourceSafetyReviewValidator.Version7);
        RuleFor(row => row.ApprovalId).Must(ModerationResourceSafetyReviewValidator.Version7);
        RuleFor(row => row.ResourceId).NotEmpty();
    }
}
