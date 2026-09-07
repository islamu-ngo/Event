using Explore.Application.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.ExceptionHandling;

internal static class QuotaProblemDetailsFactory
{
    public const int StatusCode = StatusCodes.Status422UnprocessableEntity;
    public const string Title = "Quota exceeded";
    public const string Type = "/problems/quota_exceeded";

    public static ProblemDetails Create(HttpContext httpContext, QuotaExceededDetails details, string? detail)
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCode,
            Title = Title,
            Type = Type,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        AddExtensions(problemDetails, details);
        return problemDetails;
    }

    public static ActionResult ToQuotaProblemOrBadRequest<TKey>(
        this ControllerBase controller,
        BaseCommandResponse<TKey> response)
    {
        if (response.FailureCode == FailureCodes.QuotaExceeded && response.QuotaExceeded is not null)
        {
            var problemDetails = Create(controller.HttpContext, response.QuotaExceeded, response.Message);
            return new ObjectResult(problemDetails) { StatusCode = StatusCode };
        }

        return controller.BadRequest(response);
    }

    public static void AddExtensions(ProblemDetails problemDetails, QuotaExceededDetails details)
    {
        problemDetails.Extensions["code"] = FailureCodes.QuotaExceeded;
        problemDetails.Extensions["quotaKey"] = details.QuotaKey;
        problemDetails.Extensions["limit"] = details.Limit;
        problemDetails.Extensions["scope"] = details.Scope;

        if (details.Actual.HasValue)
        {
            problemDetails.Extensions["actual"] = details.Actual.Value;
        }

        if (details.Attempted.HasValue)
        {
            problemDetails.Extensions["attempted"] = details.Attempted.Value;
        }
    }
}
