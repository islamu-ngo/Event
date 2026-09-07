using Explore.Application.DTOs.Instance;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetInstanceGovernanceSettingsQuery : IRequest<InstanceGovernanceSettings>
{
}
