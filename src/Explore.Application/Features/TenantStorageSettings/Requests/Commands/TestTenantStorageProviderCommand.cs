using Explore.Application.DTOs.Onboarding;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TenantStorageSettings.Requests.Commands;

/// <summary>Tests the effective provider, including a write/delete permissions probe.</summary>
public sealed record TestTenantStorageProviderCommand : ICommand<InstanceStorageProviderStatusDto>
{
}
