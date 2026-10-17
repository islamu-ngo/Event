using Explore.API.Middleware;
using Explore.Application.Exceptions;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Explore.API.ExceptionHandling;

internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Maps HTTP status codes to stable IANA RFC 9110 type URIs for ProblemDetails.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> ProblemTypeUris =
        new Dictionary<int, string>
        {
            [StatusCodes.Status400BadRequest] = ApiProblemTypes.BadRequest,
            [StatusCodes.Status401Unauthorized] = ApiProblemTypes.Unauthorized,
            [StatusCodes.Status403Forbidden] = ApiProblemTypes.Forbidden,
            [StatusCodes.Status404NotFound] = ApiProblemTypes.NotFound,
            [StatusCodes.Status409Conflict] = ApiProblemTypes.Conflict,
            [StatusCodes.Status413PayloadTooLarge] = ApiProblemTypes.PayloadTooLarge,
            [StatusCodes.Status422UnprocessableEntity] = ApiProblemTypes.UnprocessableEntity,
            [StatusCodes.Status429TooManyRequests] = ApiProblemTypes.TooManyRequests,
            [StatusCodes.Status500InternalServerError] = ApiProblemTypes.InternalServerError,
            [StatusCodes.Status503ServiceUnavailable] = ApiProblemTypes.ServiceUnavailable,
        };

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail, code) = exception switch
        {
            BadRequestException => (
                StatusCodes.Status400BadRequest,
                "Bad request",
                "The request is invalid.",
                ApiProblemCodes.ValidationFailed),
            NotFoundException => (
                StatusCodes.Status404NotFound,
                "Resource not found",
                "The requested resource was not found.",
                ApiProblemCodes.ResourceNotFound),
            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Authentication is required to access this resource.",
                ApiProblemCodes.AuthenticationRequired),
            AuthorizationProviderUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                "Authorization provider unavailable",
                "Authorization could not be evaluated. Try again later.",
                ApiProblemCodes.AuthorizationProviderUnavailable),
            AuthorizationException => (
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "You do not have permission to perform this action.",
                ApiProblemCodes.Forbidden),
            ConfigurationManifestExportTooLargeException tooLargeException => (
                StatusCodes.Status413PayloadTooLarge,
                "Configuration manifest export too large",
                "The configuration manifest export exceeds the maximum supported size.",
                tooLargeException.FailureCode),
            ConcurrencyConflictException concurrencyConflictException => (
                StatusCodes.Status409Conflict,
                "Concurrency conflict",
                "The resource changed. Refresh before retrying.",
                concurrencyConflictException.Code == ConcurrencyConflictException.StaleSyncBase
                    ? ConcurrencyConflictException.StaleSyncBase : ConcurrencyConflictException.ConcurrentUpdate),
            KeycloakOperationConflictException => (
                StatusCodes.Status409Conflict,
                "Keycloak operation conflict",
                "The provider operation conflicts with current state.",
                ApiProblemCodes.ResourceConflict),
            QuotaExceededException => (
                QuotaProblemDetailsFactory.StatusCode,
                QuotaProblemDetailsFactory.Title,
                "The request exceeds the supported quota.",
                "quota_exceeded"),
            AdmissionRecoveryRateLimitExceededException => (
                StatusCodes.Status429TooManyRequests,
                "Too many recovery requests",
                "The admission recovery request budget has been exhausted.",
                ApiProblemCodes.RateLimited),
            AdmissionRecoveryUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                "Admission recovery unavailable",
                "Admission recovery is temporarily unavailable.",
                ApiProblemCodes.UnexpectedError),
            AdmissionCheckInUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                "Admission check-in unavailable",
                "Admission check-in is temporarily unavailable. Stop queued scans and try again later.",
                ApiProblemCodes.AdmissionCheckInUnavailable),
            EventDiscoveryUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                "Event discovery unavailable",
                "Event discovery is temporarily unavailable.",
                ApiProblemCodes.EventDiscoveryUnavailable),
            EventDiscoveryCursorInvalidException => (
                StatusCodes.Status400BadRequest,
                "Discovery continuation invalid",
                "The discovery continuation is invalid for this search.",
                ApiProblemCodes.EventDiscoveryCursorInvalid),
            EventDiscoveryCursorExpiredException => (
                StatusCodes.Status410Gone,
                "Discovery continuation expired",
                "The discovery continuation has expired. Start a new traversal.",
                ApiProblemCodes.EventDiscoveryCursorExpired),
            EventDiscoveryRestartRequiredException => (
                StatusCodes.Status409Conflict,
                "Discovery restart required",
                "Discovery results changed. Start a new traversal.",
                ApiProblemCodes.EventDiscoveryRestartRequired),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                "An unexpected error occurred.",
                ApiProblemCodes.UnexpectedError)
        };

        string requestIdentity = SafeRouteMetadata.GetRouteIdentityOrClassification(httpContext);
        logger.Log(
            statusCode >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
            "API failure {Code} for {Method} {Route} responded {StatusCode} | TraceId={TraceId} CorrelationId={CorrelationId}",
            code, SafeRouteMetadata.GetMethod(httpContext), requestIdentity, statusCode,
            httpContext.TraceIdentifier, httpContext.Items["CorrelationId"] as string);

        httpContext.Response.StatusCode = statusCode;
        if (exception is AdmissionCheckInUnavailableException or EventDiscoveryUnavailableException
            or EventDiscoveryCursorInvalidException or EventDiscoveryCursorExpiredException
            or EventDiscoveryRestartRequiredException)
        {
            httpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";
            httpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        }
        if (exception is AdmissionRecoveryRateLimitExceededException recoveryRateLimit)
        {
            httpContext.Response.Headers.RetryAfter =
                recoveryRateLimit.RetryAfterSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        var typeUri = exception is QuotaExceededException
            ? QuotaProblemDetailsFactory.Type
            : exception is ConcurrencyConflictException concurrencyConflictExceptionForType
            ? concurrencyConflictExceptionForType.Code switch
            {
                ConcurrencyConflictException.StaleSyncBase => "/problems/stale_sync_base",
                ConcurrencyConflictException.ConcurrentUpdate => "/problems/concurrent_update",
                _ => ProblemTypeUris.TryGetValue(statusCode, out var concurrencyUri)
                    ? concurrencyUri
                    : $"https://tools.ietf.org/html/rfc9110#status.{statusCode}"
            }
            : ProblemTypeUris.TryGetValue(statusCode, out var uri)
                ? uri
                : $"https://tools.ietf.org/html/rfc9110#status.{statusCode}";

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = typeUri,
            Instance = requestIdentity
        };

        if (exception is not ConcurrencyConflictException and not QuotaExceededException)
        {
            problemDetails.Extensions["code"] = code;
        }

        if (exception is ConcurrencyConflictException)
        {
            problemDetails.Extensions["code"] = code;
        }

        if (exception is QuotaExceededException quotaExceeded)
        {
            QuotaProblemDetailsFactory.AddExtensions(problemDetails, quotaExceeded.Details with
            {
                QuotaKey = "quota",
                Scope = "request"
            });
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        });
    }
}
