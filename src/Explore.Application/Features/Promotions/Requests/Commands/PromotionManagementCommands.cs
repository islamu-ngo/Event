using Explore.Application.Authorization;
using Explore.Application.Features.Promotions;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Promotions.Requests.Commands;

public abstract record PromotionManagementCommandBase(Guid EventId) : ISecureRequest
{
    public string? ResourceId => EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}

public abstract record PromotionManagementCommandBase<TResponse>(Guid EventId) : PromotionManagementCommandBase(EventId), IRequest<TResponse>
    where TResponse : BaseCommandResponse<Guid>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record CreatePromotionDraftCommand(
    Guid EventId,
    Guid TicketCatalogVersionId,
    string DisplayLabel,
    string Code,
    string DiscountKind,
    long? FixedDiscountMinor,
    int? BasisPointDiscount,
    long? MaximumDiscountMinor,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int? TotalRedemptionLimit,
    int? PerVerifiedPurchaserLimit,
    IReadOnlyCollection<Guid> EligibleTicketTypeIds) : PromotionManagementCommandBase<PromotionCodeIssuedCommandResponseDto>(EventId);

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record RevisePromotionCommand(
    Guid EventId,
    Guid PromotionDefinitionId,
    string DisplayLabel,
    string DiscountKind,
    long? FixedDiscountMinor,
    int? BasisPointDiscount,
    long? MaximumDiscountMinor,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int? TotalRedemptionLimit,
    int? PerVerifiedPurchaserLimit,
    IReadOnlyCollection<Guid> EligibleTicketTypeIds) : PromotionManagementCommandBase<PromotionManagementCommandResponseDto>(EventId);

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record PublishPromotionCommand(
    Guid EventId,
    Guid PromotionDefinitionId,
    string Code) : PromotionManagementCommandBase<PromotionManagementCommandResponseDto>(EventId);

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record RevokePromotionCommand(
    Guid EventId,
    Guid PromotionDefinitionId) : PromotionManagementCommandBase<PromotionManagementCommandResponseDto>(EventId);

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record RotatePromotionCodeCommand(
    Guid EventId,
    Guid PromotionDefinitionId,
    string Code) : PromotionManagementCommandBase<PromotionCodeIssuedCommandResponseDto>(EventId);
