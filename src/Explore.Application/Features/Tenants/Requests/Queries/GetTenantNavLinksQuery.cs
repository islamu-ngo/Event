using System.Collections.Generic;
using Explore.Application.DTOs.Tenant;
using MediatR;

namespace Explore.Application.Features.Tenants.Requests.Queries;

/// <summary>
/// Query to retrieve all navigation links for the current tenant.
/// Returns links ordered by their display order.
/// </summary>
public sealed record GetTenantNavLinksQuery : IRequest<List<TenantNavigationLinkDto>>
{
}
