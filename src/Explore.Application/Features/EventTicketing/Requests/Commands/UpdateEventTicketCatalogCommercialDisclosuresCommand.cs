using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventTicketing.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record UpdateEventTicketCatalogCommercialDisclosuresCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public string? MerchantDisclosureText { get; init; }
    public string? RefundPolicyDisclosureText { get; init; }
    public string? SupportContactDisclosureText { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
