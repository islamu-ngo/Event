using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Scheduling;

namespace Explore.Application.Features.Scheduling.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetSchedulerAdminOverviewQuery : IQuery<SchedulerAdminOverviewDto>, ISecureRequest
{
    public const string SettingKey = "scheduler.admin";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
