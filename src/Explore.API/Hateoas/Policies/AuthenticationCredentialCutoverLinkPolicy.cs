using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class AuthenticationCredentialCutoverLinkPolicy : ILinkPolicy<AuthenticationCredentialCutoverDto>
{
    public IEnumerable<LinkDefinition> GetLinks(AuthenticationCredentialCutoverDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(LinkRelations.Self, RouteNames.GetAuthenticationCredentialCutover,
                new { operationId = dto.OperationId }, "GET", "Retained cutover receipt", RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View, ResourceKinds.InstanceSetting,
                PrepareAuthenticationCredentialCutoverCommand.ResourceKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        if (dto.CurrentHeadStamp is not null && dto.State == "ReadyForCleanup")
        {
            yield return new LinkDefinition(LinkRelations.InspectAuthenticationCredentialCleanup,
                    RouteNames.InspectAuthenticationCredentialCleanup, new { operationId = dto.OperationId }, "GET",
                    "Inspect exact legacy row metadata before approval", RequiresAuth: true)
                .RequirePermission(AuthorizationActions.InstanceSettings.View, ResourceKinds.InstanceSetting,
                    PrepareAuthenticationCredentialCutoverCommand.ResourceKey,
                    facts: InstanceScopedAuthorizationFacts.Instance);
            yield return new LinkDefinition(LinkRelations.CleanupAuthenticationCredentials,
                    RouteNames.CleanupAuthenticationCredentials, null, "POST",
                    "Clean up the exact approved legacy row inventory", RequiresAuth: true)
                .RequirePermission(AuthorizationActions.InstanceSettings.Update, ResourceKinds.InstanceSetting,
                    PrepareAuthenticationCredentialCutoverCommand.ResourceKey,
                    facts: InstanceScopedAuthorizationFacts.Instance);
        }

        if (dto.CurrentHeadStamp is not null
            && dto.State is "Prepared" or "Collecting" or "ReadyForCleanup")
        {
            yield return new LinkDefinition(LinkRelations.ReportAuthenticationCredentialActivation,
                    RouteNames.ReportAuthenticationCredentialActivation, null, "POST",
                    "Report independently verified replacement acquisition", RequiresAuth: true)
                .RequirePermission(AuthorizationActions.InstanceSettings.Update, ResourceKinds.InstanceSetting,
                    PrepareAuthenticationCredentialCutoverCommand.ResourceKey,
                    facts: InstanceScopedAuthorizationFacts.Instance);
            if (!dto.FenceAttested)
                yield return new LinkDefinition(LinkRelations.AttestAuthenticationCredentialFence,
                        RouteNames.AttestAuthenticationCredentialFence, null, "POST",
                        "Attest old process, writer and session fences", RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.InstanceSettings.Update, ResourceKinds.InstanceSetting,
                        PrepareAuthenticationCredentialCutoverCommand.ResourceKey,
                        facts: InstanceScopedAuthorizationFacts.Instance);
        }
    }
}

public sealed class AuthenticationCredentialCutoverCollectionLinkPolicy
    : ICollectionLinkPolicy<AuthenticationCredentialCutoverDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(AuthenticationCredentialCutoverDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
