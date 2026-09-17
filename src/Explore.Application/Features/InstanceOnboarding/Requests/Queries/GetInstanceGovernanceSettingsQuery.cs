using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Instance;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetInstanceGovernanceSettingsQuery : IQuery<InstanceGovernanceSettings>
{
}
