using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Inspection;

public sealed record ReadModerationResourceQuarantineStateQuery(
    ModerationResourceQuarantineStateIntent Intent) : IQuery<ModerationResourceQuarantineStateResult>
{
    public override string ToString() => nameof(ReadModerationResourceQuarantineStateQuery);
}

public sealed record ModerationResourceQuarantineStateItem(
    Guid QuarantineId, Guid MaterialVersionId, DateTimeOffset? ReviewDueAt,
    DateTimeOffset? ReviewedAt, ResourceSafetyAttestation? Disposition,
    Guid? ReleaseId, DateTimeOffset? ReleasedAt);

public sealed record ModerationResourceQuarantineStatePage(
    Guid ResourceId, int Page, bool HasNext, IReadOnlyList<ModerationResourceQuarantineStateItem> Items);

/// <summary>Single-use pending metadata read. Construction cannot authorize inspection or release.</summary>
public sealed class ModerationResourceQuarantineStateResult(
    IModerationResourceQuarantineStateRepository? owner, ModerationResourceQuarantineStateIntent intent)
{
    private int consumed;

    public Task<bool> DiscloseAsync(Func<ModerationResourceQuarantineStatePage,
        Func<CancellationToken, Task<bool>>, CancellationToken, Task<bool>> write, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(write);
        return Interlocked.Exchange(ref consumed, 1) == 0 && owner is not null
            ? owner.DiscloseAsync(intent, (entries, hasNext, current, cancellationToken) => write(
                new(intent.ResourceId, intent.Page, hasNext, entries.Select(row => new ModerationResourceQuarantineStateItem(
                    row.Cause.Id, row.Cause.MaterialVersionId, row.Duty?.DueAt,
                    row.Completion?.CompletedAt, row.Completion?.Disposition,
                    row.Release?.Id, row.Release?.ReleasedAt)).ToArray()), current, cancellationToken), token)
            : Task.FromResult(false);
    }

    public override string ToString() => nameof(ModerationResourceQuarantineStateResult);
}

public sealed class ReadModerationResourceQuarantineStateQueryHandler(IModerationResourceQuarantineStateRepository owner)
    : IQueryHandler<ReadModerationResourceQuarantineStateQuery, ModerationResourceQuarantineStateResult>
{
    public async Task<ModerationResourceQuarantineStateResult> QueryAsync(
        ReadModerationResourceQuarantineStateQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Intent);
        var valid = await new ModerationResourceQuarantineStateValidator().ValidateAsync(query.Intent, cancellationToken);
        return new(valid.IsValid ? owner : null, query.Intent);
    }
}

public sealed class ModerationResourceQuarantineStateValidator : AbstractValidator<ModerationResourceQuarantineStateIntent>
{
    public ModerationResourceQuarantineStateValidator()
    {
        RuleFor(row => row.ResourceId).NotEmpty();
        RuleFor(row => row.MaterialVersionId).Must(Uuid);
        RuleFor(row => row.CaseId).Must(Uuid);
        RuleFor(row => row.ReviewerAssignmentId).Must(Uuid);
        RuleFor(row => row.ExpectedCaseRevision).GreaterThan(0);
        RuleFor(row => row.ExpectedReviewerRevision).GreaterThanOrEqualTo(3);
        RuleFor(row => row.ExpectedQualificationRevision).GreaterThan(0);
        RuleFor(row => row.Page).InclusiveBetween(1, 10_000);
    }

    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
}
