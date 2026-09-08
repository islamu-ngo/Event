using Explore.Application.Authorization;
using Explore.Application.DTOs.PaidEventPolicies;
using MediatR;

namespace Explore.Application.Features.PaidEventPolicies.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetInstancePaidEventPolicyQuery : IRequest<PaidEventPolicyDto?>, ISecureRequest
{
    public const string SettingKey = "paid-event-policy";

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
