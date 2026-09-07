using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record RecalculateInstanceStorageUsageCommand : IRequest<InstanceStorageUsageDto>
{
}
