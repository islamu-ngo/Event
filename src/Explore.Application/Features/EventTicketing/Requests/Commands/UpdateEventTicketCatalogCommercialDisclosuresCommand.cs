using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventTicketing.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record UpdateEventTicketCatalogCommercialDisclosuresCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public string? MerchantDisclosureText { get; init; }
    public string? RefundPolicyDisclosureText { get; init; }
    public string? SupportContactDisclosureText { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
