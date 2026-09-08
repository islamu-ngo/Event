using Explore.Application.DTOs.Tenant;
using MediatR;

namespace Explore.Application.Features.Tenants.Requests.Queries;

public sealed record GetTenantDetailsRequest(Guid Id = default) : IRequest<TenantDto>;
