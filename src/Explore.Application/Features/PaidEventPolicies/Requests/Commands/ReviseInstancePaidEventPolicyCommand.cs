using Explore.Application.Authorization;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.PaidEventPolicies.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record ReviseInstancePaidEventPolicyCommand(RevisePaidEventPolicyDto Policy)
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => GetInstancePaidEventPolicyQuery.SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
