using Explore.Application.DTOs.Tenant;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TenantStorageSettings.Requests.Queries;

public sealed record GetTenantStorageSettingsQuery : IQuery<TenantStorageSettingsDto>
{
}
