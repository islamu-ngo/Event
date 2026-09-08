// Used by deployment mode toggle to enforce single-tenant revert safeguards.

using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetActiveTenantCountQuery : IRequest<int>
{
}
