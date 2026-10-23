using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Moderation.Responses;

public sealed record GetModerationContentResponsesQuery(Guid CaseId, Guid? AfterId = null, int Limit = 32)
    : IQuery<ModerationContentResponseIndexResult>;

/// <summary>Deferred single-use metadata page. Reviewer custody covers HAL preparation and terminal output.</summary>
public sealed class ModerationContentResponseIndexResult(
    IModerationContentResponseRepository repository, Guid tenantId,
    GetModerationContentResponsesQuery query, CancellationToken requestToken)
{
    private int consumed;

    public async Task<ModerationQualificationWriteOutcome> DiscloseAsync(
        Func<IReadOnlyList<ModerationContentResponseSummary>, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0)
            return ModerationQualificationWriteOutcome.Unavailable;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestToken, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        return await repository.DiscloseContentResponseIndexAsync(
            tenantId, query.CaseId, query.AfterId, query.Limit, prepare, write, linked.Token);
    }

    public override string ToString() => nameof(ModerationContentResponseIndexResult);
}

public sealed class GetModerationContentResponsesQueryHandler(
    IModerationContentResponseRepository repository, ITenantContext tenant)
    : IQueryHandler<GetModerationContentResponsesQuery, ModerationContentResponseIndexResult>
{
    public Task<ModerationContentResponseIndexResult> QueryAsync(
        GetModerationContentResponsesQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ModerationContentResponseIndexResult(repository, tenant.TenantId, query, cancellationToken));
}
