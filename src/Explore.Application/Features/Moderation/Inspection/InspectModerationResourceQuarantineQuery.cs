using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Inspection;

public sealed record InspectModerationResourceQuarantineQuery(
    ModerationResourceQuarantineInspectionIntent Intent) : IQuery<ModerationResourceQuarantineInspectionResult>
{
    public override string ToString() => nameof(InspectModerationResourceQuarantineQuery);
}

/// <summary>A single-use pending operation. Query construction does not acquire or export authority.</summary>
public sealed class ModerationResourceQuarantineInspectionResult
{
    private readonly IModerationResourceQuarantineInspectionRepository? owner;
    private readonly ModerationResourceQuarantineInspectionIntent intent;
    private int consumed;

    internal ModerationResourceQuarantineInspectionResult(
        IModerationResourceQuarantineInspectionRepository? owner,
        ModerationResourceQuarantineInspectionIntent intent)
    {
        this.owner = owner;
        this.intent = intent;
    }

    public Task<bool> DiscloseAsync(ModerationResourceInspectionOutput write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        return Interlocked.Exchange(ref consumed, 1) == 0 && owner is not null
            ? owner.DiscloseAsync(intent, write, cancellationToken)
            : Task.FromResult(false);
    }

    public override string ToString() => nameof(ModerationResourceQuarantineInspectionResult);
}

public sealed class InspectModerationResourceQuarantineQueryHandler(
    IModerationResourceQuarantineInspectionRepository owner)
    : IQueryHandler<InspectModerationResourceQuarantineQuery, ModerationResourceQuarantineInspectionResult>
{
    public async Task<ModerationResourceQuarantineInspectionResult> QueryAsync(
        InspectModerationResourceQuarantineQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Intent);
        var valid = await new ModerationResourceQuarantineInspectionValidator()
            .ValidateAsync(query.Intent, cancellationToken);
        return new(valid.IsValid ? owner : null, query.Intent);
    }
}

public sealed class ModerationResourceQuarantineInspectionValidator
    : AbstractValidator<ModerationResourceQuarantineInspectionIntent>
{
    public ModerationResourceQuarantineInspectionValidator()
    {
        RuleFor(value => value.QuarantineId).Must(Uuid);
        RuleFor(value => value.ResourceId).NotEmpty();
        RuleFor(value => value.MaterialVersionId).Must(Uuid);
        RuleFor(value => value.CaseId).Must(Uuid);
        RuleFor(value => value.AdoptionId).Must(Uuid);
        RuleFor(value => value.ReviewerAssignmentId).Must(Uuid);
        RuleFor(value => value.ExpectedCaseRevision).GreaterThan(0);
        RuleFor(value => value.ExpectedReviewerRevision).GreaterThanOrEqualTo(3);
        RuleFor(value => value.ExpectedQualificationRevision).GreaterThan(0);
    }

    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
}
