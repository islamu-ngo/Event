using System.Security.Claims;
using System.Text.Encodings.Web;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

public static class AdmissionScannerAuthenticationDefaults
{
    public const string HeaderName = "X-Admission-Scanner-Capability";
    public const string CapabilityIdClaim = "admission_scanner_capability_id";
    public const string TenantIdClaim = "admission_scanner_tenant_id";
    public const string EventIdClaim = "admission_scanner_event_id";
    public const string TargetIdClaim = "admission_scanner_target_id";
    public const string ActionClaim = "admission_scanner_action";
}

public sealed class AdmissionScannerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider services)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const int MaximumCapabilityLength = 512;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AdmissionScannerAuthenticationDefaults.HeaderName, out var values)
            || values.Count != 1)
        {
            return AuthenticateResult.NoResult();
        }

        string capability = values[0] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(capability) || capability.Length > MaximumCapabilityLength)
        {
            return AuthenticateResult.Fail("Invalid admission scanner authority.");
        }

        IAdmissionScannerAuthenticationService? authentication =
            services.GetService<IAdmissionScannerAuthenticationService>();
        if (authentication is null)
        {
            return AuthenticateResult.Fail("Invalid admission scanner authority.");
        }

        AdmissionScannerAuthenticationResult result;
        try
        {
            result = await authentication.AuthenticateAsync(
                new AdmissionScannerAuthenticationRequest(capability),
                Context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return AuthenticateResult.Fail("Invalid admission scanner authority.");
        }

        if (!IsValid(result))
        {
            return AuthenticateResult.Fail("Invalid admission scanner authority.");
        }

        Claim[] claims =
        [
            new(AdmissionScannerAuthenticationDefaults.CapabilityIdClaim, result.ScannerCapabilityId.ToString("D")),
            new(AdmissionScannerAuthenticationDefaults.TenantIdClaim, result.TenantId.ToString("D")),
            new(AdmissionScannerAuthenticationDefaults.EventIdClaim, result.EventId.ToString("D")),
            new(AdmissionScannerAuthenticationDefaults.TargetIdClaim, result.TargetId.ToString("D")),
            .. result.Actions.Select(action => new Claim(
                AdmissionScannerAuthenticationDefaults.ActionClaim,
                action.ToString()))
        ];
        var identity = new ClaimsIdentity(claims, ApiAuthenticationSchemeNames.AdmissionScanner);
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            ApiAuthenticationSchemeNames.AdmissionScanner));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        Response.ContentType = "application/problem+json";
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        await Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Admission operation not found",
                Detail = "The requested admission operation was not found."
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: Context.RequestAborted);
    }

    private static bool IsValid(AdmissionScannerAuthenticationResult result) =>
        result.Authenticated
        && result.ScannerCapabilityId != Guid.Empty
        && result.TenantId != Guid.Empty
        && result.EventId != Guid.Empty
        && result.TargetId != Guid.Empty
        && result.Actions is { Count: > 0 }
        && result.Actions.All(Enum.IsDefined);
}

internal static class AdmissionScannerPrincipalExtensions
{
    internal static bool TryGetAdmissionScannerScope(
        this ClaimsPrincipal principal,
        AdmissionCheckInAction requiredAction,
        out AdmissionScannerRequestScope scope)
    {
        scope = default;
        if (principal.Identities.Count(identity => identity.IsAuthenticated) != 1
            || principal.Identity?.AuthenticationType != ApiAuthenticationSchemeNames.AdmissionScanner
            || !Guid.TryParse(principal.FindFirstValue(AdmissionScannerAuthenticationDefaults.CapabilityIdClaim), out Guid capabilityId)
            || !Guid.TryParse(principal.FindFirstValue(AdmissionScannerAuthenticationDefaults.TenantIdClaim), out Guid tenantId)
            || !Guid.TryParse(principal.FindFirstValue(AdmissionScannerAuthenticationDefaults.EventIdClaim), out Guid eventId)
            || !Guid.TryParse(principal.FindFirstValue(AdmissionScannerAuthenticationDefaults.TargetIdClaim), out Guid targetId)
            || !principal.FindAll(AdmissionScannerAuthenticationDefaults.ActionClaim)
                .Any(claim => string.Equals(claim.Value, requiredAction.ToString(), StringComparison.Ordinal)))
        {
            return false;
        }

        scope = new AdmissionScannerRequestScope(capabilityId, tenantId, eventId, targetId);
        return true;
    }
}

internal readonly record struct AdmissionScannerRequestScope(
    Guid CapabilityId,
    Guid TenantId,
    Guid EventId,
    Guid TargetId);
