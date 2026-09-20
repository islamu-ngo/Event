// Used by deployment mode toggle to enforce single-tenant revert safeguards.

using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetActiveTenantCountQuery : IQuery<int>
{
}
