using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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
        var errors = exception switch
        {
            ApplicationValidationException appValidationException => new Dictionary<string, string[]>
            {
                ["validation"] = appValidationException.Errors.ToArray()
            },
            FluentValidationException fluentValidationException => fluentValidationException.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key.ToLowerInvariant(),
                    group => group.Select(error => error.ErrorMessage).ToArray()),
            _ => null
        };

        if (errors is null)
        {
            return false;
        }

        logger.LogWarning(exception, "Validation failure for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Type = ApiProblemTypes.BadRequest,
            Detail = "One or more validation errors occurred.",
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = ApiProblemCodes.ValidationFailed;
        problemDetails.Extensions["errors"] = errors;
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        if (httpContext.Items["CorrelationId"] is string correlationId)
        {
            problemDetails.Extensions["correlationId"] = correlationId;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }
}
