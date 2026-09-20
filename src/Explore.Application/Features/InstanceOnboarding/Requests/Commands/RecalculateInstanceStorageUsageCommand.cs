using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record RecalculateInstanceStorageUsageCommand : ICommand<InstanceStorageUsageDto>
{
}
