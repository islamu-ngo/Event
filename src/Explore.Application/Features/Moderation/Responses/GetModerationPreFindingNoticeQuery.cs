using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Moderation.Responses;

public sealed record GetModerationPreFindingNoticeQuery(Guid NoticeId, bool ReviewResponse = false)
    : IQuery<ModerationPreFindingNoticeResult>;

/// <summary>Private single-use disclosure; response text never leaves retained native custody before terminal write.</summary>
public sealed class ModerationPreFindingNoticeResult(
    IModerationPreFindingRepository repository, Guid tenantId, Guid noticeId, bool reviewResponse, CancellationToken requestToken)
{
    private int consumed;

    public async Task<ModerationQualificationWriteOutcome> DiscloseAsync(
        Func<ModerationPreFindingView, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0 || noticeId == Guid.Empty)
            return ModerationQualificationWriteOutcome.Unavailable;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestToken, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        return await repository.DisclosePreFindingAsync(tenantId, noticeId, reviewResponse, prepare, write, linked.Token);
    }

    public override string ToString() => nameof(ModerationPreFindingNoticeResult);
}

public sealed class GetModerationPreFindingNoticeQueryHandler(IModerationPreFindingRepository repository, ITenantContext tenant)
    : IQueryHandler<GetModerationPreFindingNoticeQuery, ModerationPreFindingNoticeResult>
{
    public Task<ModerationPreFindingNoticeResult> QueryAsync(
        GetModerationPreFindingNoticeQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ModerationPreFindingNoticeResult(
            repository, tenant.TenantId, query.NoticeId, query.ReviewResponse, cancellationToken));
}
