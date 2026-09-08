namespace Explore.API.Controllers;

using System.Security.Cryptography;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Features.SetupLive;
using Explore.Application.Hateoas;
using Explore.Application.Telemetry;
using ISLAMU.Wire.Contracts.SetupLive;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MediatR;

[ApiController]
[ApiVersion("0.1")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[PrivateNoStore]
[Route("api/tenants/{tenantId:guid}/setup/enrollments")]
[Tags("Setup Live")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
public sealed class SetupTargetEnrollmentsController(
    SetupLiveApplicationService setupLive,
    IMediator mediator,
    SetupLiveTelemetry telemetry) : EventControllerBase
{
    [HttpPost(Name = RouteNames.CreateSetupTargetEnrollment)]
    [Consumes(SetupLiveContractMetadata.CreateRequestMediaType)]
    [EnableRateLimiting(SetupLiveContractMetadata.EnrollmentWriteRatePolicy)]
    [RequestTimeout(SetupLiveContractMetadata.EnrollmentTimeoutPolicy)]
    [RequestSizeLimit(SetupLiveContentLimits.MaximumCreateRequestBytes)]
    [SuppressIdempotencyResponseStorage]
    [ProducesResponseType(typeof(HalResource<SetupTargetEnrollmentData>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(HalResource<SetupTargetEnrollmentData>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateSetupTargetEnrollment(
        Guid tenantId,
        [FromHeader(Name = SetupLiveContractMetadata.IdempotencyHeader)] string? idempotencyKey,
        [FromBody] CreateSetupTargetEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        using SetupLiveTelemetry.Operation telemetryOperation = telemetry.Start(
            "enrollment.create",
            Request.ContentLength);
        if (!TryParseOperationKey(idempotencyKey, out Guid operationKey))
            return InvalidRequest();

        Guid? userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return UnresolvablePrincipal();
        SetupLiveEnrollmentResult result = await setupLive.CreateAsync(
            tenantId,
            userId.Value,
            operationKey,
            request,
            cancellationToken);
        if (result.Capability is not null)
        {
            Response.Headers.Append(
                SetupLiveContractMetadata.CapabilityHeader,
                result.Capability.ToHeaderValue());
        }

        IActionResult response = result.Status switch
        {
            SetupLiveApplicationStatus.Created =>
                Enrollment(result.Data!, StatusCodes.Status201Created, result.CanMutate),
            SetupLiveApplicationStatus.Duplicate =>
                Enrollment(result.Data!, StatusCodes.Status200OK, result.CanMutate),
            SetupLiveApplicationStatus.Conflict => IdempotencyConflict(),
            SetupLiveApplicationStatus.Forbidden => Forbidden(),
            SetupLiveApplicationStatus.Invalid => InvalidRequest(),
            _ => Unavailable()
        };
        telemetryOperation.Complete(Outcome(result.Status));
        return response;
    }

    [HttpGet("{enrollmentId:guid}", Name = RouteNames.GetSetupTargetEnrollment)]
    [ProducesResponseType(typeof(HalResource<SetupTargetEnrollmentData>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSetupTargetEnrollment(
        Guid tenantId,
        Guid enrollmentId,
        [FromHeader(Name = SetupLiveContractMetadata.CapabilityHeader)] string? capability,
        CancellationToken cancellationToken)
    {
        using SetupLiveTelemetry.Operation telemetryOperation = telemetry.Start(
            "enrollment.read");
        Guid? userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return UnresolvablePrincipal();
        SetupLiveEnrollmentResult result = await setupLive.GetAsync(
            tenantId,
            enrollmentId,
            userId.Value,
            capability,
            cancellationToken);
        IActionResult response = result.Status == SetupLiveApplicationStatus.Success
            ? Enrollment(result.Data!, StatusCodes.Status200OK, result.CanMutate)
            : Unavailable();
        telemetryOperation.Complete(Outcome(result.Status));
        return response;
    }

    [HttpDelete("{enrollmentId:guid}", Name = RouteNames.RevokeSetupTargetEnrollment)]
    [EnableRateLimiting(SetupLiveContractMetadata.EnrollmentWriteRatePolicy)]
    [RequestTimeout(SetupLiveContractMetadata.EnrollmentTimeoutPolicy)]
    [SuppressIdempotencyResponseStorage]
    [ProducesResponseType(typeof(HalResource<SetupTargetEnrollmentData>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeSetupTargetEnrollment(
        Guid tenantId,
        Guid enrollmentId,
        [FromHeader(Name = SetupLiveContractMetadata.CapabilityHeader)] string? capability,
        [FromHeader(Name = SetupLiveContractMetadata.IdempotencyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using SetupLiveTelemetry.Operation telemetryOperation = telemetry.Start(
            "enrollment.revoke");
        if (!TryParseOperationKey(idempotencyKey, out Guid operationKey))
            return InvalidRequest();
        Guid? userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return UnresolvablePrincipal();
        SetupLiveEnrollmentResult result = await setupLive.RevokeAsync(
            tenantId,
            enrollmentId,
            userId.Value,
            operationKey,
            capability,
            cancellationToken);
        IActionResult response = result.Status switch
        {
            SetupLiveApplicationStatus.Success =>
                Enrollment(result.Data!, StatusCodes.Status200OK, result.CanMutate),
            SetupLiveApplicationStatus.Conflict => IdempotencyConflict(),
            _ => Unavailable()
        };
        telemetryOperation.Complete(Outcome(result.Status));
        return response;
    }

    [HttpPost(
        "{enrollmentId:guid}/capability-rotations",
        Name = RouteNames.RotateSetupTargetEnrollmentCapability)]
    [EnableRateLimiting(SetupLiveContractMetadata.EnrollmentWriteRatePolicy)]
    [RequestTimeout(SetupLiveContractMetadata.EnrollmentTimeoutPolicy)]
    [SuppressIdempotencyResponseStorage]
    [ProducesResponseType(typeof(HalResource<SetupTargetEnrollmentData>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RotateSetupTargetEnrollmentCapability(
        Guid tenantId,
        Guid enrollmentId,
        [FromHeader(Name = SetupLiveContractMetadata.CapabilityHeader)] string? capability,
        [FromHeader(Name = SetupLiveContractMetadata.IdempotencyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using SetupLiveTelemetry.Operation telemetryOperation = telemetry.Start(
            "enrollment.rotate");
        if (!TryParseOperationKey(idempotencyKey, out Guid operationKey))
            return InvalidRequest();
        Guid? userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return UnresolvablePrincipal();
        SetupLiveEnrollmentResult result = await setupLive.RotateAsync(
            tenantId,
            enrollmentId,
            userId.Value,
            operationKey,
            capability,
            cancellationToken);
        if (result.Capability is not null)
        {
            Response.Headers.Append(
                SetupLiveContractMetadata.CapabilityHeader,
                result.Capability.ToHeaderValue());
        }

        IActionResult response = result.Status switch
        {
            SetupLiveApplicationStatus.Success or
                SetupLiveApplicationStatus.Duplicate =>
                Enrollment(result.Data!, StatusCodes.Status200OK, result.CanMutate),
            SetupLiveApplicationStatus.Conflict => IdempotencyConflict(),
            _ => Unavailable()
        };
        telemetryOperation.Complete(Outcome(result.Status));
        return response;
    }

    [HttpGet(
        "{enrollmentId:guid}/secret-bindings/readiness",
        Name = RouteNames.GetSetupSecretBindingReadiness)]
    [ProducesResponseType(typeof(HalResource<SetupSecretBindingReadinessDocument>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSetupSecretBindingReadiness(
        Guid tenantId,
        Guid enrollmentId,
        [FromHeader(Name = SetupLiveContractMetadata.CapabilityHeader)] string? capability,
        CancellationToken cancellationToken)
    {
        using SetupLiveTelemetry.Operation telemetryOperation = telemetry.Start(
            "secret_binding.readiness");
        Guid? userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return UnresolvablePrincipal();
        SetupLiveReadinessResult result = await setupLive.ReadinessAsync(
            tenantId,
            enrollmentId,
            userId.Value,
            capability,
            cancellationToken);
        if (result.Status != SetupLiveApplicationStatus.Success)
            return Unavailable();

        HalResource<SetupSecretBindingReadinessItem>[] items = result.Items!
            .Select(item =>
            {
                var resource = new HalResource<SetupSecretBindingReadinessItem>(item);
                if (result.CanWrite
                    && item.State == SetupSecretBindingReadinessState.Ready)
                {
                    resource.WithLink(
                        SetupLiveHalRelations.WriteSecretBinding,
                        HalLink.CreateAction(
                            SecretWritePath(tenantId, enrollmentId, item.BindingKey),
                            HttpMethods.Put));
                }
                return resource;
            })
            .ToArray();
        var resource = new HalResource<SetupSecretBindingReadinessDocument>(new())
            .WithLink(
                SetupLiveHalRelations.Self,
                HalLink.Create(ReadinessPath(tenantId, enrollmentId)))
            .WithEmbedded("items", items);
        telemetryOperation.Complete("succeeded");
        return Hal(resource, StatusCodes.Status200OK);
    }

    [HttpPut(
        "{enrollmentId:guid}/secret-bindings/{bindingKey}",
        Name = RouteNames.WriteSetupSecretBinding)]
    [Consumes(SetupLiveContractMetadata.SecretWriteRequestMediaType)]
    [EnableRateLimiting(SetupLiveContractMetadata.SecretWriteRatePolicy)]
    [RequestTimeout(SetupLiveContractMetadata.SecretWriteTimeoutPolicy)]
    [RequestSizeLimit(SetupLiveContentLimits.MaximumSecretWriteBytes)]
    [SuppressIdempotencyResponseStorage]
    [ProducesResponseType(typeof(HalResource<SetupSecretBindingOperationData>), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> WriteSetupSecretBinding(
        Guid tenantId,
        Guid enrollmentId,
        string bindingKey,
        [FromHeader(Name = SetupLiveContractMetadata.CapabilityHeader)] string? capability,
        [FromHeader(Name = SetupLiveContractMetadata.IdempotencyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using SetupLiveTelemetry.Operation telemetryOperation = telemetry.Start(
            "secret_binding.write",
            Request.ContentLength);
        if (!TryParseOperationKey(idempotencyKey, out Guid operationKey))
            return InvalidRequest();
        Guid? userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return UnresolvablePrincipal();
        SetupLiveApplicationStatus validation = await setupLive.ValidateSecretWriteAsync(
            tenantId,
            enrollmentId,
            userId.Value,
            capability,
            bindingKey,
            cancellationToken);
        if (validation != SetupLiveApplicationStatus.Success)
            return Unavailable();
        byte[] secret = GC.AllocateUninitializedArray<byte>(
            SetupLiveContentLimits.MaximumSecretWriteBytes + 1);
        try
        {
            int length = 0;
            while (length < secret.Length)
            {
                int read = await Request.Body.ReadAsync(
                    secret.AsMemory(length),
                    cancellationToken);
                if (read == 0)
                    break;
                length += read;
            }
            if (length is < 1 or > SetupLiveContentLimits.MaximumSecretWriteBytes)
                return InvalidRequest();

            SetupLiveSecretBindingResult result =
                await setupLive.WriteSecretBindingAsync(
                    tenantId,
                    enrollmentId,
                    userId.Value,
                    capability,
                    operationKey,
                    bindingKey,
                    secret.AsMemory(0, length),
                    cancellationToken);
            IActionResult response = result.Status switch
            {
                SetupLiveApplicationStatus.Success or
                    SetupLiveApplicationStatus.Duplicate =>
                    SecretBindingOperation(
                        tenantId,
                        enrollmentId,
                        result.Data!,
                        StatusCodes.Status202Accepted),
                SetupLiveApplicationStatus.Conflict => IdempotencyConflict(),
                _ => Unavailable()
            };
            telemetryOperation.Complete(Outcome(result.Status));
            return response;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [HttpGet(
        "{enrollmentId:guid}/secret-binding-operations/{operationId:guid}",
        Name = RouteNames.GetSetupSecretBindingOperation)]
    [ProducesResponseType(typeof(HalResource<SetupSecretBindingOperationData>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSetupSecretBindingOperation(
        Guid tenantId,
        Guid enrollmentId,
        Guid operationId,
        [FromHeader(Name = SetupLiveContractMetadata.CapabilityHeader)] string? capability,
        CancellationToken cancellationToken)
    {
        using SetupLiveTelemetry.Operation telemetryOperation = telemetry.Start(
            "secret_binding.operation.read");
        Guid? userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return UnresolvablePrincipal();
        SetupLiveSecretBindingResult result =
            await setupLive.GetSecretBindingOperationAsync(
            tenantId,
            enrollmentId,
            operationId,
            userId.Value,
            capability,
            cancellationToken);
        IActionResult response = result.Status == SetupLiveApplicationStatus.Success
            ? SecretBindingOperation(
                tenantId,
                enrollmentId,
                result.Data!,
                StatusCodes.Status200OK)
            : Unavailable();
        telemetryOperation.Complete(Outcome(result.Status));
        return response;
    }

    private IActionResult Enrollment(
        SetupTargetEnrollmentData data,
        int statusCode,
        bool canMutate)
    {
        string enrollmentPath = EnrollmentPath(data.EnrollmentId);
        var resource = new HalResource<SetupTargetEnrollmentData>(data)
            .WithLink(SetupLiveHalRelations.Self, HalLink.Create(enrollmentPath));
        if (data.State == ISLAMU.Wire.Contracts.SetupLive.SetupEnrollmentState.Active)
        {
            if (canMutate
                && data.Scopes.Contains(SetupEnrollmentScope.TargetRead))
            {
                resource.WithLink(
                    SetupLiveHalRelations.Revoke,
                    HalLink.CreateAction(enrollmentPath, HttpMethods.Delete))
                    .WithLink(
                    SetupLiveHalRelations.RotateCapability,
                    HalLink.CreateAction(
                        $"{enrollmentPath}/capability-rotations",
                        HttpMethods.Post));
            }
            if (data.Scopes.Contains(SetupEnrollmentScope.SecretBindingReadiness))
            {
                resource.WithLink(
                    SetupLiveHalRelations.SecretBindingReadiness,
                    HalLink.Create(ReadinessPath(data.EnrollmentId)));
            }
        }

        return Hal(resource, statusCode);
    }

    private ObjectResult Hal(object value, int statusCode) => new(value)
    {
        StatusCode = statusCode,
        ContentTypes = { SetupLiveContractMetadata.SuccessMediaType }
    };

    private IActionResult SecretBindingOperation(
        Guid tenantId,
        Guid enrollmentId,
        SetupSecretBindingOperationData data,
        int statusCode)
    {
        string path = OperationPath(
            tenantId,
            enrollmentId,
            data.OperationId);
        var resource = new HalResource<SetupSecretBindingOperationData>(data)
            .WithLink(SetupLiveHalRelations.Self, HalLink.Create(path))
            .WithLink(
                SetupLiveHalRelations.SecretBindingOperation,
                HalLink.Create(path));
        return Hal(resource, statusCode);
    }

    private ObjectResult Unavailable() => Problem(
        SetupLiveProblemContracts.UnavailableStatus,
        SetupLiveProblemContracts.UnavailableTitle,
        SetupLiveProblemContracts.UnavailableType,
        SetupLiveProblemContracts.UnavailableDetail,
        SetupLiveProblemContracts.UnavailableCode);

    private ObjectResult IdempotencyConflict() => Problem(
        SetupLiveProblemContracts.IdempotencyConflictStatus,
        SetupLiveProblemContracts.IdempotencyConflictTitle,
        SetupLiveProblemContracts.IdempotencyConflictType,
        SetupLiveProblemContracts.IdempotencyConflictDetail,
        SetupLiveProblemContracts.IdempotencyConflictCode);

    private ObjectResult InvalidRequest() => Problem(
        StatusCodes.Status400BadRequest,
        "Setup enrollment request invalid",
        "/problems/setup-enrollment-invalid",
        "The setup enrollment request is invalid.",
        "setup_enrollment_invalid");

    private ObjectResult Forbidden() => Problem(
        StatusCodes.Status403Forbidden,
        "Setup enrollment forbidden",
        "/problems/setup-enrollment-forbidden",
        "The current identity cannot create this setup enrollment.",
        "setup_enrollment_forbidden");

    private IActionResult UnresolvablePrincipal() =>
        this.ToAuthenticationRequiredProblem(
            detail: "The authenticated principal could not be resolved to an application user.");

    private ObjectResult Problem(
        int status,
        string title,
        string type,
        string detail,
        string code) => ApiProblemFactory.ToProblemResult(
        ApiProblemFactory.CreateProblem(
            HttpContext,
            status,
            title,
            type,
            detail,
            code));

    private string EnrollmentPath(Guid enrollmentId) =>
        $"/api/tenants/{RouteData.Values["tenantId"]}/setup/enrollments/{enrollmentId:D}";

    private static string ReadinessPath(Guid tenantId, Guid enrollmentId) =>
        $"/api/tenants/{tenantId:D}/setup/enrollments/{enrollmentId:D}/secret-bindings/readiness";

    private static string SecretWritePath(
        Guid tenantId,
        Guid enrollmentId,
        string bindingKey) =>
        $"/api/tenants/{tenantId:D}/setup/enrollments/{enrollmentId:D}/secret-bindings/{Uri.EscapeDataString(bindingKey)}";

    private static string OperationPath(
        Guid tenantId,
        Guid enrollmentId,
        Guid operationId) =>
        $"/api/tenants/{tenantId:D}/setup/enrollments/{enrollmentId:D}/secret-binding-operations/{operationId:D}";

    private string ReadinessPath(Guid enrollmentId) =>
        $"{EnrollmentPath(enrollmentId)}/secret-bindings/readiness";

    private static bool TryParseOperationKey(string? value, out Guid operationKey) =>
        Guid.TryParseExact(value, "D", out operationKey)
        && operationKey.Version == 7
        && HasRfcVariant(operationKey);

    private static bool HasRfcVariant(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        return value.TryWriteBytes(bytes, bigEndian: true, out int written)
            && written == bytes.Length
            && (bytes[8] & 0b1100_0000) == 0b1000_0000;
    }

    private static string Outcome(SetupLiveApplicationStatus status) => status switch
    {
        SetupLiveApplicationStatus.Created => "created",
        SetupLiveApplicationStatus.Duplicate => "duplicate",
        SetupLiveApplicationStatus.Success => "succeeded",
        _ => "unavailable"
    };
}

public sealed record SetupSecretBindingReadinessDocument;
