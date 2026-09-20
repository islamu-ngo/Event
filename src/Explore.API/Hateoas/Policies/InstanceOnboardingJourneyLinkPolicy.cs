using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class InstanceOnboardingJourneyLinkPolicy(
    ILinkPolicy<InstanceOnboardingStatusDto> statusPolicy,
    ILinkPolicy<InstanceOperatorIdentityDocumentDto> identityPolicy,
    ISetupSecretProvider setupSecretProvider,
    IHttpContextAccessor context) : ILinkPolicy<InstanceOnboardingJourneyDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceOnboardingJourneyDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetInstanceOnboardingJourney);
        yield return new LinkDefinition("refresh", RouteNames.GetInstanceOnboardingJourney);
        if (dto.State != "Available" || dto.Bootstrap is null) yield break;

        foreach (var link in statusPolicy.GetLinks(dto.Bootstrap, user))
        {
            if (link.Rel is LinkRelations.Self or "save-profile") continue;
            if (link.Rel is "complete" or "complete-local" && dto.Preflight?.IsReadyToLaunch != true) continue;
            yield return link;
        }
        if (dto.OperatorIdentity is not null)
        {
            foreach (var link in identityPolicy.GetLinks(dto.OperatorIdentity, user))
                if (link.Rel == "update") yield return link with { Rel = "update-operator-identity" };
        }
        if (!dto.Bootstrap.IsCompleted && setupSecretProvider.IsSetupModeActive
            && setupSecretProvider.ValidateSecret(context.HttpContext?.Request.Headers["X-Setup-Secret"].FirstOrDefault()))
            yield return new LinkDefinition("save-profile", RouteNames.SaveInstanceOnboardingProfile,
                Method: HttpMethods.Patch);
    }
}

public sealed class InstanceOnboardingJourneyCollectionLinkPolicy : ICollectionLinkPolicy<InstanceOnboardingJourneyDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceOnboardingJourneyDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
