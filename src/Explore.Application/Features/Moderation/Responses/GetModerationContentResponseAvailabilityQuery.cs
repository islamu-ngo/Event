using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Responses;

public sealed record GetModerationContentResponseAvailabilityQuery(Guid NoticeId)
    : IQuery<ModerationContentResponseAvailabilityResult>;

/// <summary>A deferred, single-use current-authority projection; no private coordinates escape the owner before delivery.</summary>
public sealed class ModerationContentResponseAvailabilityResult(
    IModerationContentResponseRepository repository, Guid tenantId, Guid noticeId, CancellationToken requestToken)
{
    private int consumed;
    public async Task<ModerationQualificationWriteOutcome> DiscloseAsync(
        Func<ModerationContentResponseAvailability, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0 || noticeId == Guid.Empty)
            return ModerationQualificationWriteOutcome.Unavailable;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestToken, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        return await repository.DiscloseContentResponseAvailabilityAsync(tenantId, noticeId, prepare, write, linked.Token);
    }

    public static BaseCommandResponse<Guid> Failure(ModerationQualificationWriteOutcome outcome, Guid operationId = default) =>
        outcome switch
        {
            ModerationQualificationWriteOutcome.Unauthenticated => BaseCommandResponse.Authentication<Guid>(),
            ModerationQualificationWriteOutcome.NotFound or ModerationQualificationWriteOutcome.Denied =>
                BaseCommandResponse.NotFound<Guid>(),
            ModerationQualificationWriteOutcome.Conflict when operationId != Guid.Empty => BaseCommandResponse.Conflict(operationId),
            _ => BaseCommandResponse.Failure<Guid>("ModerationContentResponseUnavailable")
        };

    public override string ToString() => nameof(ModerationContentResponseAvailabilityResult);
}

public sealed class GetModerationContentResponseAvailabilityQueryHandler(
    IModerationContentResponseRepository repository, ITenantContext tenant)
    : IQueryHandler<GetModerationContentResponseAvailabilityQuery, ModerationContentResponseAvailabilityResult>
{
    public Task<ModerationContentResponseAvailabilityResult> QueryAsync(
        GetModerationContentResponseAvailabilityQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ModerationContentResponseAvailabilityResult(repository, tenant.TenantId, query.NoticeId, cancellationToken));
}
