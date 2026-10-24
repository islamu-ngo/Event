using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Moderation.Responses;

public sealed record GetModerationContentResponseStatementQuery(Guid ResponseId)
    : IQuery<ModerationContentResponseStatementResult>;

/// <summary>Single-use reviewer disclosure. No private response text leaves its retained privacy and native authority custody.</summary>
public sealed class ModerationContentResponseStatementResult(
    IModerationContentResponseRepository repository, Guid tenantId, Guid responseId, CancellationToken requestToken)
{
    private int consumed;

    public async Task<ModerationQualificationWriteOutcome> DiscloseAsync(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0 || responseId == Guid.Empty)
            return ModerationQualificationWriteOutcome.Unavailable;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestToken, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        return await repository.DiscloseContentResponseStatementAsync(tenantId, responseId, write, linked.Token);
    }

    public override string ToString() => nameof(ModerationContentResponseStatementResult);
}

public sealed class GetModerationContentResponseStatementQueryHandler(
    IModerationContentResponseRepository repository, ITenantContext tenant)
    : IQueryHandler<GetModerationContentResponseStatementQuery, ModerationContentResponseStatementResult>
{
    public Task<ModerationContentResponseStatementResult> QueryAsync(
        GetModerationContentResponseStatementQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ModerationContentResponseStatementResult(repository, tenant.TenantId, query.ResponseId, cancellationToken));
}
