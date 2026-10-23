using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Explore.API.Middleware;

using ApplicationValidationException = Explore.Application.Exceptions.ValidationException;
using FluentValidationException = FluentValidation.ValidationException;

namespace Explore.API.ExceptionHandling;

internal sealed class ValidationExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ValidationExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not (ApplicationValidationException or FluentValidationException))
        {
            return false;
        }

        logger.LogWarning(
            "API failure {Code} for {Method} {Route} responded {StatusCode} | TraceId={TraceId} CorrelationId={CorrelationId}",
            ApiProblemCodes.ValidationFailed, SafeRouteMetadata.GetMethod(httpContext),
            SafeRouteMetadata.GetRouteIdentityOrClassification(httpContext), StatusCodes.Status400BadRequest,
            httpContext.TraceIdentifier, httpContext.Items["CorrelationId"] as string);

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Type = ApiProblemTypes.BadRequest,
            Detail = "One or more validation errors occurred.",
            Instance = SafeRouteMetadata.GetRouteIdentityOrClassification(httpContext)
        };

        problemDetails.Extensions["code"] = ApiProblemCodes.ValidationFailed;
        // Both exception variants permit arbitrary messages and field names.
        // Keep the dictionary/array contract without exporting their contents.
        problemDetails.Extensions["errors"] = new Dictionary<string, string[]>
        {
            ["validation"] = ["The request failed validation."]
        };
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        if (httpContext.Items["CorrelationId"] is string correlationId)
        {
            problemDetails.Extensions["correlationId"] = correlationId;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        });
    }
}
