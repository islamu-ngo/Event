using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Hateoas;
using Explore.Application.Services.Registration;

namespace Explore.API.Hateoas.Policies;

internal static class RegistrationOrderPurchaseAuthorityLink
{
    public static LinkDefinition? TryCreate(
        RegistrationOrderDto dto,
        DateTime utcNow)
    {
        if (!RegistrationPaymentPayability.IsCurrentlyPayable(
            dto.StatusId,
            dto.TotalDueMinor,
            dto.ExpiresAt,
            utcNow))
        {
            return null;
        }

        return new LinkDefinition(
                LinkRelations.ReservePurchaseAuthority,
                RouteNames.ReserveAuthenticatedPurchaseAuthority,
                new { eventId = dto.EventId, orderId = dto.Id },
                HttpMethods.Post,
                "Reserve purchase authority",
                RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.RegistrationOrders.Continue,
                resourceKind: ResourceKinds.RegistrationOrder,
                resourceId: dto.Id.ToString("D"),
                facts: new RegistrationOrderAuthorizationFacts(
                    dto.TenantId,
                    dto.EventId,
                    dto.AccountUserId));
    }
}
