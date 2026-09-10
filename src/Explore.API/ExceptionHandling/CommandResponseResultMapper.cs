using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.EventReporting;
using Explore.Application.Responses;
using Explore.Infrastructure.Services.Keycloak;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.ExceptionHandling;

internal static class CommandResponseResultMapper
{
    internal const string VisitorAccessAccountRequiredConflict = "visitor_access_account_required_conflict";
    private static readonly ApiValidationProblemDescriptor CommandValidationProblem = new(
        "command",
        "Command validation failed",
        "The command could not be completed.");

    private static readonly CommandFailurePolicy VisitorAccessFailurePolicy = CommandFailurePolicy
        .ValidatedBy(CommandValidationProblem)
        .Conflict("Visitor access conflict",
            "Existing account-required events must retain an available public onboarding provider.",
            VisitorAccessAccountRequiredConflict);

    private static readonly ApiNotFoundProblemDescriptor CommandNotFoundProblem = new(
        "Resource not found",
        "The requested resource was not found.",
        FailureCodes.NotFound);

    private static readonly ApiValidationProblemDescriptor StorageUploadValidationProblem = new(
        "storageUpload",
        "Storage upload validation failed",
        "Storage upload command failed.");

    private static readonly ApiValidationProblemDescriptor EmailDispatchValidationProblem = new(
        "emailDispatch",
        "Email dispatch validation failed",
        "Email dispatch command failed.");

    private static readonly ApiValidationProblemDescriptor AiAssistantValidationProblem = new(
        "aiAssistant",
        "AI assistant request failed",
        "AI assistant request failed.");

    private static readonly ApiValidationProblemDescriptor EventReportValidationProblem = new(
        "eventReport",
        "Event report validation failed",
        "Event report submission failed.");

    private static readonly ApiValidationProblemDescriptor AuthProviderValidationProblem = new(
        "instanceAuthProviderConfiguration",
        "Instance auth-provider configuration validation failed",
        "Instance auth-provider configuration update failed.");

