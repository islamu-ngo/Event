using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

/// <summary>
/// Detail policy for EmailDispatch status rows rendered outside a collection.
/// </summary>
public sealed class EmailDispatchStatusDetailLinkPolicy : ILinkPolicy<EmailDispatchStatusDto>
{
    private readonly EmailDispatchStatusCollectionLinkPolicy _collectionPolicy = new();

    public IEnumerable<LinkDefinition> GetLinks(EmailDispatchStatusDto dto, ClaimsPrincipal? user)
        => _collectionPolicy.GetItemLinks(dto, user);
}

/// <summary>
/// Collection-item policy for EmailDispatch operator status rows.
/// </summary>
public sealed class EmailDispatchStatusCollectionLinkPolicy : ICollectionLinkPolicy<EmailDispatchStatusDto>
{
    private static readonly string[] ReplayableStatuses =
    [
        "DeadLettered",
        "Parked",
        "RetryScheduled"
    ];

    public IEnumerable<LinkDefinition> GetItemLinks(EmailDispatchStatusDto dto, ClaimsPrincipal? user)
    {
        var routeValues = new { tenantId = dto.TenantId, outboxId = dto.OutboxId };

        if (dto.ContentRedactedAt is null && IsReplayable(dto.DeliveryStatus))
        {
            yield return new LinkDefinition(
                "replay",
                RouteNames.ReplayEmailDispatch,
                routeValues,
                "POST",
                "Replay email dispatch",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Replay,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }

        if (dto.ContentRedactedAt is null && CanPark(dto.DeliveryStatus))
        {
            yield return new LinkDefinition(
                "park",
                RouteNames.ParkEmailDispatch,
                routeValues,
                "PUT",
                "Park email dispatch",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Park,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }

        if (dto.ContentRedactedAt is null && CanResolve(dto.DeliveryStatus))
        {
            yield return new LinkDefinition(
                "resolve-without-replay",
                RouteNames.ResolveEmailDispatchWithoutReplay,
                routeValues,
                "POST",
                "Resolve email dispatch without replay",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Resolve,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }

        if (dto.ContentRedactedAt is null
            && string.Equals(dto.DeliveryStatus, "Unknown", StringComparison.Ordinal))
        {
            yield return new LinkDefinition(
                "reconcile",
                RouteNames.ReconcileUnknownEmailDispatch,
                routeValues,
                "POST",
                "Reconcile unknown email dispatch",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.EmailDispatches.Reconcile,
                    ResourceDescriptors.EmailDispatchStatus,
                    dto);
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];

    private static bool IsReplayable(string deliveryStatus)
        => ReplayableStatuses.Contains(deliveryStatus, StringComparer.Ordinal);

    private static bool CanPark(string deliveryStatus)
        => !string.Equals(deliveryStatus, "Sent", StringComparison.Ordinal) &&
           !string.Equals(deliveryStatus, "Skipped", StringComparison.Ordinal) &&
           !string.Equals(deliveryStatus, "Parked", StringComparison.Ordinal) &&
           !string.Equals(deliveryStatus, "Processing", StringComparison.Ordinal) &&
           !string.Equals(deliveryStatus, "Unknown", StringComparison.Ordinal);

    private static bool CanResolve(string deliveryStatus)
        => string.Equals(deliveryStatus, "DeadLettered", StringComparison.Ordinal)
           || string.Equals(deliveryStatus, "Parked", StringComparison.Ordinal)
           || string.Equals(deliveryStatus, "Unknown", StringComparison.Ordinal);
}
