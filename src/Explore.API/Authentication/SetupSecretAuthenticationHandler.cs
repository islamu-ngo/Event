// ABOUTME: Authenticates setup-secret authority only for canonical provider settings and Local first-run routes.
// ABOUTME: Fails closed without placing setup secret material in principals, logs, responses, or exceptions.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Explore.API.ExceptionHandling;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

public sealed class SetupSecretAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISetupSecretProvider setupSecretProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string HeaderName = "X-Setup-Secret";

    private const string AuthProviderPath = "/api/instance/settings/auth-provider";
    private const string AuthorizationProviderPath = "/api/instance/settings/authz-provider";
    private bool _setupModeInactive;

    internal static bool SupportsRequest(HttpRequest request)
        => (HttpMethods.IsGet(request.Method) || HttpMethods.IsPatch(request.Method))
           && (string.Equals(request.Path.Value, AuthProviderPath, StringComparison.OrdinalIgnoreCase)
               || string.Equals(request.Path.Value, AuthorizationProviderPath, StringComparison.OrdinalIgnoreCase))
           || HttpMethods.IsGet(request.Method)
               && string.Equals(request.Path.Value, "/api/instanceonboarding/status", StringComparison.OrdinalIgnoreCase)
           || HttpMethods.IsPost(request.Method)
               && string.Equals(request.Path.Value, "/api/instanceonboarding/complete-local", StringComparison.OrdinalIgnoreCase);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!SupportsRequest(Request))
        {
            return AuthenticateResult.NoResult();
        }

        if (!Request.Headers.TryGetValue(HeaderName, out var values)
            || values.Count != 1)
        {
            return AuthenticateResult.Fail("Setup-secret authentication failed.");
        }

        SetupSecretValidationOutcome validation = await setupSecretProvider.ValidateSecretAsync(
            values[0],
            Context.RequestAborted);
        if (validation == SetupSecretValidationOutcome.SetupCompleted)
        {
            _setupModeInactive = true;
            return AuthenticateResult.Fail("Setup-secret authentication failed.");
        }

        if (validation != SetupSecretValidationOutcome.Accepted)
        {
            return AuthenticateResult.Fail("Setup-secret authentication failed.");
        }

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(authenticationType: ApiAuthenticationSchemeNames.SetupSecret));
        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (_setupModeInactive)
        {
            var problemDetails = ApiProblemFactory.CreateGoneProblem(
                Context,
                "Setup already completed",
                "Setup mode is no longer active for this instance.",
                ApiProblemCodes.SetupAlreadyCompleted);
            Response.StatusCode = StatusCodes.Status410Gone;
            var problemDetailsService = Context.RequestServices.GetRequiredService<IProblemDetailsService>();
            await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = Context,
                ProblemDetails = problemDetails
            });
            return;
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{ApiAuthenticationSchemeNames.SetupSecret} realm=\"setup\"";
    }
}
