using System.Security.Claims;
using System.Diagnostics.CodeAnalysis;
using Event.Web.BffHosting.Authentication;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Explore.Blazor.Services.Auth;

namespace Explore.Blazor.Services;

public sealed class ExploreBffCookieSessionHandler(
    BffAdminClaimsTransformation adminClaimsTransformation,
    IBffOnboardingStatusProvider onboardingStatusProvider)
    : IEventBffCookieSessionHandler
{
    internal const string UserSynchronizationCompletedProperty =
        ".islamu.bff.user-synchronization-completed";

    public async Task<bool> ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null || !await IsSessionActiveAsync(context.HttpContext, context.Principal, context.Properties))
        {
            return false;
        }

        return await adminClaimsTransformation.ValidateLocalSessionAsync(
                context.Principal,
                context.Properties,
                context.Properties.GetTokenValue("access_token"),
                context.HttpContext.RequestAborted);
    }

    public async Task OnSigningInAsync(CookieSigningInContext context)
    {
        if (context.Principal is null)
        {
            return;
        }

        if (context.Properties.Items.ContainsKey(BffSessionIdentity.HandleProperty)
            && !await IsSessionActiveAsync(context.HttpContext, context.Principal, context.Properties))
        {
            throw new BffSessionAuthorityUnavailableException();
        }

        if (context.Properties.Items.Remove(UserSynchronizationCompletedProperty))
        {
            return;
        }

        var initialStatus = await onboardingStatusProvider
            .GetStatusAsync(context.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        var configuredPending = initialStatus.Disposition ==
            BffOnboardingDisposition.ConfiguredAdministratorPending;
        if (configuredPending)
        {
            if (!context.Properties.Items.TryGetValue(
                    EventBffAuthenticationConstants.OidcSchemePropertyKey,
                    out var oidcScheme)
                || string.IsNullOrWhiteSpace(oidcScheme)
                || !initialStatus.AllowsProvider(oidcScheme))
            {
                AbortConfiguredSignIn();
            }

            ReplaceProviderClaim(context.Principal, oidcScheme);
        }

        var hasAdminAuthority = await adminClaimsTransformation.EnrichPrincipalAsync(
            context.Principal,
            context.Properties,
            synchronizeUser: true,
            cancellationToken: context.HttpContext.RequestAborted);

        if (configuredPending)
        {
            var completedStatus = await onboardingStatusProvider
                .GetStatusAsync(context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (!hasAdminAuthority
                || completedStatus.Disposition != BffOnboardingDisposition.Completed)
            {
                AbortConfiguredSignIn();
            }
        }
    }

    internal static void MarkUserSynchronizationCompleted(
        AuthenticationProperties properties) =>
        properties.Items[UserSynchronizationCompletedProperty] = bool.TrueString;

    private static void ReplaceProviderClaim(ClaimsPrincipal principal, string provider)
    {
        foreach (var identity in principal.Identities)
        {
            foreach (var claim in identity.FindAll("auth_provider").ToList())
            {
                identity.RemoveClaim(claim);
            }
        }

        principal.AddIdentity(new ClaimsIdentity([new Claim("auth_provider", provider)]));
    }

    [DoesNotReturn]
    private static void AbortConfiguredSignIn() =>
        throw new InvalidOperationException(
            "Configured administrator sign-in did not establish synchronized authority.");

    public async Task OnTokenRefreshSucceededAsync(
        CookieValidatePrincipalContext context,
        IReadOnlyList<AuthenticationToken> refreshedTokens)
    {
        if (context.Principal is not null)
        {
            await adminClaimsTransformation.EnrichPrincipalAsync(
                context.Principal,
                context.Properties,
                forceRefresh: true,
                cancellationToken: context.HttpContext.RequestAborted);
            context.ReplacePrincipal(context.Principal);
        }

        var newAccessToken = refreshedTokens.FirstOrDefault(token => token.Name == "access_token")?.Value;
        if (!string.IsNullOrEmpty(newAccessToken))
        {
            var tokenService = context.HttpContext.RequestServices.GetService<ICircuitAccessTokenService>();
            tokenService?.SetToken(newAccessToken);
        }
    }

    public Task OnTokenRefreshRejectedAsync(
        CookieValidatePrincipalContext context,
        EventBffSessionRejectionCategory category,
        string reason)
    {
        ClearCircuitTokenState(
            context: context,
            requireSession: category != EventBffSessionRejectionCategory.TokenRefresh);
        return Task.CompletedTask;
    }

    public async Task<bool> TryRedirectRejectedHtmlNavigationAsync(
        CookieValidatePrincipalContext context,
        string reason)
    {
        var currentPath = context.HttpContext.Request.Path;
        var onboardingStatus = await onboardingStatusProvider
            .GetStatusAsync(context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (onboardingStatus.Disposition != BffOnboardingDisposition.InteractivePending)
        {
            return false;
        }

        if (currentPath.StartsWithSegments("/setup", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        context.HttpContext.Response.Redirect($"/setup?session=expired&reason={reason}");
        return true;
    }

    private static void ClearCircuitTokenState(CookieValidatePrincipalContext context, bool requireSession)
    {
        var hasSession = context.Principal is not null && BffSessionIdentity.TryGetBinding(context.Principal, out _);
        var tokenService = context.HttpContext.RequestServices.GetService<ICircuitAccessTokenService>();
        if (!requireSession || hasSession)
        {
            tokenService?.ClearToken();
        }

        context.HttpContext.RequestServices.GetService<ICircuitUserContext>()?.Clear();
        context.HttpContext.RequestServices.GetService<IBffAuthCookieStore>()?.Clear();

        if (context.Principal is null || !BffSessionIdentity.TryGetBinding(context.Principal, out var binding))
        {
            return;
        }

        var tokenStore = context.HttpContext.RequestServices.GetService<ICircuitTokenStore>();
        tokenStore?.ClearSession(binding.OwnerPartitionKey, binding.Handle);
    }

    private static async Task<bool> IsSessionActiveAsync(
        HttpContext httpContext, ClaimsPrincipal principal, AuthenticationProperties properties)
    {
        if (!BffSessionIdentity.TryGetBinding(principal, out var binding)
            || !properties.Items.TryGetValue(BffSessionIdentity.HandleProperty, out var handle)
            || !string.Equals(handle, binding.Handle, StringComparison.Ordinal)
            || !properties.Items.TryGetValue(BffSessionIdentity.RevisionProperty, out var revision)
            || string.IsNullOrWhiteSpace(revision))
        {
            return false;
        }

        var authority = httpContext.RequestServices.GetService<IBffSessionAuthority>()
            ?? throw new BffSessionAuthorityUnavailableException();
        return await authority.IsActiveAsync(binding, httpContext.RequestAborted);
    }
}
