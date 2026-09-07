using System.Security.Claims;
using System.Diagnostics.CodeAnalysis;
using Event.Web.BffHosting.Authentication;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Explore.Blazor.Services;

public sealed class ExploreBffCookieSessionHandler(
    BffAdminClaimsTransformation adminClaimsTransformation,
    IBffOnboardingStatusProvider onboardingStatusProvider)
    : IEventBffCookieSessionHandler
{
    internal const string UserSynchronizationCompletedProperty =
        ".islamu.bff.user-synchronization-completed";

    public async Task OnSigningInAsync(CookieSigningInContext context)
    {
        if (context.Principal is null)
        {
            return;
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

    public Task OnTokenRefreshRejectedAsync(CookieValidatePrincipalContext context, string reason)
    {
        ClearCircuitTokenState(context);
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

    private static void ClearCircuitTokenState(CookieValidatePrincipalContext context)
    {
        var tokenService = context.HttpContext.RequestServices.GetService<ICircuitAccessTokenService>();
        tokenService?.ClearToken();

        context.HttpContext.RequestServices.GetService<ICircuitUserContext>()?.Clear();
        context.HttpContext.RequestServices.GetService<IBffAuthCookieStore>()?.Clear();

        var userId = ResolveUserId(context.Principal);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var tokenStore = context.HttpContext.RequestServices.GetService<ICircuitTokenStore>();
        var sessionId = context.Principal.TryGetSessionId(out var resolvedSessionId) ? resolvedSessionId.PartitionKey : null;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            tokenStore?.ClearUser(userId);
        }
        else
        {
            tokenStore?.ClearSession(userId, sessionId);
        }
    }

    private static string? ResolveUserId(ClaimsPrincipal? principal)
    {
        return principal.TryGetCircuitSubject(out var subject) ? subject.PartitionKey : null;
    }
}
