using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Moderation.Notices;

public sealed class GetModerationNoticesQueryHandler(
    IModerationNoticeReadRepository repository, IAdminContext identity,
    IMachinePrincipalAccessor machine, ITenantContext tenant)
    : IQueryHandler<GetModerationNoticesQuery, ModerationNoticeReadResult>
{
    public Task<ModerationNoticeReadResult> QueryAsync(
        GetModerationNoticesQuery query, CancellationToken cancellationToken = default) =>
        ModerationNoticeReadProjection.PrepareAsync(
            repository, identity, machine, tenant, null, query.AfterId, query.Limit, cancellationToken);
}

public sealed class GetModerationNoticeQueryHandler(
    IModerationNoticeReadRepository repository, IAdminContext identity,
    IMachinePrincipalAccessor machine, ITenantContext tenant)
    : IQueryHandler<GetModerationNoticeQuery, ModerationNoticeReadResult>
{
    public Task<ModerationNoticeReadResult> QueryAsync(
        GetModerationNoticeQuery query, CancellationToken cancellationToken = default) =>
        query.Id == Guid.Empty
            ? Task.FromResult(new ModerationNoticeReadResult(ModerationNoticeReadOutcome.Invalid))
            : ModerationNoticeReadProjection.PrepareAsync(
                repository, identity, machine, tenant, query.Id, null, 1, cancellationToken);
}