    /// <summary>
    /// Maps a command result for capabilities that use the platform's shared failure vocabulary rather than a
    /// feature-specific <see cref="CommandFailurePolicy"/>.
    /// <para>
    /// Failures emit RFC 7807 <c>ProblemDetails</c>, the same shape every other failure path in the API
    /// produces. This previously returned the bare <c>BaseCommandResponse</c> as the body, which meant two
    /// endpoints in the same product could fail in two different formats — clients had to branch on which
    /// endpoint they called before they could read an error. <c>[ApiController]</c> already promises
    /// ProblemDetails for framework-generated failures, so returning anything else from handler-generated
    /// failures made the API inconsistent with itself.
    /// </para>
    /// </summary>
    public static ActionResult MapCommandResponse<TKey>(
        this ControllerBase controller,
        BaseCommandResponse<TKey> response)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccess)
        {
            return controller.Ok(response);
        }

        return response.FailureCode switch
        {
            VisitorAccessAccountRequiredConflict => VisitorAccessFailurePolicy.Map(controller, response),
            FailureCodes.NotFound => controller.ToNotFoundProblem(
                CommandNotFoundProblem,
                response.Message),
            FailureCodes.AdminRequired => controller.ToForbiddenProblem(
                detail: response.Message ?? "The authenticated principal is not authorized to perform this operation."),
            FailureCodes.AuthenticationRequired => controller.ToAuthenticationRequiredProblem(),
            FailureCodes.ConcurrencyConflict => controller.ToCommandConflictProblem(
                response,
                "Concurrency conflict",
                "The resource was modified by another request."),
            _ => controller.ToCommandValidationProblem(response, CommandValidationProblem),
        };
    }

    public static ActionResult ToCommandValidationProblem<TKey>(
        this ControllerBase controller,
        BaseCommandResponse<TKey> response,
        ApiValidationProblemDescriptor descriptor)
    {
        var errors = response.Errors is { Count: > 0 }
            ? response.Errors.ToArray()
            : [response.Message ?? descriptor.FallbackDetail];
        var detail = response.Message ?? descriptor.FallbackDetail;
        var code = string.IsNullOrWhiteSpace(response.FailureCode)
            ? ApiProblemCodes.ValidationFailed
            : response.FailureCode;

        var problemDetails = ApiProblemFactory.CreateValidationProblem(
            controller.HttpContext,
            descriptor,
            errors,
            detail,
            code);

        if (response.Id is not null)
        {
            problemDetails.Extensions["id"] = response.Id;
            problemDetails.Extensions["success"] = response.IsSuccess;
            problemDetails.Extensions["message"] = response.Message;
        }

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToValidationProblem(
        this ControllerBase controller,
        ApiValidationProblemDescriptor descriptor,
        string detail,
        string code = ApiProblemCodes.ValidationFailed)
    {
        var problemDetails = ApiProblemFactory.CreateValidationProblem(
            controller.HttpContext,
            descriptor,
            [detail],
            detail,
            code);

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToIngressValidationProblem(
        this ControllerBase controller,
        ApiValidationProblemDescriptor descriptor,
        string code = ApiProblemCodes.ValidationFailed)
    {
        var problemDetails = ApiProblemFactory.CreateValidationProblem(
            controller.HttpContext,
            descriptor,
            [descriptor.FallbackDetail],
            descriptor.FallbackDetail,
            code);

        // Request paths can contain rejected scalar identities and must not be reflected to the caller.
        problemDetails.Instance = null;
        return new JsonResult(problemDetails)
        {
            StatusCode = problemDetails.Status,
            ContentType = "application/problem+json"
        };
    }


    public static ActionResult ToAuthenticationRequiredProblem(
        this ControllerBase controller,
        string title = "User ID not found in token",
        string detail = "The authenticated principal does not include a supported user identifier claim.")
    {
        var problemDetails = ApiProblemFactory.CreateAuthenticationRequiredProblem(
            controller.HttpContext,
            title,
            detail);

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToForbiddenProblem(
        this ControllerBase controller,
        string title = "Forbidden",
        string? detail = null)
    {
        var problemDetails = ApiProblemFactory.CreateForbiddenProblem(
            controller.HttpContext,
            title,
            detail ?? "The authenticated principal is not authorized to perform this operation.");

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToCommandConflictProblem<TKey>(
        this ControllerBase controller,
        BaseCommandResponse<TKey> response,
        string title,
        string fallbackDetail)
    {
        var problemDetails = ApiProblemFactory.CreateConflictProblem(
            controller.HttpContext,
            title,
            response.Message ?? fallbackDetail,
            string.IsNullOrWhiteSpace(response.FailureCode)
                ? ApiProblemCodes.ResourceConflict
                : response.FailureCode);

        if (response.Id is not null)
        {
            problemDetails.Extensions["id"] = response.Id;
            problemDetails.Extensions["success"] = response.IsSuccess;
            problemDetails.Extensions["message"] = response.Message;
        }

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToNotFoundProblem(
        this ControllerBase controller,
        ApiNotFoundProblemDescriptor descriptor,
        string? detail = null)
    {
        var activeDescriptor = detail is not null
            ? new ApiNotFoundProblemDescriptor(descriptor.Title, detail, descriptor.Code)
            : descriptor;
        var problemDetails = ApiProblemFactory.CreateNotFoundProblem(controller.HttpContext, activeDescriptor);
        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToGoneProblem(
        this ControllerBase controller,
        string title,
        string detail,
        string code = ApiProblemCodes.ResourceConflict)
    {
        var problemDetails = ApiProblemFactory.CreateGoneProblem(
            controller.HttpContext,
            title,
            detail,
            code);

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToBadGatewayProblem(
        this ControllerBase controller,
        string title,
        string detail,
        string code = ApiProblemCodes.ProviderGateway)
    {
        var problemDetails = ApiProblemFactory.CreateBadGatewayProblem(
            controller.HttpContext,
            title,
            detail,
            code);

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToServiceUnavailableProblem(
        this ControllerBase controller,
        string title,
        string detail,
        string code = ApiProblemCodes.UnexpectedError)
    {
        var problemDetails = ApiProblemFactory.CreateServiceUnavailableProblem(
            controller.HttpContext,
            title,
            detail,
            code);

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToStorageUploadProblem(
        this ControllerBase controller,
        BaseCommandResponse<StorageUploadSessionDto> response)
    {
        var statusCode = ResolveStorageUploadStatusCode(response.FailureCode);

        if (statusCode == StatusCodes.Status400BadRequest)
        {
            if (TryResolveStorageUploadPublicDetail(response.FailureCode, out var validationDetail))
            {
                var validationProblemDetails = ApiProblemFactory.CreateValidationProblem(
                    controller.HttpContext,
                    StorageUploadValidationProblem,
                    [validationDetail],
                    validationDetail,
                    response.FailureCode ?? ApiProblemCodes.ValidationFailed);

                return ApiProblemFactory.ToProblemResult(validationProblemDetails);
            }

            return controller.ToCommandValidationProblem(response, StorageUploadValidationProblem);
        }

        var detail = TryResolveStorageUploadPublicDetail(response.FailureCode, out var publicDetail)
            ? publicDetail
            : response.Message ?? StorageUploadValidationProblem.FallbackDetail;

        var problemDetails = ApiProblemFactory.CreateProblem(
            controller.HttpContext,
            statusCode,
            ResolveStorageUploadTitle(statusCode),
            ResolveStorageUploadType(statusCode, response.FailureCode),
            detail,
            response.FailureCode ?? ApiProblemCodes.UnexpectedError);

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToEventReportProblem(
        this ControllerBase controller,
        BaseCommandResponse<Guid> response)
    {
        var statusCode = ResolveEventReportStatusCode(response.FailureCode);

        if (statusCode == StatusCodes.Status400BadRequest)
        {
            return controller.ToCommandValidationProblem(response, EventReportValidationProblem);
        }

        var problemDetails = ApiProblemFactory.CreateProblem(
            controller.HttpContext,
            statusCode,
            ResolveEventReportTitle(statusCode, response.FailureCode),
            ResolveEventReportType(statusCode, response.FailureCode),
            response.Message ?? EventReportValidationProblem.FallbackDetail,
            response.FailureCode ?? ApiProblemCodes.UnexpectedError);

        problemDetails.Extensions["id"] = response.Id;
        problemDetails.Extensions["success"] = response.IsSuccess;
        problemDetails.Extensions["message"] = response.Message;

        if (response.QuotaExceeded is not null)
        {
            problemDetails.Extensions["quota"] = response.QuotaExceeded;
        }

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToAuthProviderProblem(
        this ControllerBase controller,
        BaseCommandResponse<Guid> response)
    {
        var statusCode = ResolveAuthProviderStatusCode(response.FailureCode);

        if (statusCode == StatusCodes.Status400BadRequest)
        {
            return controller.ToCommandValidationProblem(response, AuthProviderValidationProblem);
        }

        var problemDetails = ApiProblemFactory.CreateProblem(
            controller.HttpContext,
            statusCode,
            ResolveAuthProviderTitle(statusCode, response.FailureCode),
            ResolveAuthProviderType(statusCode),
            response.Message ?? AuthProviderValidationProblem.FallbackDetail,
            response.FailureCode ?? ApiProblemCodes.UnexpectedError);

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    private static int ResolveAuthProviderStatusCode(string? failureCode)
        => failureCode switch
        {
            KeycloakFailureCodes.Timeout => StatusCodes.Status503ServiceUnavailable,
            KeycloakFailureCodes.Unreachable => StatusCodes.Status503ServiceUnavailable,
            KeycloakFailureCodes.InvalidResponse => StatusCodes.Status502BadGateway,
            KeycloakFailureCodes.RealmCheckFailed => StatusCodes.Status502BadGateway,
            KeycloakFailureCodes.RealmCreateFailed => StatusCodes.Status502BadGateway,
            KeycloakFailureCodes.ClientLookupFailed => StatusCodes.Status502BadGateway,
            KeycloakFailureCodes.ClientCreateFailed => StatusCodes.Status502BadGateway,
            KeycloakFailureCodes.ClientSecretUpdateFailed => StatusCodes.Status502BadGateway,
            KeycloakFailureCodes.OfflineAccessRoleUpdateFailed => StatusCodes.Status502BadGateway,
            KeycloakFailureCodes.ClientScopeUpdateFailed => StatusCodes.Status502BadGateway,
            KeycloakFailureCodes.OfflineAccessScopeMappingFailed => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest
        };

    private static string ResolveAuthProviderTitle(int statusCode, string? failureCode)
        => statusCode switch
        {
            StatusCodes.Status502BadGateway => "Keycloak provider returned an invalid bootstrap response",
            StatusCodes.Status503ServiceUnavailable => "Keycloak provider unavailable",
            _ when failureCode == KeycloakFailureCodes.BootstrapValidationFailed => AuthProviderValidationProblem.Title,
            _ => AuthProviderValidationProblem.Title
        };

    private static string ResolveAuthProviderType(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status502BadGateway => ApiProblemTypes.BadGateway,
            StatusCodes.Status503ServiceUnavailable => ApiProblemTypes.ServiceUnavailable,
            _ => ApiProblemTypes.BadRequest
        };

    private static int ResolveStorageUploadStatusCode(string? failureCode)
        => failureCode switch
        {
            FailureCodes.StorageUploadTooLarge => StatusCodes.Status413PayloadTooLarge,
            FailureCodes.QuotaExceeded => StatusCodes.Status422UnprocessableEntity,
            FailureCodes.StorageUploadSessionNotFound => StatusCodes.Status404NotFound,
            FailureCodes.StorageUploadSessionFinalized => StatusCodes.Status409Conflict,
            FailureCodes.StorageUploadSessionExpired => StatusCodes.Status409Conflict,
            FailureCodes.StorageUploadSessionInvalidState => StatusCodes.Status409Conflict,
            FailureCodes.StorageUploadSizeMismatch => StatusCodes.Status400BadRequest,
            FailureCodes.StorageUploadContentTypeMismatch => StatusCodes.Status400BadRequest,
            FailureCodes.StorageUploadContentSignatureMismatch => StatusCodes.Status400BadRequest,
            FailureCodes.StorageUploadWriteFailed => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };

    private static bool TryResolveStorageUploadPublicDetail(string? failureCode, out string detail)
    {
        detail = failureCode switch
        {
            FailureCodes.StorageUploadTooLarge => "Upload exceeds the configured per-file limit.",
            FailureCodes.QuotaExceeded => "Storage quota has been exceeded.",
            FailureCodes.StorageUploadSessionNotFound => "Upload session was not found.",
            FailureCodes.StorageUploadSessionFinalized => "Finalized upload sessions cannot be canceled.",
            FailureCodes.StorageUploadSessionExpired => "Upload session has expired.",
            FailureCodes.StorageUploadSessionInvalidState => "Upload session cannot accept bytes in its current state.",
            FailureCodes.StorageUploadSizeMismatch => "Upload content length does not match the reserved byte count.",
            FailureCodes.StorageUploadContentTypeMismatch => "Upload content type does not match the reserved content type.",
            FailureCodes.StorageUploadContentSignatureMismatch => "Upload content did not match the reserved content policy.",
            FailureCodes.StorageUploadWriteFailed => "Storage provider returned invalid upload metadata.",
            _ => string.Empty
        };

        return detail.Length > 0;
    }

    private static string ResolveStorageUploadTitle(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status404NotFound => "Storage upload session not found",
            StatusCodes.Status409Conflict => "Storage upload session conflict",
            StatusCodes.Status413PayloadTooLarge => "Storage upload is too large",
            StatusCodes.Status422UnprocessableEntity => "Storage quota exceeded",
            StatusCodes.Status503ServiceUnavailable => "Storage provider unavailable",
            _ => StorageUploadValidationProblem.Title
        };

    private static string ResolveStorageUploadType(int statusCode, string? failureCode)
        => failureCode == FailureCodes.QuotaExceeded
            ? "/problems/quota_exceeded"
            : statusCode switch
            {
                StatusCodes.Status404NotFound => ApiProblemTypes.NotFound,
                StatusCodes.Status409Conflict => ApiProblemTypes.Conflict,
                StatusCodes.Status413PayloadTooLarge => ApiProblemTypes.PayloadTooLarge,
                StatusCodes.Status422UnprocessableEntity => ApiProblemTypes.UnprocessableEntity,
                StatusCodes.Status503ServiceUnavailable => ApiProblemTypes.ServiceUnavailable,
                _ => ApiProblemTypes.BadRequest
            };

    private static int ResolveEventReportStatusCode(string? failureCode)
        => failureCode switch
        {
            EventReportFailureCodes.UserUnresolved => StatusCodes.Status401Unauthorized,
            EventReportFailureCodes.ReporterActorUnresolved => StatusCodes.Status403Forbidden,
            EventReportFailureCodes.ModeratorUnavailable => StatusCodes.Status403Forbidden,
            EventReportFailureCodes.AssigneeUnavailable => StatusCodes.Status403Forbidden,
            EventReportFailureCodes.EventNotFound => StatusCodes.Status404NotFound,
            EventReportFailureCodes.ReportNotFound => StatusCodes.Status404NotFound,
            EventReportFailureCodes.CaseNotFound => StatusCodes.Status404NotFound,
            EventReportFailureCodes.DecisionNotFound => StatusCodes.Status404NotFound,
            EventReportFailureCodes.EventMismatch => StatusCodes.Status404NotFound,
            EventReportFailureCodes.Duplicate => StatusCodes.Status409Conflict,
            EventReportFailureCodes.IntakeDisabled => StatusCodes.Status409Conflict,
            EventReportFailureCodes.EventInvalidStatus => StatusCodes.Status409Conflict,
            EventReportFailureCodes.CaseConcurrencyConflict => StatusCodes.Status409Conflict,
            EventReportFailureCodes.CaseInvalidStatus => StatusCodes.Status409Conflict,
            EventReportFailureCodes.ReportInvalidStatus => StatusCodes.Status409Conflict,
            EventReportFailureCodes.AssignmentMismatch => StatusCodes.Status409Conflict,
            EventReportFailureCodes.DecisionInvalid => StatusCodes.Status409Conflict,
            FailureCodes.QuotaExceeded => StatusCodes.Status422UnprocessableEntity,
            EventReportFailureCodes.DecisionExecutionFailed => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };

    private static string ResolveEventReportTitle(int statusCode, string? failureCode)
        => statusCode switch
        {
            StatusCodes.Status401Unauthorized => "Authentication required",
            StatusCodes.Status403Forbidden => "Event report access denied",
            StatusCodes.Status404NotFound => failureCode == EventReportFailureCodes.EventNotFound
                ? "Event not found"
                : "Event report not found",
            StatusCodes.Status409Conflict => "Event report conflict",
            StatusCodes.Status422UnprocessableEntity => "Event report quota exceeded",
            StatusCodes.Status503ServiceUnavailable => "Event report decision execution failed",
            _ => EventReportValidationProblem.Title
        };

    private static string ResolveEventReportType(int statusCode, string? failureCode)
        => failureCode == FailureCodes.QuotaExceeded
            ? "/problems/quota_exceeded"
            : statusCode switch
            {
                StatusCodes.Status401Unauthorized => ApiProblemTypes.Unauthorized,
                StatusCodes.Status403Forbidden => ApiProblemTypes.Forbidden,
                StatusCodes.Status404NotFound => ApiProblemTypes.NotFound,
                StatusCodes.Status409Conflict => ApiProblemTypes.Conflict,
                StatusCodes.Status422UnprocessableEntity => ApiProblemTypes.UnprocessableEntity,
                _ => ApiProblemTypes.BadRequest
            };

    public static ActionResult ToEmailDispatchValidationProblem(
        this ControllerBase controller,
        string detail,
        IReadOnlyCollection<string>? errors)
    {
        var problemDetails = ApiProblemFactory.CreateValidationProblem(
            controller.HttpContext,
            EmailDispatchValidationProblem,
            (errors is { Count: > 0 } ? errors : [detail]).ToArray(),
            detail,
            EmailDispatchFailureCodes.ValidationFailed);

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    public static ActionResult ToEmailDispatchProblem(
        this ControllerBase controller,
        BaseCommandResponse<Guid> response)
    {
        var statusCode = ResolveEmailDispatchStatusCode(response.FailureCode);

        if (statusCode == StatusCodes.Status400BadRequest)
        {
            return controller.ToCommandValidationProblem(response, EmailDispatchValidationProblem);
        }

        var problemDetails = ApiProblemFactory.CreateProblem(
            controller.HttpContext,
            statusCode,
            ResolveEmailDispatchTitle(statusCode),
            ResolveEmailDispatchType(statusCode),
            response.Message ?? EmailDispatchValidationProblem.FallbackDetail,
            response.FailureCode ?? ApiProblemCodes.UnexpectedError);

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    private static int ResolveEmailDispatchStatusCode(string? failureCode)
        => failureCode switch
        {
            EmailDispatchFailureCodes.NotFound => StatusCodes.Status404NotFound,
            EmailDispatchFailureCodes.InvalidTransition => StatusCodes.Status409Conflict,
            EmailDispatchFailureCodes.ConcurrentTransition => StatusCodes.Status409Conflict,
            EmailDispatchFailureCodes.Misconfigured => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };

    private static string ResolveEmailDispatchTitle(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status404NotFound => "Email dispatch row not found",
            StatusCodes.Status409Conflict => "Email dispatch state transition conflict",
            StatusCodes.Status503ServiceUnavailable => "Email dispatch is misconfigured",
            _ => EmailDispatchValidationProblem.Title
        };

    private static string ResolveEmailDispatchType(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status404NotFound => ApiProblemTypes.NotFound,
            StatusCodes.Status409Conflict => ApiProblemTypes.Conflict,
            StatusCodes.Status503ServiceUnavailable => ApiProblemTypes.ServiceUnavailable,
            _ => ApiProblemTypes.BadRequest
        };

    public static ActionResult ToAiAssistantProblem(
        this ControllerBase controller,
        BaseCommandResponse<Guid> response)
    {
        var statusCode = ResolveAiAssistantStatusCode(response.FailureCode);
        var title = ResolveAiAssistantTitle(statusCode, response.FailureCode);
        var detail = ResolveAiAssistantDetail(response, statusCode);
        ProblemDetails problemDetails;

        if (statusCode == StatusCodes.Status400BadRequest)
        {
            var errors = response.Errors is { Count: > 0 }
                ? response.Errors.ToArray()
                : [response.Message ?? AiAssistantValidationProblem.FallbackDetail];
            var descriptor = new ApiValidationProblemDescriptor(
                AiAssistantValidationProblem.ErrorKey,
                title,
                AiAssistantValidationProblem.FallbackDetail);

            problemDetails = ApiProblemFactory.CreateValidationProblem(
                controller.HttpContext,
                descriptor,
                errors,
                detail,
                response.FailureCode ?? ApiProblemCodes.ValidationFailed);
        }
        else
        {
            problemDetails = ApiProblemFactory.CreateProblem(
                controller.HttpContext,
                statusCode,
                title,
                ResolveAiAssistantType(statusCode, response.FailureCode),
                detail,
                response.FailureCode ?? ApiProblemCodes.UnexpectedError);
        }

        if (response.QuotaExceeded is not null)
        {
            problemDetails.Extensions["quota"] = response.QuotaExceeded;
        }

        return ApiProblemFactory.ToProblemResult(problemDetails);
    }

    private static int ResolveAiAssistantStatusCode(string? failureCode)
        => failureCode switch
        {
            "unauthenticated" => StatusCodes.Status401Unauthorized,
            "conversation_not_found" => StatusCodes.Status404NotFound,
            "run_not_found" => StatusCodes.Status404NotFound,
            "proposed_action_not_found" => StatusCodes.Status404NotFound,
            "conversation_not_active" => StatusCodes.Status409Conflict,
            "run_not_cancellable" => StatusCodes.Status409Conflict,
            "proposed_action_rejected" => StatusCodes.Status409Conflict,
            "proposed_action_failed" => StatusCodes.Status409Conflict,
            "invalid_proposed_action_state" => StatusCodes.Status409Conflict,
            "idempotency_key_conflict" => StatusCodes.Status409Conflict,
            "idempotency_replay_failed" => StatusCodes.Status409Conflict,
            FailureCodes.QuotaExceeded => StatusCodes.Status422UnprocessableEntity,
            "provider_not_ready" => StatusCodes.Status503ServiceUnavailable,
            "provider_timeout" => StatusCodes.Status503ServiceUnavailable,
            "provider_unreachable" => StatusCodes.Status503ServiceUnavailable,
            "http_429" => StatusCodes.Status429TooManyRequests,
            "disabled" => StatusCodes.Status403Forbidden,
            "provider_not_configured" => StatusCodes.Status403Forbidden,
            "provider_unsupported" => StatusCodes.Status403Forbidden,
            "endpoint_not_configured" => StatusCodes.Status403Forbidden,
            "api_key_not_configured" => StatusCodes.Status403Forbidden,
            "model_not_configured" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

    private static string ResolveAiAssistantDetail(BaseCommandResponse<Guid> response, int statusCode)
    {
        if ((statusCode == StatusCodes.Status400BadRequest || statusCode == StatusCodes.Status503ServiceUnavailable)
            && TryGetFirstAiAssistantError(response) is { } firstError)
        {
            return firstError;
        }

        return response.Message ?? AiAssistantValidationProblem.FallbackDetail;
    }

    private static string? TryGetFirstAiAssistantError(BaseCommandResponse<Guid> response)
    {
        if (response.Errors is null)
        {
            return null;
        }

        foreach (var error in response.Errors)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                return error.Trim();
            }
        }

        return null;
    }

    private static string ResolveAiAssistantTitle(int statusCode, string? failureCode)
        => statusCode switch
        {
            StatusCodes.Status401Unauthorized => "AI assistant authentication required",
            StatusCodes.Status403Forbidden => "AI assistant unavailable",
            StatusCodes.Status404NotFound => "AI conversation not found",
            StatusCodes.Status409Conflict => "AI conversation conflict",
            StatusCodes.Status422UnprocessableEntity => "AI assistant quota exceeded",
            StatusCodes.Status429TooManyRequests => "AI provider rate limited",
            StatusCodes.Status503ServiceUnavailable => "AI provider unavailable",
            _ when failureCode == "validation_failed" => "AI assistant validation failed",
            _ => AiAssistantValidationProblem.Title
        };

    private static string ResolveAiAssistantType(int statusCode, string? failureCode)
        => failureCode == FailureCodes.QuotaExceeded
            ? "/problems/quota_exceeded"
            : statusCode switch
            {
                StatusCodes.Status401Unauthorized => ApiProblemTypes.Unauthorized,
                StatusCodes.Status403Forbidden => ApiProblemTypes.Forbidden,
                StatusCodes.Status404NotFound => ApiProblemTypes.NotFound,
                StatusCodes.Status409Conflict => ApiProblemTypes.Conflict,
                StatusCodes.Status422UnprocessableEntity => ApiProblemTypes.UnprocessableEntity,
                StatusCodes.Status429TooManyRequests => ApiProblemTypes.TooManyRequests,
                StatusCodes.Status503ServiceUnavailable => ApiProblemTypes.ServiceUnavailable,
                _ => ApiProblemTypes.BadRequest
            };
}
