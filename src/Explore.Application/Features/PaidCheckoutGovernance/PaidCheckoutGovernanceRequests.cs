using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Payments;
using Explore.Application.Responses;

namespace Explore.Application.Features.PaidCheckoutGovernance.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetPaidCheckoutSaleControlQuery(Guid TenantId, Guid? EventId)
    : IQuery<PaidCheckoutSaleControlDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => "paid-checkout-governance";
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record StopPaidCheckoutSalesCommand(Guid TenantId, Guid? EventId, string ReasonCode)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => "paid-checkout-governance";
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record RequestPaidCheckoutResumeCommand(Guid TenantId, Guid? EventId, string ReasonCode)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => "paid-checkout-governance";
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record ReviewPaidCheckoutResumeCommand(Guid TenantId, Guid? EventId, bool Approved, string ReasonCode)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => "paid-checkout-governance";
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record RequestPaidCheckoutReviewCommand(
    Guid TenantId,
    Guid EventId,
    int TriggerId,
    string CurrencyCode,
    long? MaximumOrderAmountMinor,
    string ReasonCode) : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => "paid-checkout-governance";
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record DecidePaidCheckoutReviewCommand(
    Guid TenantId,
    Guid ReviewId,
    bool Approved,
    string ReasonCode) : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => "paid-checkout-governance";
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}
