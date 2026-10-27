using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Moderation.Notices;

public sealed class GetModerationNoticeStatusQueryHandler(
    IModerationNoticeStatusRepository repository, IAdminContext identity,
    IMachinePrincipalAccessor machine, ITenantContext tenant)
    : IQueryHandler<GetModerationNoticeStatusQuery, ModerationNoticeStatusResult>
{
    public async Task<ModerationNoticeStatusResult> QueryAsync(
        GetModerationNoticeStatusQuery query, CancellationToken cancellationToken = default)
    {
        if (query.DecisionId == Guid.Empty || query.EffectId == Guid.Empty)
            return new(ModerationNoticeReadOutcome.Invalid);
        if (machine.IsMachineCaller || await identity.ResolveUserIdAsync(cancellationToken) is not { } subject
            || subject == Guid.Empty)
            return new(ModerationNoticeReadOutcome.Unauthenticated);
        if (tenant.TenantId == Guid.Empty) return new(ModerationNoticeReadOutcome.NotFound);
        return new(ModerationNoticeReadOutcome.Allowed, new(repository,
            new(tenant.TenantId, subject, query.DecisionId, query.EffectId), cancellationToken));
    }
}
