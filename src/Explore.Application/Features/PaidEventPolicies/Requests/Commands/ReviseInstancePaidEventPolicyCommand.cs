using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies.Requests.Queries;
using Explore.Application.Responses;

namespace Explore.Application.Features.PaidEventPolicies.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record ReviseInstancePaidEventPolicyCommand(RevisePaidEventPolicyDto Policy)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => GetInstancePaidEventPolicyQuery.SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
