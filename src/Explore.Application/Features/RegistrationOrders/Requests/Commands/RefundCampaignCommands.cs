using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;

namespace Explore.Application.Features.RegistrationOrders.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record ResumeRefundCampaignCommand(Guid EventId, Guid CampaignId)
    : ICommand<RefundCampaignDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
