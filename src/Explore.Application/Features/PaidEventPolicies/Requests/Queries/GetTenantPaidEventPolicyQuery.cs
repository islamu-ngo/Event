using Explore.Application.Authorization;
using Explore.Application.DTOs.PaidEventPolicies;
using MediatR;

namespace Explore.Application.Features.PaidEventPolicies.Requests.Queries;

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.View)]
public sealed record GetTenantPaidEventPolicyQuery(Guid TenantId) : IRequest<PaidEventPolicyDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : $"{TenantId}:paid-event-policy";

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new TenantSettingAuthorizationFacts(TenantId);
}

[AuthorizeResource(ResourceKinds.TenantSetting, AuthorizationActions.TenantSettings.View)]
public sealed record GetTenantPaidEventPolicyConfigurationQuery(Guid TenantId)
    : IRequest<TenantPaidEventPolicyConfigurationDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : $"{TenantId}:paid-event-policy";

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new TenantSettingAuthorizationFacts(TenantId);
}
