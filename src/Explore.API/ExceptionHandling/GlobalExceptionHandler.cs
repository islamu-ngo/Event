using Explore.API.Middleware;
using Explore.Application.Exceptions;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Explore.API.ExceptionHandling;

internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment hostEnvironment) : IExceptionHandler
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
            BadRequestException badRequestException => (
                StatusCodes.Status400BadRequest,
                "Bad request",
                badRequestException.Message,
                ApiProblemCodes.ValidationFailed),
            NotFoundException notFoundException => (
                StatusCodes.Status404NotFound,
                "Resource not found",
                notFoundException.Message,
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
                tooLargeException.Message,
                tooLargeException.FailureCode),
            ConcurrencyConflictException concurrencyConflictException => (
                StatusCodes.Status409Conflict,
                "Concurrency conflict",
                concurrencyConflictException.Message,
                ApiProblemCodes.ConcurrencyConflict),
            QuotaExceededException quotaExceededException => (
                QuotaProblemDetailsFactory.StatusCode,
                QuotaProblemDetailsFactory.Title,
                quotaExceededException.Message,
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
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                hostEnvironment.IsDevelopment() ? exception.Message : "An unexpected error occurred.",
                ApiProblemCodes.UnexpectedError)
        };

        bool isAdmissionRequest = RequestLoggingMiddleware.TryGetAdmissionRouteIdentity(
            httpContext,
            out string admissionRouteIdentity);
        string requestIdentity = isAdmissionRequest
            ? admissionRouteIdentity
            : httpContext.Request.Path;
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Route}", httpContext.Request.Method, requestIdentity);
        }
        else
        {
            logger.LogWarning(exception, "Handled application exception for {Method} {Route}", httpContext.Request.Method, requestIdentity);
        }

        httpContext.Response.StatusCode = statusCode;
        if (exception is AdmissionCheckInUnavailableException)
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
            Instance = isAdmissionRequest ? admissionRouteIdentity : httpContext.Request.Path
        };

        if (exception is not ConcurrencyConflictException and not QuotaExceededException)
        {
            problemDetails.Extensions["code"] = code;
        }

        if (exception is ConcurrencyConflictException concurrencyConflict)
        {
            problemDetails.Extensions["code"] = concurrencyConflict.Code;
            if (concurrencyConflict.EntityType is not null)
            {
                problemDetails.Extensions["entityType"] = concurrencyConflict.EntityType;
            }
            if (concurrencyConflict.EntityId is not null)
            {
                problemDetails.Extensions["entityId"] = concurrencyConflict.EntityId;
            }
        }

        if (exception is QuotaExceededException quotaExceeded)
        {
            QuotaProblemDetailsFactory.AddExtensions(problemDetails, quotaExceeded.Details);
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }
}
