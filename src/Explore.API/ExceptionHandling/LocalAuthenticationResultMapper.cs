using Explore.Application.Features.Authentication.Local.Models;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.ExceptionHandling;

internal static class LocalAuthenticationResultMapper
{
    private static readonly IReadOnlyDictionary<LocalAuthFailure, FailureDescriptor> Failures =
        new Dictionary<LocalAuthFailure, FailureDescriptor>
        {
            [LocalAuthFailure.InvalidRequest] = new(
                StatusCode: StatusCodes.Status400BadRequest,
                Title: "Invalid authentication request",
                Type: ApiProblemTypes.BadRequest,
                Detail: "The submitted authentication request is invalid."),
            [LocalAuthFailure.EmailVerificationRequired] = new(
                StatusCode: StatusCodes.Status401Unauthorized,
                Title: "Email verification required",
                Type: ApiProblemTypes.Unauthorized,
                Detail: "Verify the local account email address before signing in."),
            [LocalAuthFailure.InvalidCredentials] = new(
                StatusCode: StatusCodes.Status401Unauthorized,
                Title: "Authentication failed",
                Type: ApiProblemTypes.Unauthorized,
                Detail: "The submitted credentials are invalid."),
            [LocalAuthFailure.AccountLocked] = new(
                StatusCode: StatusCodes.Status401Unauthorized,
                Title: "Authentication failed",
                Type: ApiProblemTypes.Unauthorized,
                Detail: "The local account is temporarily unavailable."),
            [LocalAuthFailure.ProviderInactive] = new(
                StatusCode: StatusCodes.Status409Conflict,
                Title: "Authentication provider inactive",
                Type: ApiProblemTypes.Conflict,
                Detail: "Local Identity is not the active primary authentication provider."),
            [LocalAuthFailure.UserSynchronizationFailed] = new(
                StatusCode: StatusCodes.Status503ServiceUnavailable,
                Title: "Authentication unavailable",
                Type: ApiProblemTypes.ServiceUnavailable,
                Detail: "The authenticated account could not be synchronized."),
            [LocalAuthFailure.AuthenticationFailed] = new(
                StatusCode: StatusCodes.Status503ServiceUnavailable,
                Title: "Authentication unavailable",
                Type: ApiProblemTypes.ServiceUnavailable,
                Detail: "Local authentication is temporarily unavailable.")
        };

    private static readonly FailureDescriptor UnexpectedFailure = new(
        StatusCode: StatusCodes.Status503ServiceUnavailable,
        Title: "Authentication unavailable",
        Type: ApiProblemTypes.ServiceUnavailable,
        Detail: "Local authentication is temporarily unavailable.");

    internal static ActionResult<LocalAuthResponseDto> Map(
        ControllerBase controller,
        LocalAuthResponseDto response) =>
        response.Outcome switch
        {
            LocalAuthOutcome.Authenticated or LocalAuthOutcome.ReplacementRequired => controller.Ok(response),
            LocalAuthOutcome.Failed => MapFailure(controller, response),
            _ => throw new InvalidOperationException("Unknown Local authentication outcome.")
        };

    private static ActionResult<LocalAuthResponseDto> MapFailure(
        ControllerBase controller,
        LocalAuthResponseDto response)
    {
        FailureDescriptor descriptor = Failures.GetValueOrDefault(
            response.Failure!.Value,
            UnexpectedFailure);
        ProblemDetails problem = ApiProblemFactory.CreateProblem(
            controller.HttpContext,
            descriptor.StatusCode,
            descriptor.Title,
            descriptor.Type,
            descriptor.Detail,
            response.FailureCode);
        return ApiProblemFactory.ToProblemResult(problem);
    }

    private sealed record FailureDescriptor(
        int StatusCode,
        string Title,
        string Type,
        string Detail);
}
