using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Queries;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Queries;

public sealed class GetModerationCurrentPolicyQueryHandler(
    IModerationPolicyPublicationRepository repository, IAdminContext identity, ITenantContext tenant)
    : IQueryHandler<GetModerationCurrentPolicyQuery, ModerationCurrentPolicyReadResult>
{
    public async Task<ModerationCurrentPolicyReadResult> QueryAsync(
        GetModerationCurrentPolicyQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Scope is not ("tenant" or "instance")
            || query.ProspectivePolicyId is { } prospective
                && (prospective == Guid.Empty || prospective.Version != 7 || prospective.Variant is < 8 or > 11))
            return new(ModerationCurrentPolicyReadOutcome.Invalid);
        // Capture identity, not permission. Native qualification is independently acquired at use.
        var human = await identity.ResolveUserIdAsync(cancellationToken);
        var request = new ModerationCurrentPolicyReadRequest(
            query.Scope == "tenant" ? ModerationScopeKind.Tenant : ModerationScopeKind.Instance, tenant.TenantId, human);
        return new(repository, request, cancellationToken, query.ProspectivePolicyId);
    }
}
