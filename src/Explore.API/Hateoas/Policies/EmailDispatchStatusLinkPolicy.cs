using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Hateoas;
using Explore.Domain;

namespace Explore.API.Hateoas.Policies;

/// <summary>
/// Detail policy for EmailDispatch status rows rendered outside a collection.
/// </summary>
public sealed class EmailDispatchStatusDetailLinkPolicy : ILinkPolicy<EmailDispatchStatusDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EmailDispatchStatusDto dto, ClaimsPrincipal? user)
    {
        if (dto.ContentRedactedAt is not null)
        {
            yield break;
        }

        var routeValues = new { tenantId = dto.TenantId, outboxId = dto.OutboxId };

        if (dto.DeliveryStatus is EmailDispatchStatus.DeadLettered or EmailDispatchStatus.Parked or EmailDispatchStatus.RetryScheduled)
        {
            yield return new LinkDefinition(
                Rel: "replay",
                RouteName: RouteNames.ReplayEmailDispatch,
                RouteValues: routeValues,
                Method: "POST",
                Title: "Replay email dispatch",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Replay,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }

        if (EmailDispatchOutbox.CanParkForOperator(status: dto.DeliveryStatus, parkReason: dto.ParkReason))
        {
            yield return new LinkDefinition(
                Rel: "park",
                RouteName: RouteNames.ParkEmailDispatch,
                RouteValues: routeValues,
                Method: "PUT",
                Title: "Park email dispatch",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Park,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }

        if (dto.DeliveryStatus is EmailDispatchStatus.DeadLettered or EmailDispatchStatus.Parked or EmailDispatchStatus.Unknown)
        {
            yield return new LinkDefinition(
                Rel: "resolve-without-replay",
                RouteName: RouteNames.ResolveEmailDispatchWithoutReplay,
                RouteValues: routeValues,
                Method: "POST",
                Title: "Resolve email dispatch without replay",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Resolve,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }

        if (dto.DeliveryStatus is EmailDispatchStatus.Unknown)
        {
            yield return new LinkDefinition(
                Rel: "reconcile",
                RouteName: RouteNames.ReconcileUnknownEmailDispatch,
                RouteValues: routeValues,
                Method: "POST",
                Title: "Reconcile unknown email dispatch",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Reconcile,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }
    }
}

/// <summary>
/// Collection-item policy for EmailDispatch operator status rows.
/// </summary>
public sealed class EmailDispatchStatusCollectionLinkPolicy : ICollectionLinkPolicy<EmailDispatchStatusDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EmailDispatchStatusDto dto, ClaimsPrincipal? user)
    {
        if (dto.ContentRedactedAt is not null)
        {
            yield break;
        }

        var routeValues = new { tenantId = dto.TenantId, outboxId = dto.OutboxId };

        if (dto.DeliveryStatus is EmailDispatchStatus.DeadLettered or EmailDispatchStatus.Parked or EmailDispatchStatus.RetryScheduled)
        {
            yield return new LinkDefinition(
                Rel: "replay",
                RouteName: RouteNames.ReplayEmailDispatch,
                RouteValues: routeValues,
                Method: "POST",
                Title: "Replay email dispatch",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Replay,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }

        if (EmailDispatchOutbox.CanParkForOperator(status: dto.DeliveryStatus, parkReason: dto.ParkReason))
        {
            yield return new LinkDefinition(
                Rel: "park",
                RouteName: RouteNames.ParkEmailDispatch,
                RouteValues: routeValues,
                Method: "PUT",
                Title: "Park email dispatch",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Park,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }

        if (dto.DeliveryStatus is EmailDispatchStatus.DeadLettered or EmailDispatchStatus.Parked or EmailDispatchStatus.Unknown)
        {
            yield return new LinkDefinition(
                Rel: "resolve-without-replay",
                RouteName: RouteNames.ResolveEmailDispatchWithoutReplay,
                RouteValues: routeValues,
                Method: "POST",
                Title: "Resolve email dispatch without replay",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Resolve,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }

        if (dto.DeliveryStatus is EmailDispatchStatus.Unknown)
        {
            yield return new LinkDefinition(
                Rel: "reconcile",
                RouteName: RouteNames.ReconcileUnknownEmailDispatch,
                RouteValues: routeValues,
                Method: "POST",
                Title: "Reconcile unknown email dispatch",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Reconcile,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
