using System.Collections.Generic;
using Explore.Application.DTOs.Tenant;
using MediatR;

namespace Explore.Application.Features.Tenants.Requests.Queries;

public sealed record GetTenantListRequest : IRequest<List<TenantListDto>>
{
}
