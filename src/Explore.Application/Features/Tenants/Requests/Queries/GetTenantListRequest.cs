using System.Collections.Generic;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Tenant;

namespace Explore.Application.Features.Tenants.Requests.Queries;

public sealed record GetTenantListRequest : IQuery<List<TenantListDto>>
{
}
