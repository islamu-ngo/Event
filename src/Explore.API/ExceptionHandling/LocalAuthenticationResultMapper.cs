// ABOUTME: Declaratively maps Local Identity application outcomes to RFC 7807 HTTP responses.
// ABOUTME: Keeps credential failures generic and prevents controllers from branching on failure codes.

using Explore.Application.Features.Authentication.Local.Models;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.ExceptionHandling;

internal static class LocalAuthenticationResultMapper
{
    private static readonly IReadOnlyDictionary<string, FailureDescriptor> Failures =
        new Dictionary<string, FailureDescriptor>(StringComparer.Ordinal)
        {
            ["invalid_request"] = new(
                StatusCodes.Status400BadRequest,
                "Invalid authentication request",
                ApiProblemTypes.BadRequest,
                "The submitted authentication request is invalid."),
            ["email_verification_required"] = new(
                StatusCode: StatusCodes.Status401Unauthorized,
                Title: "Email verification required",
                Type: ApiProblemTypes.Unauthorized,
                Detail: "Verify the local account email address before signing in."),
            ["invalid_credentials"] = new(
                StatusCodes.Status401Unauthorized,
                "Authentication failed",
                ApiProblemTypes.Unauthorized,
                "The submitted credentials are invalid."),
            ["account_locked"] = new(
                StatusCodes.Status401Unauthorized,
                "Authentication failed",
                ApiProblemTypes.Unauthorized,
                "The local account is temporarily unavailable."),
            ["provider_inactive"] = new(
                StatusCodes.Status409Conflict,
                "Authentication provider inactive",
                ApiProblemTypes.Conflict,
                "Local Identity is not the active primary authentication provider."),
            ["user_sync_failed"] = new(
                StatusCodes.Status503ServiceUnavailable,
                "Authentication unavailable",
                ApiProblemTypes.ServiceUnavailable,
                "The authenticated account could not be synchronized."),
            ["authentication_failed"] = new(
                StatusCodes.Status503ServiceUnavailable,
                "Authentication unavailable",
                ApiProblemTypes.ServiceUnavailable,
                "Local authentication is temporarily unavailable.")
        };

    private static readonly FailureDescriptor UnexpectedFailure = new(
        StatusCodes.Status503ServiceUnavailable,
        "Authentication unavailable",
        ApiProblemTypes.ServiceUnavailable,
        "Local authentication is temporarily unavailable.");

    internal static ActionResult<LocalAuthResponseDto> Map(
        ControllerBase controller,
        LocalAuthResponseDto response) =>
        response.Success
            ? controller.Ok(response)
            : MapFailure<LocalAuthResponseDto>(controller, response.FailureCode);

    private static ActionResult<T> MapFailure<T>(
        ControllerBase controller,
        string failureCode)
    {
        FailureDescriptor descriptor = Failures.GetValueOrDefault(
            failureCode,
            UnexpectedFailure);
        ProblemDetails problem = ApiProblemFactory.CreateProblem(
            controller.HttpContext,
            descriptor.StatusCode,
            descriptor.Title,
            descriptor.Type,
            descriptor.Detail,
            failureCode);
        return ApiProblemFactory.ToProblemResult(problem);
    }

    private sealed record FailureDescriptor(
        int StatusCode,
        string Title,
        string Type,
        string Detail);
}
