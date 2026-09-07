using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Explore.API.ExceptionHandling;

internal static class ApiValidationProblemDetailsFactory
{
    private const string BodyKey = "body";

    public static IActionResult CreateInvalidModelStateResponse(ActionContext context)
    {
        var problemDetails = Create(
            context.HttpContext,
            StatusCodes.Status400BadRequest,
            "Validation failed",
            ApiProblemTypes.BadRequest,
            "One or more validation errors occurred.");

        problemDetails.Extensions["errors"] = NormalizeErrors(context.ModelState);

        return new ObjectResult(problemDetails)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }

    public static ProblemDetails CreateUnsupportedMediaType(HttpContext httpContext)
    {
        return Create(
            httpContext,
            StatusCodes.Status415UnsupportedMediaType,
            "Unsupported media type",
            ApiProblemTypes.UnsupportedMediaType,
            "The request content type is not supported for this endpoint.");
    }

    private static ProblemDetails Create(
        HttpContext httpContext,
        int statusCode,
        string title,
        string type,
        string detail)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = ApiProblemCodes.ValidationFailed;
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;
        problemDetails.Extensions["correlationId"] =
            httpContext.Items["CorrelationId"] as string;

        return problemDetails;
    }

    private static Dictionary<string, string[]> NormalizeErrors(ModelStateDictionary modelState)
    {
        return modelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .GroupBy(entry => NormalizeFieldKey(entry.Key, entry.Value!.Errors), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(entry => entry.Value!.Errors.Select(error => NormalizeMessage(group.Key, error)))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    private static string NormalizeFieldKey(string key, ModelErrorCollection errors)
    {
        if (errors.Any(error =>
                error.Exception is not null ||
                error.ErrorMessage.Contains("request body", StringComparison.OrdinalIgnoreCase)))
        {
            return BodyKey;
        }

        if (string.IsNullOrWhiteSpace(key) || key is "$")
        {
            return BodyKey;
        }

        var normalized = key.Trim();
        if (normalized.StartsWith('$'))
        {
            return BodyKey;
        }

        if (normalized.StartsWith("$.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        var separatorIndex = normalized.LastIndexOf('.');
        if (separatorIndex >= 0 && separatorIndex < normalized.Length - 1)
        {
            normalized = normalized[(separatorIndex + 1)..];
        }

        return char.ToLowerInvariant(normalized[0]) + normalized[1..];
    }

    private static string NormalizeMessage(string fieldKey, ModelError error)
    {
        if (fieldKey == BodyKey || error.Exception is not null)
        {
            return "Request body is invalid or contains unsupported fields.";
        }

        return string.IsNullOrWhiteSpace(error.ErrorMessage)
            ? "The field is invalid."
            : error.ErrorMessage;
    }
}
