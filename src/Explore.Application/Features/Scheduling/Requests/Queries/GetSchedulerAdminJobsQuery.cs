using Explore.Application.Authorization;
using Explore.Application.DTOs.Scheduling;
using MediatR;

namespace Explore.Application.Features.Scheduling.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetSchedulerAdminJobsQuery : IRequest<IReadOnlyList<SchedulerAdminJobDto>>, ISecureRequest
{
    public const string SettingKey = GetSchedulerAdminOverviewQuery.SettingKey;

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
