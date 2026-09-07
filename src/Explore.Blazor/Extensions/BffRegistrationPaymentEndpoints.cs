using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services.Http;
using Explore.Blazor.Services;

namespace Explore.Blazor.Extensions;

public static class BffRegistrationPaymentEndpoints
{
    private const string CapabilityHeader = "X-Registration-Order-Capability";
    private const string CheckoutCookieName = "__Secure-islamu-registration-payment-checkout";
    private const string CheckoutSessionCookieName = "__Secure-islamu-registration-payment-session";
    private const string CheckoutPath = "/bff/registration-payments/checkout";

    public static WebApplication MapRegistrationPaymentEndpoints(this WebApplication app)
    {
        app.MapPost("/bff/registration-payments/events/{eventId:guid}/orders/{orderId:guid}/checkout-ticket", HandleCheckoutTicketAsync)
            .RequireRateLimiting(RateLimitingExtensions.RegistrationPaymentCheckoutIssuePolicy)
            .ValidateAntiforgeryBeforeRateLimiting();
        app.MapGet(CheckoutPath, HandleCheckoutNavigation);
        app.MapGet("/payments/checkout/success", NavigateToRecovery);
        app.MapGet("/payments/checkout/cancel", NavigateToRecovery);
        return app;
    }

    private static async Task<IResult> HandleCheckoutTicketAsync(
        Guid eventId,
        Guid orderId,
        HttpContext context,
        IAuthenticatedRegistrationOrderPaymentClient authenticatedClient,
        IGuestRegistrationOrderPaymentClient guestClient,
        RegistrationPaymentCheckoutTicketStore ticketStore,
        ITenantRouteContextAccessor tenantContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        SetNoStore(context.Response.Headers);
        if (context.Request.QueryString.HasValue)
        {
            return Results.BadRequest();
        }

        string? capability = context.Request.Headers[CapabilityHeader].FirstOrDefault();
        RegistrationPaymentCheckoutTargetDto target;
        try
        {
            target = string.IsNullOrWhiteSpace(capability)
                ? await authenticatedClient.GetAuthenticatedRegistrationPaymentCheckoutTargetAsync(eventId, orderId, cancellationToken: cancellationToken)
                : await guestClient.GetGuestRegistrationPaymentCheckoutTargetAsync(eventId, orderId, capability, cancellationToken: cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode is StatusCodes.Status401Unauthorized
            or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound)
        {
            return Results.StatusCode(exception.StatusCode);
        }

        string[] allowedHosts = configuration.GetSection("Payments:Stripe:AllowedCheckoutHosts").Get<string[]>() ?? ["checkout.stripe.com"];
        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out Uri? uri) || !IsApprovedCheckoutTarget(uri, allowedHosts))
        {
            return Results.NotFound();
        }

        string checkoutSession = context.Request.Cookies[CheckoutSessionCookieName]
            ?? WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        RegistrationPaymentCheckoutTicketIssue? issue = ticketStore.PrepareIssue(
            uri,
            eventId,
            orderId,
            context.Request,
            tenantContext.TenantSlug ?? string.Empty,
            checkoutSession);
        if (issue is null)
        {
            return Results.BadRequest();
        }

        context.RequestAborted.ThrowIfCancellationRequested();
        if (!context.Request.Cookies.ContainsKey(CheckoutSessionCookieName))
        {
            context.Response.Cookies.Append(
                CheckoutSessionCookieName,
                checkoutSession,
                CreateCheckoutSessionCookieOptions(context.Request));
        }
        context.Response.Cookies.Append(CheckoutCookieName, issue.ProtectedCookie, CreateCheckoutCookieOptions(context.Request));
        return Results.Ok(new BffRegistrationPaymentCheckoutTicketResponseDto(BuildCheckoutPath(context.Request)));
    }

    private static IResult HandleCheckoutNavigation(
        HttpContext context,
        RegistrationPaymentCheckoutTicketStore ticketStore,
        ITenantRouteContextAccessor tenantContext,
        IConfiguration configuration,
        ILogger<RegistrationPaymentCheckoutTicketStore> logger)
    {
        SetNoStore(context.Response.Headers);
        if (context.Request.QueryString.HasValue
            || !string.Equals(context.Request.Headers["Sec-Fetch-Site"], "same-origin", StringComparison.Ordinal))
        {
            return Results.BadRequest();
        }

        if (!context.Request.Cookies.TryGetValue(CheckoutCookieName, out string? cookie)
            || !context.Request.Cookies.TryGetValue(CheckoutSessionCookieName, out string? checkoutSession))
        {
            return Results.NotFound();
        }

        Uri? target = ticketStore.ValidateAndExtractTarget(
            cookie,
            context.Request,
            tenantContext.TenantSlug ?? string.Empty,
            checkoutSession);
        string[] allowedHosts = configuration.GetSection("Payments:Stripe:AllowedCheckoutHosts").Get<string[]>() ?? ["checkout.stripe.com"];
        if (target is null || !IsApprovedCheckoutTarget(target, allowedHosts))
        {
            logger.LogDebug("Registration payment checkout navigation rejected an invalid ticket or destination.");
            return Results.NotFound();
        }

        CookieOptions deletionOptions = CreateCheckoutCookieOptions(context.Request);
        deletionOptions.MaxAge = TimeSpan.Zero;
        deletionOptions.Expires = DateTimeOffset.UnixEpoch;
        context.Response.Cookies.Append(CheckoutCookieName, string.Empty, deletionOptions);
        context.Response.Headers.Location = target.AbsoluteUri;
        return Results.StatusCode(StatusCodes.Status303SeeOther);
    }

    private static IResult NavigateToRecovery(HttpContext context)
    {
        SetNoStore(context.Response.Headers);
        return Results.Redirect($"{context.Request.PathBase}/registration/payment-recovery", permanent: false, preserveMethod: false);
    }

    public static bool IsApprovedCheckoutTarget(Uri target, IEnumerable<string> allowedHosts)
    {
        if (!target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttps || !target.IsDefaultPort ||
            target.UserInfo.Length != 0 || target.Fragment.Length != 0)
        {
            return false;
        }

        var hosts = allowedHosts
            .Select(host => host.Trim().ToLowerInvariant())
            .Where(host => host.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        return hosts.Contains(target.IdnHost.ToLowerInvariant());
    }

    private static void SetNoStore(IHeaderDictionary headers)
    {
        headers.CacheControl = "private, no-store";
        headers.Pragma = "no-cache";
        headers.Expires = "0";
    }

    private static CookieOptions CreateCheckoutCookieOptions(HttpRequest request) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        MaxAge = TimeSpan.FromMinutes(5),
        Path = BuildCheckoutPath(request)
    };

    private static CookieOptions CreateCheckoutSessionCookieOptions(HttpRequest request) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        MaxAge = TimeSpan.FromMinutes(30),
        Path = $"{request.PathBase}/bff/registration-payments"
    };

    private static string BuildCheckoutPath(HttpRequest request) => $"{request.PathBase}{CheckoutPath}";
}
