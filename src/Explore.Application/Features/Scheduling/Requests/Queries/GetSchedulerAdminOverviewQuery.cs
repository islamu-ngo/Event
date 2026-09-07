using Explore.Application.Authorization;
using Explore.Application.DTOs.Scheduling;
using MediatR;

namespace Explore.Application.Features.Scheduling.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetSchedulerAdminOverviewQuery : IRequest<SchedulerAdminOverviewDto>, ISecureRequest
{
    public const string SettingKey = "scheduler.admin";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
