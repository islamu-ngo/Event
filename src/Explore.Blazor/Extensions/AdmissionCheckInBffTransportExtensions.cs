using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Yarp.ReverseProxy.Forwarder;

namespace Explore.Blazor.Extensions;

public static class AdmissionCheckInBffTransportExtensions
{
    private const string ScannerCapabilityHeader = "X-Admission-Scanner-Capability";
    private const string ScannerCheckInPath = "/api/admission/scanner/check-ins";

    public static IApplicationBuilder UseAdmissionCheckInBffTransport(
        this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(InvokeAsync);
    }

    internal static bool IsAdmissionCheckInPath(PathString path) =>
        IsScannerCheckInPath(path) || IsStaffAuthorityPath(path);

    private static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        bool scannerRequest = IsScannerCheckInPath(context.Request.Path);
        bool staffRequest = !scannerRequest && IsStaffAuthorityPath(context.Request.Path);
        if (!scannerRequest && !staffRequest)
        {
            await next();
            return;
        }

        ClaimsPrincipal? originalUser = null;
        IAuthenticateResultFeature? originalAuthenticateResult = null;
        if (scannerRequest)
        {
            originalUser = context.User;
            originalAuthenticateResult = context.Features.Get<IAuthenticateResultFeature>();
            context.Features.Set<IAuthenticateResultFeature>(null);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            context.Request.Headers.Remove("Authorization");
            context.Request.Headers.Remove("Cookie");
        }
        else
        {
            context.Request.Headers.Remove(ScannerCapabilityHeader);
        }

        try
        {
            await next();
            await TranslateExplicitUpstreamFailureAsync(context);
        }
        finally
        {
            if (originalUser is not null)
            {
                context.User = originalUser;
                context.Features.Set<IAuthenticateResultFeature>(originalAuthenticateResult);
            }
        }
    }

    private static bool IsScannerCheckInPath(PathString path)
    {
        if (!TryGetExactSegments(path, out string[] segments) ||
            segments.Length < 3 ||
            !EqualsSegment(segments[0], "api") ||
            !EqualsSegment(segments[1], "admission") ||
            !EqualsSegment(segments[2], "scanner") ||
            segments.Length < 4 ||
            !EqualsSegment(segments[3], "check-ins"))
        {
            return false;
        }

        return segments.Length == 4 ||
            (segments.Length == 5 && EqualsSegment(segments[4], "batch")) ||
            (segments.Length == 6 && Guid.TryParse(segments[4], out _) &&
                EqualsSegment(segments[5], "undo"));
    }

    private static bool IsStaffAuthorityPath(PathString path)
    {
        if (!TryGetExactSegments(path, out string[] segments) ||
            segments.Length < 5 ||
            !EqualsSegment(segments[0], "api") ||
            !EqualsSegment(segments[1], "events") ||
            !Guid.TryParse(segments[2], out _) ||
            !EqualsSegment(segments[3], "admission"))
        {
            return false;
        }

        return IsStaffCheckInPath(segments) || IsScannerCapabilityManagementPath(segments);
    }

    private static bool IsStaffCheckInPath(string[] segments)
    {
        if (!EqualsSegment(segments[4], "check-ins"))
        {
            return false;
        }

        return segments.Length == 5 ||
            (segments.Length == 6 &&
                (EqualsSegment(segments[5], "batch") ||
                    EqualsSegment(segments[5], "summary") ||
                    EqualsSegment(segments[5], "audit") ||
                    EqualsSegment(segments[5], "health") ||
                    Guid.TryParse(segments[5], out _))) ||
            (segments.Length == 7 && Guid.TryParse(segments[5], out _) &&
                EqualsSegment(segments[6], "undo")) ||
            (segments.Length == 7 &&
                EqualsSegment(segments[5], "operations") &&
                (EqualsSegment(segments[6], "stop") ||
                    EqualsSegment(segments[6], "restore") ||
                    EqualsSegment(segments[6], "reconcile")));
    }

    private static bool IsScannerCapabilityManagementPath(string[] segments) =>
        EqualsSegment(segments[4], "scanner-capabilities") &&
        (segments.Length == 5 ||
            (segments.Length == 6 && Guid.TryParse(segments[5], out _)));

    private static bool TryGetExactSegments(PathString path, out string[] segments)
    {
        string? value = path.Value;
        if (string.IsNullOrEmpty(value) || value[0] != '/' || value[^1] == '/')
        {
            segments = [];
            return false;
        }

        string[] rawSegments = value.Split('/', StringSplitOptions.None);
        if (rawSegments[0].Length != 0 || rawSegments.Skip(1).Any(string.IsNullOrEmpty))
        {
            segments = [];
            return false;
        }

        segments = rawSegments[1..];
        return true;
    }

    private static bool EqualsSegment(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static async Task TranslateExplicitUpstreamFailureAsync(HttpContext context)
    {
        if (context.Response.HasStarted ||
            context.RequestAborted.IsCancellationRequested ||
            context.GetForwarderErrorFeature() is not { } failure ||
            !IsUpstreamUnavailable(failure.Error))
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Type = "about:blank",
                Title = "Admission check-in unavailable",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "Admission check-in is temporarily unavailable.",
                Extensions = { ["code"] = "admission_upstream_unavailable" }
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }

    private static bool IsUpstreamUnavailable(ForwarderError error) => error is
        ForwarderError.Request or
        ForwarderError.RequestTimedOut or
        ForwarderError.RequestBodyDestination or
        ForwarderError.ResponseHeaders or
        ForwarderError.ResponseBodyDestination or
        ForwarderError.NoAvailableDestinations;
}
