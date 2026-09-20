using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Tenant;

namespace Explore.Application.Features.Tenants.Requests.Queries;

public sealed record GetTenantDetailsRequest(Guid Id = default) : IQuery<TenantDto?>;
