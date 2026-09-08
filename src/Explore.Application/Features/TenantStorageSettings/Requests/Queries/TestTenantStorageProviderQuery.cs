using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.TenantStorageSettings.Requests.Queries;

public sealed record TestTenantStorageProviderQuery : IRequest<InstanceStorageProviderStatusDto>
{
}
