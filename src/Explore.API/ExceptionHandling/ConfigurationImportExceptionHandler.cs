namespace Explore.API.ExceptionHandling;

using Explore.API.ConfigurationImport;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

public sealed class ConfigurationImportExceptionHandler(
    IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ConfigurationImportSessionException importFailure)
            return false;

        ConfigurationImportProblem contract = Map(importFailure.FailureCode);
        httpContext.Response.StatusCode = contract.Status;
        if (contract.RetryAfterSeconds is { } retryAfterSeconds)
        {
            httpContext.Response.Headers.RetryAfter =
                retryAfterSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Type = ProblemType(contract.Status),
            Title = Title(contract.Status),
            Status = contract.Status,
            Detail = Detail(contract.Status),
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = contract.Code;
        if (contract.RetryAfterSeconds is { } retry)
            problem.Extensions["retryAfterSeconds"] = retry;

        return await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem
            });
    }

    private static ConfigurationImportProblem Map(string failureCode) =>
        failureCode switch
        {
            ConfigurationImportFailureCodes.ContractInvalid =>
                new(
                    StatusCodes.Status400BadRequest,
                    failureCode,
                    RetryAfterSeconds: null),
            ConfigurationImportFailureCodes.TooLarge =>
                new(
                    StatusCodes.Status413PayloadTooLarge,
                    failureCode,
                    RetryAfterSeconds: null),
            ConfigurationImportFailureCodes.ArtifactMissing
                or ConfigurationImportFailureCodes.TargetMismatch
                or ConfigurationImportFailureCodes.TokenInvalid =>
                new(
                    StatusCodes.Status404NotFound,
                    ConfigurationImportFailureCodes.ArtifactMissing,
                    RetryAfterSeconds: null),
            ConfigurationImportFailureCodes.Expired
                or ConfigurationImportFailureCodes.Cancelled
                or ConfigurationImportFailureCodes.Replayed
                or ConfigurationImportFailureCodes.StalePreview
                or ConfigurationImportFailureCodes.ArtifactIntegrityInvalid =>
                new(
                    StatusCodes.Status409Conflict,
                    failureCode,
                    RetryAfterSeconds: null),
            ConfigurationImportFailureCodes.ApplyFailed =>
                new(
                    StatusCodes.Status503ServiceUnavailable,
                    failureCode,
                    RetryAfterSeconds: 5),
            _ => new(
                StatusCodes.Status409Conflict,
                "configuration_import_conflict",
                RetryAfterSeconds: null)
        };

    private static string Title(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Invalid configuration import",
        StatusCodes.Status404NotFound => "Configuration import session not found",
        StatusCodes.Status413PayloadTooLarge => "Configuration import is too large",
        StatusCodes.Status503ServiceUnavailable =>
            "Configuration import is temporarily unavailable",
        _ => "Configuration import conflict"
    };

    private static string Detail(int status) => status switch
    {
        StatusCodes.Status400BadRequest =>
            "The configuration import request is invalid.",
        StatusCodes.Status404NotFound =>
            "The configuration import session is unavailable.",
        StatusCodes.Status413PayloadTooLarge =>
            "The configuration import exceeds the supported size.",
        StatusCodes.Status503ServiceUnavailable =>
            "The configuration import could not commit and no selected section was changed.",
        _ => "The configuration import session can no longer complete this operation."
    };

    private static string ProblemType(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ApiProblemTypes.BadRequest,
        StatusCodes.Status404NotFound => ApiProblemTypes.NotFound,
        StatusCodes.Status413PayloadTooLarge => ApiProblemTypes.PayloadTooLarge,
        StatusCodes.Status503ServiceUnavailable => ApiProblemTypes.ServiceUnavailable,
        _ => ApiProblemTypes.Conflict
    };
}
