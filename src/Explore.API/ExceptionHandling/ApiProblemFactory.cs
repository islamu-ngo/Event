using Microsoft.AspNetCore.Mvc;

namespace Explore.API.ExceptionHandling;

internal static class ApiProblemFactory
{
    private const string ProblemJsonContentType = "application/problem+json";

    public static ValidationProblemDetails CreateValidationProblem(
        HttpContext httpContext,
        ApiValidationProblemDescriptor descriptor,
        IReadOnlyCollection<string> errors,
        string detail,
        string code)
    {
        var problemDetails = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [descriptor.ErrorKey] = errors.ToArray()
        })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = descriptor.Title,
            Type = ApiProblemTypes.BadRequest,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = string.IsNullOrWhiteSpace(code)
            ? ApiProblemCodes.ValidationFailed
            : code;
        AddStandardExtensions(httpContext, problemDetails);

        return problemDetails;
    }


    public static ProblemDetails CreateAuthenticationRequiredProblem(
        HttpContext httpContext,
        string title = "Authentication required",
        string detail = "The request requires an authenticated principal with a supported user identifier claim.")
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = title,
            Type = ApiProblemTypes.Unauthorized,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = ApiProblemCodes.AuthenticationRequired;
        AddStandardExtensions(httpContext, problemDetails);

        return problemDetails;
    }

    public static ProblemDetails CreateForbiddenProblem(
        HttpContext httpContext,
        string title = "Forbidden",
        string detail = "The authenticated principal is not authorized to perform this operation.")
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = title,
            Type = ApiProblemTypes.Forbidden,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = ApiProblemCodes.Forbidden;
        AddStandardExtensions(httpContext, problemDetails);

        return problemDetails;
    }

    public static ProblemDetails CreateConflictProblem(
        HttpContext httpContext,
        string title,
        string detail,
        string code = ApiProblemCodes.ResourceConflict)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
            Type = ApiProblemTypes.Conflict,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = string.IsNullOrWhiteSpace(code)
            ? ApiProblemCodes.ResourceConflict
            : code;
        AddStandardExtensions(httpContext, problemDetails);

        return problemDetails;
    }

    public static ProblemDetails CreateNotFoundProblem(
        HttpContext httpContext,
        ApiNotFoundProblemDescriptor descriptor)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = descriptor.Title,
            Type = ApiProblemTypes.NotFound,
            Detail = descriptor.Detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = descriptor.Code;
        AddStandardExtensions(httpContext, problemDetails);

        return problemDetails;
    }

    public static ProblemDetails CreateGoneProblem(
        HttpContext httpContext,
        string title,
        string detail,
        string code = ApiProblemCodes.ResourceConflict)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status410Gone,
            Title = title,
            Type = ApiProblemTypes.Gone,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = string.IsNullOrWhiteSpace(code)
            ? ApiProblemCodes.ResourceConflict
            : code;
        AddStandardExtensions(httpContext, problemDetails);

        return problemDetails;
    }

    public static ProblemDetails CreateBadGatewayProblem(
        HttpContext httpContext,
        string title,
        string detail,
        string code = ApiProblemCodes.ProviderGateway)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title = title,
            Type = ApiProblemTypes.BadGateway,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = string.IsNullOrWhiteSpace(code)
            ? ApiProblemCodes.ProviderGateway
            : code;
        AddStandardExtensions(httpContext, problemDetails);

        return problemDetails;
    }

    public static ProblemDetails CreateServiceUnavailableProblem(
        HttpContext httpContext,
        string title,
        string detail,
        string code = ApiProblemCodes.UnexpectedError)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = title,
            Type = ApiProblemTypes.ServiceUnavailable,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = string.IsNullOrWhiteSpace(code)
            ? ApiProblemCodes.UnexpectedError
            : code;
        AddStandardExtensions(httpContext, problemDetails);

        return problemDetails;
    }

    public static ProblemDetails CreateProblem(
        HttpContext httpContext,
        int statusCode,
        string title,
        string type,
        string detail,
        string code)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = string.IsNullOrWhiteSpace(code)
            ? ApiProblemCodes.UnexpectedError
            : code;
        AddStandardExtensions(httpContext, problemDetails);

        return problemDetails;
    }

    public static ObjectResult ToProblemResult(ProblemDetails problemDetails)
    {
        return new ObjectResult(problemDetails)
        {
            StatusCode = problemDetails.Status,
            ContentTypes = { ProblemJsonContentType }
        };
    }

    private static void AddStandardExtensions(HttpContext httpContext, ProblemDetails problemDetails)
    {
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        if (httpContext.Items["CorrelationId"] is string correlationId)
        {
            problemDetails.Extensions["correlationId"] = correlationId;
        }
    }
}
