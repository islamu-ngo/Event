// ABOUTME: HAL policies for instance onboarding status and its setup/admin affordances.
// ABOUTME: Emits provider, completion, and tenant-management links only for trusted server authority.

namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;

public sealed class InstanceOnboardingStatusLinkPolicy(
    ISetupSecretProvider setupSecretProvider,
    IHttpContextAccessor httpContextAccessor) : ILinkPolicy<InstanceOnboardingStatusDto>
{
    private const string SetupSecretHeader = "X-Setup-Secret";
    private const string AuthenticationSettingKey = "auth-provider";
    private const string AuthorizationSettingKey = "authorization-provider";

    public IEnumerable<LinkDefinition> GetLinks(InstanceOnboardingStatusDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetInstanceOnboardingStatus);

        if (!dto.IsCompleted && HasActiveSetupAuthority())
        {
            if (dto.State == "InteractivePending" && dto.Provider == "Local"
                && user?.Identities.Any(identity => identity.IsAuthenticated
                    && identity.AuthenticationType == ApiAuthenticationSchemeNames.SetupSecret) == true)
            {
                yield return new LinkDefinition(
                    "complete-local", RouteNames.CompleteLocalInstanceOnboarding,
                    Method: HttpMethods.Post, Title: "Complete Local instance onboarding", RequiresAuth: true);
            }

            yield return new LinkDefinition(
                "manage-authentication",
                RouteNames.GetInstanceOnboardingAuthProviderConfigurationInternal,
                Method: HttpMethods.Get,
                Title: "Manage authentication provider during setup");

            yield return new LinkDefinition(
                "manage-authorization",
                RouteNames.GetInstanceOnboardingAuthorizationProviderConfigurationInternal,
                Method: HttpMethods.Get,
                Title: "Manage authorization provider during setup");

            if (dto.IsAuthenticated && user?.Identities.Any(identity => identity.IsAuthenticated
                && identity.AuthenticationType != ApiAuthenticationSchemeNames.SetupSecret) == true)
            {
                yield return new LinkDefinition(
                    "save-profile",
                    RouteNames.SaveInstanceOnboardingProfile,
                    Method: HttpMethods.Patch,
                    Title: "Save instance profile during setup",
                    RequiresAuth: true);

                yield return new LinkDefinition(
                    "complete",
                    RouteNames.CompleteInstanceOnboarding,
                    Method: HttpMethods.Post,
                    Title: "Complete instance onboarding",
                    RequiresAuth: true);
            }

            yield break;
        }

        if (!dto.IsCompleted || !dto.IsCurrentUserInstanceAdmin)
        {
            yield break;
        }

        yield return InstanceSettingLink(
            "manage-authentication",
            RouteNames.GetInstanceAuthProviderConfiguration,
            AuthenticationSettingKey,
            "Manage authentication provider");

        yield return InstanceSettingLink(
            "manage-authorization",
            RouteNames.GetInstanceAuthorizationProviderConfiguration,
            AuthorizationSettingKey,
            "Manage authorization provider");

        if (string.Equals(dto.SelectedDeploymentMode, "MultiTenant", StringComparison.OrdinalIgnoreCase))
        {
            yield return InstanceSettingLink(
                "manage-tenants",
                RouteNames.GetControlPlaneTenants,
                GetControlPlaneTenantListQuery.SettingKey,
                "Manage tenants");
        }
    }

    private bool HasActiveSetupAuthority()
    {
        string? secret = httpContextAccessor.HttpContext?.Request.Headers[SetupSecretHeader].FirstOrDefault();
        return setupSecretProvider.IsSetupModeActive && setupSecretProvider.ValidateSecret(secret);
    }

    private static LinkDefinition InstanceSettingLink(
        string rel,
        string routeName,
        string settingKey,
        string title) =>
        new LinkDefinition(rel, routeName, Method: HttpMethods.Get, Title: title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                settingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}

public sealed class InstanceOnboardingStatusCollectionLinkPolicy
    : ICollectionLinkPolicy<InstanceOnboardingStatusDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceOnboardingStatusDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
