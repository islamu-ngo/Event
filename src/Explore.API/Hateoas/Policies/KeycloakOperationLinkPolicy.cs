using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class KeycloakConnectionLinkPolicy
    : ILinkPolicy<KeycloakConnectionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        KeycloakConnectionDto dto,
        ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(
            RouteNames.GetInstanceKeycloakConnection);
        if (!dto.Available)
        {
            yield break;
        }

        yield return new LinkDefinition(
            "inspect",
            RouteNames.InspectInstanceKeycloak,
            Method: HttpMethods.Post);
        yield return new LinkDefinition(
            "plan",
            RouteNames.PlanInstanceKeycloak,
            Method: HttpMethods.Post);
    }
}

public sealed class KeycloakConnectionCollectionLinkPolicy
    : ICollectionLinkPolicy<KeycloakConnectionDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        KeycloakConnectionDto dto,
        ClaimsPrincipal? user) =>
        [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(
        ClaimsPrincipal? user) =>
        [];
}

public sealed class KeycloakInspectionLinkPolicy
    : ILinkPolicy<KeycloakInspectionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        KeycloakInspectionDto dto,
        ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(
            RouteNames.InspectInstanceKeycloak);
        yield return new LinkDefinition(
            "connection",
            RouteNames.GetInstanceKeycloakConnection);
        if (dto.Findings.Count > 0)
        {
            yield return new LinkDefinition(
                "plan",
                RouteNames.PlanInstanceKeycloak,
                Method: HttpMethods.Post);
        }
    }
}

public sealed class KeycloakInspectionCollectionLinkPolicy
    : ICollectionLinkPolicy<KeycloakInspectionDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        KeycloakInspectionDto dto,
        ClaimsPrincipal? user) =>
        [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(
        ClaimsPrincipal? user) =>
        [];
}

public sealed class KeycloakOperationLinkPolicy(TimeProvider timeProvider)
    : ILinkPolicy<KeycloakOperationDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        KeycloakOperationDto dto,
        ClaimsPrincipal? user)
    {
        if (dto.Id == Guid.Empty)
        {
            yield return new LinkDefinition(
                "connection",
                RouteNames.GetInstanceKeycloakConnection);
            yield break;
        }

        var routeValues = new { operationId = dto.Id };
        yield return LinkDefinition.Self(
            RouteNames.GetInstanceKeycloakOperation,
            routeValues);
        if (string.Equals(
                dto.State,
                "Previewed",
                StringComparison.Ordinal)
            && dto.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            yield break;
        }

        if (string.Equals(
                dto.State,
                "Previewed",
                StringComparison.Ordinal))
        {
            yield return new LinkDefinition(
                "apply",
                RouteNames.ApplyInstanceKeycloakOperation,
                routeValues,
                HttpMethods.Post);
            yield return new LinkDefinition(
                "cancel",
                RouteNames.CancelInstanceKeycloakOperation,
                routeValues,
                HttpMethods.Post);
            yield break;
        }

        if (string.Equals(
                dto.State,
                "Applying",
                StringComparison.Ordinal))
        {
            yield return new LinkDefinition(
                "cancel",
                RouteNames.CancelInstanceKeycloakOperation,
                routeValues,
                HttpMethods.Post);
        }

        if (dto.State is "Applying" or "OutcomeUnknown")
        {
            yield return new LinkDefinition(
                "reconcile",
                RouteNames.ReconcileInstanceKeycloakOperation,
                routeValues,
                HttpMethods.Post);
        }
    }
}

public sealed class KeycloakOperationCollectionLinkPolicy
    : ICollectionLinkPolicy<KeycloakOperationDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        KeycloakOperationDto dto,
        ClaimsPrincipal? user) =>
        [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(
        ClaimsPrincipal? user) =>
        [];
}
