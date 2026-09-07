using Explore.API.ExceptionHandling;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.EventReporting;
using Explore.Application.Responses;
using Explore.Infrastructure.Services.Keycloak;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.ExceptionHandling;

/// <summary>
/// Success still returns the command response as the body; failures return ProblemDetails.
/// <para>
/// These assertions changed deliberately. The mapper used to serialize the raw
/// <see cref="BaseCommandResponse{TKey}"/> on failure, which meant a caller had to know *which* endpoint it
/// hit before it could parse an error — some endpoints answered with ProblemDetails and some with a command
/// object. Every failure path in the API now answers in the one shape <c>[ApiController]</c> already promises.
/// </para>
/// </summary>
public sealed class CommandResponseResultMapperTests
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
    private const string PayloadTooLargeType = "https://tools.ietf.org/html/rfc9110#section-15.5.14";
    private const string UnprocessableEntityType = "https://tools.ietf.org/html/rfc9110#section-15.5.21";
    private const string TooManyRequestsType = "https://tools.ietf.org/html/rfc9110#section-15.5.30";
    private const string BadGatewayType = "https://tools.ietf.org/html/rfc9110#section-15.6.3";
    private const string ServiceUnavailableType = "https://tools.ietf.org/html/rfc9110#section-15.6.4";
    private const string QuotaExceededType = "/problems/quota_exceeded";

    [Test]
    public async Task MapCommandResponse_WhenSuccessful_ReturnsOkWithOriginalResponse()
    {
        var controller = CreateController();
        var response = BaseCommandResponse.Success("created-id", "Created");

        var result = controller.MapCommandResponse(response);

        await Assert.That(result).IsTypeOf<OkObjectResult>();
        await Assert.That(((OkObjectResult)result).Value).IsSameReferenceAs(response);
    }

    [Test]
    public async Task MapCommandResponse_WhenNotFound_ReturnsNotFoundProblemDetails()
    {
        var result = CreateController().MapCommandResponse(Failure(FailureCodes.NotFound));

        await AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Test]
    public async Task MapCommandResponse_WhenAdminRequired_ReturnsForbiddenProblemDetails()
    {
        var result = CreateController().MapCommandResponse(Failure(FailureCodes.AdminRequired));

        await AssertProblem(result, StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task MapCommandResponse_WhenAuthenticationRequired_ReturnsUnauthorizedProblemDetails()
    {
        var result = CreateController().MapCommandResponse(Failure(FailureCodes.AuthenticationRequired));

        await AssertProblem(result, StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task MapCommandResponse_WhenConcurrencyConflict_ReturnsConflictProblemDetails()
    {
        var result = CreateController().MapCommandResponse(Failure(FailureCodes.ConcurrencyConflict));

        await AssertProblem(result, StatusCodes.Status409Conflict);
    }

    [Test]
    public async Task ToEventReportProblem_WhenIntakeIsDisabled_ReturnsCanonicalConflictProblemDetails()
    {
        var controller = CreateController();
        var response = BaseCommandResponse.Failure<Guid>(
            "event_reporting_intake_disabled",
            "Event report intake is disabled for this tenant.");

        var result = controller.ToEventReportProblem(response);

        var objectResult = result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Type).IsEqualTo(ApiProblemTypes.Conflict);
        await Assert.That(problem.Title).IsEqualTo("Event report conflict");
        await Assert.That(problem.Detail).IsEqualTo("Event report intake is disabled for this tenant.");
        await Assert.That(problem.Extensions["code"]).IsEqualTo("event_reporting_intake_disabled");
    }

    /// <summary>
    /// An unrecognized, blank, or absent failure code falls through to a validation problem rather than being
    /// collapsed into an untyped 400 body, so a client can still read why the command failed.
    /// </summary>
    [Test]
    [Arguments("unexpected_failure")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(null)]
    public async Task MapCommandResponse_WhenFailureCodeIsNotMapped_ReturnsValidationProblemDetails(
        string? failureCode)
    {
        var result = CreateController().MapCommandResponse(Failure(failureCode));

        await AssertProblem(result, StatusCodes.Status400BadRequest);
    }

    [Test]
    [Arguments(FailureCodes.NotFound, 404, NotFoundType, "not_found", "handler-detail")]
    [Arguments(FailureCodes.AdminRequired, 403, ForbiddenType, "forbidden", "handler-detail")]
    [Arguments(FailureCodes.AuthenticationRequired, 401, UnauthorizedType, "authentication_required",
        "The authenticated principal does not include a supported user identifier claim.")]
    [Arguments(FailureCodes.ConcurrencyConflict, 409, ConflictType, "concurrency_conflict", "handler-detail")]
    public async Task MapCommandResponse_SharedFailures_PreserveMachineContract(
        string failureCode,
        int expectedStatus,
        string expectedType,
        string expectedCode,
        string expectedDetail)
    {
        var result = CreateController().MapCommandResponse(GuidFailure(failureCode, "handler-detail"));

        await AssertContractProblem(result, expectedStatus, expectedType, expectedCode, expectedDetail);
    }

    [Test]
    [Arguments("unexpected_failure", "unexpected_failure")]
    [Arguments("", "validation_failed")]
    [Arguments("   ", "validation_failed")]
    [Arguments(null, "validation_failed")]
    public async Task MapCommandResponse_ValidationFallback_PreservesCodeAndErrorShape(
        string? failureCode,
        string expectedCode)
    {
        var result = CreateController().MapCommandResponse(GuidFailure(failureCode, "handler-detail"));

        var problem = await AssertContractProblem(
            result,
            400,
            BadRequestType,
            expectedCode,
            "handler-detail");
        await AssertValidationErrors(problem, "command", "handler-detail");
    }

    [Test]
    public async Task MapCommandResponse_WhenMessageAndErrorsAreMissing_UsesDeclaredFallbackForBoth()
    {
        var result = CreateController().MapCommandResponse(GuidFailure("unmapped", message: null));

        var problem = await AssertContractProblem(
            result,
            400,
            BadRequestType,
            "unmapped",
            "The command could not be completed.");
        await AssertValidationErrors(problem, "command", "The command could not be completed.");
    }

    [Test]
    public async Task MapCommandResponse_WhenErrorsExist_KeepsErrorsSeparateFromFallbackDetail()
    {
        var response = GuidFailure(
            "unmapped",
            message: null,
            errors: ["first-error", "second-error"]);

        var problem = await AssertContractProblem(
            CreateController().MapCommandResponse(response),
            400,
            BadRequestType,
            "unmapped",
            "The command could not be completed.");
        await AssertValidationErrors(problem, "command", "first-error", "second-error");
    }

    [Test]
    [Arguments("unmapped", 400, BadRequestType)]
    [Arguments(FailureCodes.ConcurrencyConflict, 409, ConflictType)]
    public async Task MapCommandResponse_WhenFailureHasId_AddsResponseExtensions(
        string failureCode,
        int expectedStatus,
        string expectedType)
    {
        var response = StringFailure(failureCode, "response-message", id: "response-id");

        var problem = await AssertContractProblem(
            CreateController().MapCommandResponse(response),
            expectedStatus,
            expectedType,
            failureCode,
            "response-message");

        await Assert.That(problem.Extensions["id"]).IsEqualTo("response-id");
        await Assert.That(problem.Extensions["success"]).IsEqualTo(false);
        await Assert.That(problem.Extensions["message"]).IsEqualTo("response-message");
    }

    [Test]
    [Arguments(FailureCodes.StorageUploadSizeMismatch, 400, BadRequestType, "storage_upload_size_mismatch",
        "Upload content length does not match the reserved byte count.")]
    [Arguments(FailureCodes.StorageUploadContentTypeMismatch, 400, BadRequestType,
        "storage_upload_content_type_mismatch", "Upload content type does not match the reserved content type.")]
    [Arguments(FailureCodes.StorageUploadContentSignatureMismatch, 400, BadRequestType,
        "storage_upload_content_signature_mismatch", "Upload content did not match the reserved content policy.")]
    [Arguments(FailureCodes.StorageUploadSessionNotFound, 404, NotFoundType, "storage_upload_session_not_found",
        "Upload session was not found.")]
    [Arguments(FailureCodes.StorageUploadSessionFinalized, 409, ConflictType, "storage_upload_session_finalized",
        "Finalized upload sessions cannot be canceled.")]
    [Arguments(FailureCodes.StorageUploadSessionExpired, 409, ConflictType, "storage_upload_session_expired",
        "Upload session has expired.")]
    [Arguments(FailureCodes.StorageUploadSessionInvalidState, 409, ConflictType,
        "storage_upload_session_invalid_state", "Upload session cannot accept bytes in its current state.")]
    [Arguments(FailureCodes.StorageUploadTooLarge, 413, PayloadTooLargeType, "storage_upload_too_large",
        "Upload exceeds the configured per-file limit.")]
    [Arguments(FailureCodes.QuotaExceeded, 422, QuotaExceededType, "quota_exceeded",
        "Storage quota has been exceeded.")]
    [Arguments(FailureCodes.StorageUploadWriteFailed, 503, ServiceUnavailableType, "storage_upload_write_failed",
        "Storage provider returned invalid upload metadata.")]
    public async Task ToStorageUploadProblem_MapsEveryStatusBranchAndCanonicalDetail(
        string failureCode,
        int expectedStatus,
        string expectedType,
        string expectedCode,
        string expectedDetail)
    {
        var response = StorageFailure(failureCode, "provider-internal-detail");

        await AssertContractProblem(
            CreateController().ToStorageUploadProblem(response),
            expectedStatus,
            expectedType,
            expectedCode,
            expectedDetail);
    }

    [Test]
    [Arguments(EventReportFailureCodes.UserUnresolved, 401, UnauthorizedType, "event_report_user_unresolved")]
    [Arguments(EventReportFailureCodes.ReporterActorUnresolved, 403, ForbiddenType, "event_report_actor_unresolved")]
    [Arguments(EventReportFailureCodes.ModeratorUnavailable, 403, ForbiddenType,
        "event_report_moderator_unavailable")]
    [Arguments(EventReportFailureCodes.AssigneeUnavailable, 403, ForbiddenType,
        "event_report_assignee_unavailable")]
    [Arguments(EventReportFailureCodes.EventNotFound, 404, NotFoundType, "event_report_event_not_found")]
    [Arguments(EventReportFailureCodes.ReportNotFound, 404, NotFoundType, "event_report_not_found")]
    [Arguments(EventReportFailureCodes.CaseNotFound, 404, NotFoundType, "event_report_case_not_found")]
    [Arguments(EventReportFailureCodes.DecisionNotFound, 404, NotFoundType, "event_report_decision_not_found")]
    [Arguments(EventReportFailureCodes.EventMismatch, 404, NotFoundType, "event_report_event_mismatch")]
    [Arguments(EventReportFailureCodes.Duplicate, 409, ConflictType, "event_report_duplicate")]
    [Arguments(EventReportFailureCodes.EventInvalidStatus, 409, ConflictType,
        "event_report_event_invalid_status")]
    [Arguments(EventReportFailureCodes.CaseConcurrencyConflict, 409, ConflictType,
        "event_report_case_concurrency_conflict")]
    [Arguments(EventReportFailureCodes.CaseInvalidStatus, 409, ConflictType,
        "event_report_case_invalid_status")]
    [Arguments(EventReportFailureCodes.ReportInvalidStatus, 409, ConflictType, "event_report_invalid_status")]
    [Arguments(EventReportFailureCodes.AssignmentMismatch, 409, ConflictType,
        "event_report_assignment_mismatch")]
    [Arguments(EventReportFailureCodes.DecisionInvalid, 409, ConflictType, "event_report_decision_invalid")]
    [Arguments(FailureCodes.QuotaExceeded, 422, QuotaExceededType, "quota_exceeded")]
    [Arguments(EventReportFailureCodes.DecisionExecutionFailed, 503, BadRequestType,
        "event_report_decision_execution_failed")]
    public async Task ToEventReportProblem_MapsEveryStatusBranchAndResponseExtensions(
        string failureCode,
        int expectedStatus,
        string expectedType,
        string expectedCode)
    {
        var response = GuidFailure(
            failureCode,
            "event-report-detail",
            id: Guid.Parse("018f3f7d-8b43-7b5a-8f58-a1848c51d214"));

        var problem = await AssertContractProblem(
            CreateController().ToEventReportProblem(response),
            expectedStatus,
            expectedType,
            expectedCode,
            "event-report-detail");

        await Assert.That(problem.Extensions["id"]).IsEqualTo(response.Id);
        await Assert.That(problem.Extensions["success"]).IsEqualTo(false);
        await Assert.That(problem.Extensions["message"]).IsEqualTo("event-report-detail");
    }

    [Test]
    [Arguments(KeycloakFailureCodes.BootstrapValidationFailed, 400, BadRequestType,
        "keycloak_bootstrap_validation_failed")]
    [Arguments(KeycloakFailureCodes.InvalidResponse, 502, BadGatewayType, "keycloak_invalid_response")]
    [Arguments(KeycloakFailureCodes.RealmCheckFailed, 502, BadGatewayType, "keycloak_realm_check_failed")]
    [Arguments(KeycloakFailureCodes.RealmCreateFailed, 502, BadGatewayType, "keycloak_realm_create_failed")]
    [Arguments(KeycloakFailureCodes.ClientLookupFailed, 502, BadGatewayType, "keycloak_client_lookup_failed")]
    [Arguments(KeycloakFailureCodes.ClientCreateFailed, 502, BadGatewayType, "keycloak_client_create_failed")]
    [Arguments(KeycloakFailureCodes.ClientSecretUpdateFailed, 502, BadGatewayType,
        "keycloak_client_secret_update_failed")]
    [Arguments(KeycloakFailureCodes.OfflineAccessRoleUpdateFailed, 502, BadGatewayType,
        "keycloak_offline_access_role_update_failed")]
    [Arguments(KeycloakFailureCodes.ClientScopeUpdateFailed, 502, BadGatewayType,
        "keycloak_client_scope_update_failed")]
    [Arguments(KeycloakFailureCodes.OfflineAccessScopeMappingFailed, 502, BadGatewayType,
        "keycloak_offline_access_scope_mapping_failed")]
    [Arguments(KeycloakFailureCodes.Timeout, 503, ServiceUnavailableType, "keycloak_timeout")]
    [Arguments(KeycloakFailureCodes.Unreachable, 503, ServiceUnavailableType, "keycloak_unreachable")]
    public async Task ToAuthProviderProblem_MapsEveryStatusBranch(
        string failureCode,
        int expectedStatus,
        string expectedType,
        string expectedCode)
    {
        await AssertContractProblem(
            CreateController().ToAuthProviderProblem(GuidFailure(failureCode, "auth-provider-detail")),
            expectedStatus,
            expectedType,
            expectedCode,
            "auth-provider-detail");
    }

    [Test]
    [Arguments(EmailDispatchFailureCodes.ValidationFailed, 400, BadRequestType,
        "email_dispatch_validation_failed")]
    [Arguments(EmailDispatchFailureCodes.NotFound, 404, NotFoundType, "email_dispatch_not_found")]
    [Arguments(EmailDispatchFailureCodes.InvalidTransition, 409, ConflictType,
        "email_dispatch_invalid_transition")]
    [Arguments(EmailDispatchFailureCodes.ConcurrentTransition, 409, ConflictType,
        "email_dispatch_concurrent_transition")]
    [Arguments(EmailDispatchFailureCodes.Misconfigured, 503, ServiceUnavailableType,
        "email_dispatch_misconfigured")]
    public async Task ToEmailDispatchProblem_MapsEveryStatusBranch(
        string failureCode,
        int expectedStatus,
        string expectedType,
        string expectedCode)
    {
        await AssertContractProblem(
            CreateController().ToEmailDispatchProblem(GuidFailure(failureCode, "email-dispatch-detail")),
            expectedStatus,
            expectedType,
            expectedCode,
            "email-dispatch-detail");
    }

    [Test]
    [Arguments("validation_failed", 400, BadRequestType, "validation_failed", "first-error")]
    [Arguments("unauthenticated", 401, UnauthorizedType, "unauthenticated", "ai-response-detail")]
    [Arguments("disabled", 403, ForbiddenType, "disabled", "ai-response-detail")]
    [Arguments("provider_not_configured", 403, ForbiddenType, "provider_not_configured", "ai-response-detail")]
    [Arguments("provider_unsupported", 403, ForbiddenType, "provider_unsupported", "ai-response-detail")]
    [Arguments("endpoint_not_configured", 403, ForbiddenType, "endpoint_not_configured", "ai-response-detail")]
    [Arguments("api_key_not_configured", 403, ForbiddenType, "api_key_not_configured", "ai-response-detail")]
    [Arguments("model_not_configured", 403, ForbiddenType, "model_not_configured", "ai-response-detail")]
    [Arguments("conversation_not_found", 404, NotFoundType, "conversation_not_found", "ai-response-detail")]
    [Arguments("run_not_found", 404, NotFoundType, "run_not_found", "ai-response-detail")]
    [Arguments("proposed_action_not_found", 404, NotFoundType, "proposed_action_not_found", "ai-response-detail")]
    [Arguments("conversation_not_active", 409, ConflictType, "conversation_not_active", "ai-response-detail")]
    [Arguments("run_not_cancellable", 409, ConflictType, "run_not_cancellable", "ai-response-detail")]
    [Arguments("proposed_action_rejected", 409, ConflictType, "proposed_action_rejected", "ai-response-detail")]
    [Arguments("proposed_action_failed", 409, ConflictType, "proposed_action_failed", "ai-response-detail")]
    [Arguments("invalid_proposed_action_state", 409, ConflictType, "invalid_proposed_action_state",
        "ai-response-detail")]
    [Arguments("idempotency_key_conflict", 409, ConflictType, "idempotency_key_conflict", "ai-response-detail")]
    [Arguments("idempotency_replay_failed", 409, ConflictType, "idempotency_replay_failed", "ai-response-detail")]
    [Arguments(FailureCodes.QuotaExceeded, 422, QuotaExceededType, "quota_exceeded", "ai-response-detail")]
    [Arguments("http_429", 429, TooManyRequestsType, "http_429", "ai-response-detail")]
    [Arguments("provider_not_ready", 503, ServiceUnavailableType, "provider_not_ready", "first-error")]
    [Arguments("provider_timeout", 503, ServiceUnavailableType, "provider_timeout", "first-error")]
    [Arguments("provider_unreachable", 503, ServiceUnavailableType, "provider_unreachable", "first-error")]
    public async Task ToAiAssistantProblem_MapsEveryStatusBranchAndDetailSource(
        string failureCode,
        int expectedStatus,
        string expectedType,
        string expectedCode,
        string expectedDetail)
    {
        var response = GuidFailure(
            failureCode,
            "ai-response-detail",
            errors: [" first-error ", "second-error"]);

        await AssertContractProblem(
            CreateController().ToAiAssistantProblem(response),
            expectedStatus,
            expectedType,
            expectedCode,
            expectedDetail);
    }

    [Test]
    [Arguments("event-report")]
    [Arguments("ai-assistant")]
    public async Task FeatureMapper_WhenQuotaMetadataExists_AddsQuotaExtension(string mapper)
    {
        var quota = new QuotaExceededDetails(
            "quota.key",
            Limit: 7,
            Actual: 6,
            Attempted: 8,
            Scope: "quota-scope",
            TenantId: Guid.Parse("018f3f7d-8b43-7b5a-8f58-a1848c51d215"));
        var response = GuidFailure(
            FailureCodes.QuotaExceeded,
            "quota-detail",
            quotaExceeded: quota);

        var controller = CreateController();
        var result = mapper == "event-report"
            ? controller.ToEventReportProblem(response)
            : controller.ToAiAssistantProblem(response);

        var problem = await AssertContractProblem(result, 422, QuotaExceededType, "quota_exceeded", "quota-detail");
        await Assert.That(problem.Extensions["quota"]).IsSameReferenceAs(quota);
        await Assert.That(quota.QuotaKey).IsEqualTo("quota.key");
        await Assert.That(quota.Limit).IsEqualTo(7);
        await Assert.That(quota.Actual).IsEqualTo(6);
        await Assert.That(quota.Attempted).IsEqualTo(8);
        await Assert.That(quota.Scope).IsEqualTo("quota-scope");
    }

    [Test]
    [Arguments("generic", "The command could not be completed.")]
    [Arguments("storage", "Storage upload command failed.")]
    [Arguments("event-report", "Event report submission failed.")]
    [Arguments("auth-provider", "Instance auth-provider configuration update failed.")]
    [Arguments("email-dispatch", "Email dispatch command failed.")]
    [Arguments("ai-assistant", "AI assistant request failed.")]
    public async Task FeatureMapper_WhenMessageIsMissing_UsesDeclaredFallbackDetail(
        string mapper,
        string expectedDetail)
    {
        const string unmappedCode = "unmapped_failure";
        var controller = CreateController();
        ActionResult result = mapper switch
        {
            "generic" => controller.MapCommandResponse(StringFailure(unmappedCode, message: null)),
            "storage" => controller.ToStorageUploadProblem(StorageFailure(unmappedCode, message: null)),
            "event-report" => controller.ToEventReportProblem(GuidFailure(unmappedCode, message: null)),
            "auth-provider" => controller.ToAuthProviderProblem(GuidFailure(unmappedCode, message: null)),
            "email-dispatch" => controller.ToEmailDispatchProblem(GuidFailure(unmappedCode, message: null)),
            "ai-assistant" => controller.ToAiAssistantProblem(GuidFailure(unmappedCode, message: null)),
            _ => throw new InvalidOperationException($"Unknown mapper '{mapper}'.")
        };

        await AssertContractProblem(result, 400, BadRequestType, unmappedCode, expectedDetail);
    }

    private static async Task<ProblemDetails> AssertContractProblem(
        ActionResult result,
        int expectedStatus,
        string expectedType,
        string expectedCode,
        string expectedDetail)
    {
        var objectResult = result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(expectedStatus);

        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Status).IsEqualTo(expectedStatus);
        await Assert.That(problem.Type).IsEqualTo(expectedType);
        await Assert.That(problem.Detail).IsEqualTo(expectedDetail);
        await Assert.That(problem.Extensions["code"]).IsEqualTo(expectedCode);
        return problem;
    }

    private static async Task AssertValidationErrors(
        ProblemDetails problem,
        string expectedKey,
        params string[] expectedErrors)
    {
        var validationProblem = problem as ValidationProblemDetails;
        await Assert.That(validationProblem).IsNotNull();
        await Assert.That(validationProblem!.Errors.ContainsKey(expectedKey)).IsTrue();
        await Assert.That(validationProblem.Errors[expectedKey].Length).IsEqualTo(expectedErrors.Length);

        for (var index = 0; index < expectedErrors.Length; index++)
        {
            await Assert.That(validationProblem.Errors[expectedKey][index]).IsEqualTo(expectedErrors[index]);
        }
    }

    private static async Task AssertProblem(ActionResult result, int expectedStatusCode)
    {
        var objectResult = result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(expectedStatusCode);

        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Status).IsEqualTo(expectedStatusCode);
    }

    private static TestController CreateController() => new()
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        }
    };

    private static BaseCommandResponse<Guid> Failure(string? failureCode) =>
        CreateFailure<Guid>(failureCode, "Command failed");

    private static BaseCommandResponse<Guid> GuidFailure(
        string? failureCode,
        string? message,
        IEnumerable<string>? errors = null,
        Guid id = default,
        QuotaExceededDetails? quotaExceeded = null) =>
        CreateFailure(failureCode, message, errors, id, quotaExceeded);

    private static BaseCommandResponse<string> StringFailure(
        string? failureCode,
        string? message,
        IEnumerable<string>? errors = null,
        string? id = null,
        QuotaExceededDetails? quotaExceeded = null) =>
        CreateFailure(failureCode, message, errors, id, quotaExceeded);

    private static BaseCommandResponse<StorageUploadSessionDto> StorageFailure(
        string? failureCode,
        string? message,
        IEnumerable<string>? errors = null,
        StorageUploadSessionDto? id = null,
        QuotaExceededDetails? quotaExceeded = null) =>
        CreateFailure(failureCode, message, errors, id, quotaExceeded);

    private static BaseCommandResponse<TKey> CreateFailure<TKey>(
        string? failureCode,
        string? message,
        IEnumerable<string>? errors = null,
        TKey? id = default,
        QuotaExceededDetails? quotaExceeded = null)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            var detail = message ?? "Command failed";
            return BaseCommandResponse.Validation(errors ?? [detail], detail, id);
        }

        return failureCode switch
        {
            FailureCodes.NotFound => BaseCommandResponse.NotFound(message, id),
            FailureCodes.ConcurrencyConflict => BaseCommandResponse.Conflict(id!, message),
            FailureCodes.AdminRequired => BaseCommandResponse.Authorization<TKey>(message),
            FailureCodes.AuthenticationRequired => BaseCommandResponse.Authentication<TKey>(message),
            FailureCodes.QuotaExceeded => BaseCommandResponse.Quota(
                message ?? "Command failed",
                quotaExceeded ?? CreateQuotaDetails(),
                id: id),
            _ => BaseCommandResponse.Failure(failureCode, message, errors, id)
        };
    }

    private static QuotaExceededDetails CreateQuotaDetails() => new(
        "mapper-test.quota",
        Limit: 1,
        Actual: 1,
        Attempted: 2,
        Scope: "mapper-test");

    private sealed class TestController : ControllerBase;
}
