// ABOUTME: Forwards Local lifecycle requests through generated clients with native browser antiforgery.
// ABOUTME: Discards downstream bodies, forbids mutation retries, and clears ordinary sessions after consumption.

using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Net.Http.Headers;

namespace Explore.Blazor.Extensions;

public static class BffLocalIdentityLifecycleEndpoints
{
    public const string Prefix = "/bff/auth/local";
    public const string LandingPath = "/auth/local-account-recovery";

    public static WebApplication MapLocalIdentityLifecycleEndpoints(this WebApplication app)
    {
        Map(app, "/email-verifications", async (HttpContext context, LocalEmailVerificationRequestDto body,
            IHttpClientFactory factory, CancellationToken ct) => await ForwardAsync(context, factory,
                client => new LocalEmailVerificationClient(client).RequestLocalEmailVerificationAsync(body, cancellationToken: ct),
                consume: false, forwardSession: true));
        Map(app, "/email-verifications/consume", async (HttpContext context, LocalEmailConfirmationRequestDto body,
            IHttpClientFactory factory, CancellationToken ct) => await ForwardAsync(context, factory,
                client => new LocalEmailVerificationClient(client).ConfirmLocalEmailAsync(body, cancellationToken: ct),
                consume: true, forwardSession: false));
        Map(app, "/password-recoveries", async (HttpContext context, LocalPasswordRecoveryRequestDto body,
            IHttpClientFactory factory, CancellationToken ct) => await ForwardAsync(context, factory,
                client => new LocalPasswordRecoveryClient(client).RequestLocalPasswordRecoveryAsync(body, cancellationToken: ct),
                consume: false, forwardSession: false));
        Map(app, "/password-recoveries/consume", async (HttpContext context, LocalPasswordRecoveryCompletionRequestDto body,
            IHttpClientFactory factory, CancellationToken ct) => await ForwardAsync(context, factory,
                client => new LocalPasswordRecoveryClient(client).CompleteLocalPasswordRecoveryAsync(body, cancellationToken: ct),
                consume: true, forwardSession: false));
        Map(app, "/password", async (HttpContext context, LocalPasswordChangeRequestDto body,
            IHttpClientFactory factory, CancellationToken ct) => await ForwardAsync(context, factory,
                client => new LocalPasswordClient(client).ChangeLocalPasswordAsync(body, cancellationToken: ct),
                consume: true, forwardSession: true)).RequireAuthorization();
        return app;
    }

    private static RouteHandlerBuilder Map(WebApplication app, string path, Delegate handler) =>
        app.MapPost(Prefix + path, handler)
            .ValidateAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.LocalAuthenticationPolicy)
            .ExcludeFromDescription();

    private static async Task<IResult> ForwardAsync(HttpContext context, IHttpClientFactory factory,
        Func<HttpClient, Task> send, bool consume, bool forwardSession)
    {
        try
        {
            // This named transport has neither bearer forwarding nor resilience retries.
            using var client = factory.CreateClient(BffLocalCredentialEndpoints.HttpClientName);
            if (forwardSession && context.User.Identity?.IsAuthenticated == true)
            {
                var token = await context.GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme, "access_token");
                if (string.IsNullOrEmpty(token)) return Results.StatusCode(StatusCodes.Status401Unauthorized);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            await send(client);
            if (!consume) return Results.StatusCode(StatusCodes.Status202Accepted);
            await BffLocalCredentialEndpoints.ClearLocalSessionAsync(context);
            return Results.NoContent();
        }
        catch (ApiException exception)
        {
            return Results.StatusCode(exception.StatusCode switch
            {
                400 or 401 or 403 or 409 or 429 or 503 => exception.StatusCode,
                _ => StatusCodes.Status503ServiceUnavailable
            });
        }
        catch (Exception exception) when (exception is HttpRequestException
            || exception is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
