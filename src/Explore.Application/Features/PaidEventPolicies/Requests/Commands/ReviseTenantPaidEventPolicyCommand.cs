using Explore.Application.Authorization;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.PaidEventPolicies.Requests.Commands;

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.Update)]
public sealed record ReviseTenantPaidEventPolicyCommand(Guid TenantId, RevisePaidEventPolicyDto Policy)
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : $"{TenantId}:paid-event-policy";

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(TenantId);
}
