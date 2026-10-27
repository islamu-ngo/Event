using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using FluentValidation;
using System.Text.Json.Serialization;

namespace Explore.Application.Features.Moderation.Inspection;

public sealed record ReadModerationResourceQuarantineReviewStateQuery(
    ModerationResourceQuarantineReviewStateIntent Intent) : IQuery<ModerationResourceQuarantineReviewStateResult>
{
    public override string ToString() => nameof(ReadModerationResourceQuarantineReviewStateQuery);
}

public sealed record ModerationResourceQuarantineReviewStateItem(
    Guid Id, Guid PurposeId, Guid? PredecessorId, ResourceSafetyReviewStep Step,
    ResourceSafetyAttestation? Attestation, int RequiredApprovals, long MaximumEvidenceLifetimeTicks,
    DateTimeOffset RecordedAt, DateTimeOffset ValidUntil);

public sealed record ModerationResourceQuarantineReviewState(
    Guid ResourceId, Guid MaterialVersionId, Guid QuarantineId, Guid TipId, Guid? AdoptionId,
    DateTimeOffset ValidUntil, IReadOnlyList<ModerationResourceQuarantineReviewStateItem> Chain,
    [property: JsonIgnore] IReadOnlyList<ResourceSafetyReviewStep> AllowedSteps,
    [property: JsonIgnore] bool CanInspect, [property: JsonIgnore] bool CanRelease);

/// <summary>One terminal read of qualified metadata. No material or historical notice is returned.</summary>
public sealed class ModerationResourceQuarantineReviewStateResult(
    IModerationResourceQuarantineReviewStateRepository? owner, ModerationResourceQuarantineReviewStateIntent intent)
{
    private int consumed;

    public Task<bool> DiscloseAsync(Func<ModerationResourceQuarantineReviewState,
        Func<CancellationToken, Task<bool>>, CancellationToken, Task<bool>> write, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(write);
        return Interlocked.Exchange(ref consumed, 1) == 0 && owner is not null
            ? owner.DiscloseAsync(intent, (entry, current, ct) => write(new(
                intent.ResourceId, intent.MaterialVersionId, intent.QuarantineId, intent.TipId,
                entry.Chain.Count >= 2 ? entry.Chain[1].Id : null, entry.ValidUntil,
                entry.Chain.Select(row => new ModerationResourceQuarantineReviewStateItem(
                    row.Id, row.PurposeId, row.PredecessorId, row.Step, row.Attestation,
                    row.RequiredApprovals, row.MaximumEvidenceLifetimeTicks, row.RecordedAt, row.ValidUntil)).ToArray(),
                entry.AllowedSteps, entry.CanInspect, entry.CanRelease), current, ct), token)
            : Task.FromResult(false);
    }

    public override string ToString() => nameof(ModerationResourceQuarantineReviewStateResult);
}

public sealed class ReadModerationResourceQuarantineReviewStateQueryHandler(
    IModerationResourceQuarantineReviewStateRepository owner)
    : IQueryHandler<ReadModerationResourceQuarantineReviewStateQuery, ModerationResourceQuarantineReviewStateResult>
{
    public async Task<ModerationResourceQuarantineReviewStateResult> QueryAsync(
        ReadModerationResourceQuarantineReviewStateQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Intent);
        var valid = await new ModerationResourceQuarantineReviewStateValidator().ValidateAsync(query.Intent, cancellationToken);
        return new(valid.IsValid ? owner : null, query.Intent);
    }
}

public sealed class ModerationResourceQuarantineReviewStateValidator
    : AbstractValidator<ModerationResourceQuarantineReviewStateIntent>
{
    public ModerationResourceQuarantineReviewStateValidator()
    {
        RuleFor(row => row.ResourceId).NotEmpty();
        RuleFor(row => row.MaterialVersionId).Must(Uuid);
        RuleFor(row => row.QuarantineId).Must(Uuid);
        RuleFor(row => row.TipId).Must(Uuid);
        RuleFor(row => row.CaseId).Must(Uuid);
        RuleFor(row => row.ReviewerAssignmentId).Must(Uuid);
        RuleFor(row => row.ExpectedCaseRevision).GreaterThan(0);
        RuleFor(row => row.ExpectedReviewerRevision).GreaterThanOrEqualTo(3);
        RuleFor(row => row.ExpectedQualificationRevision).GreaterThan(0);
    }

    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
}
