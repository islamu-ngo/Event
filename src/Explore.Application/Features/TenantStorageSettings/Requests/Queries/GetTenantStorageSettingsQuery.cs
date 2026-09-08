using Explore.Application.DTOs.Tenant;
using MediatR;

namespace Explore.Application.Features.TenantStorageSettings.Requests.Queries;

public sealed record GetTenantStorageSettingsQuery : IRequest<TenantStorageSettingsDto>
{
}
