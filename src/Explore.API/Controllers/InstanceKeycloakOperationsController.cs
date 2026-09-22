using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Authorize]
[Route("api/instance/keycloak")]
[EndpointClassification(EndpointClass.Admin)]
public sealed class InstanceKeycloakOperationsController(
    IQueryHandler<GetKeycloakConnectionQuery, KeycloakConnectionDto> connection,
    IQueryHandler<InspectKeycloakOperationQuery, KeycloakInspectionDto> inspect,
    ICommandHandler<PlanKeycloakOperationCommand, KeycloakOperationDto> plan,
    IQueryHandler<GetKeycloakOperationQuery, KeycloakOperationDto?> operation,
    ICommandHandler<ApplyKeycloakOperationCommand, KeycloakOperationDto> apply,
    ICommandHandler<ReconcileKeycloakOperationCommand, KeycloakOperationDto> reconcile,
    ICommandHandler<CancelKeycloakOperationCommand, KeycloakOperationDto> cancel,
    IAdminContext adminContext,
    ISetupSecretProvider setupSecretProvider)
    : InstanceSettingsControllerBase(adminContext, setupSecretProvider)
{
    [HttpGet("connection", Name = RouteNames.GetInstanceKeycloakConnection)]
    [InstanceManagement]
    [EndpointSummary("Get Effective Keycloak Connection")]
    [PrivateNoStore]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(
        typeof(HalResource<KeycloakConnectionDto>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<KeycloakConnectionDto>>> Connection(
        [FromServices] IResourceAssembler<
            KeycloakConnectionDto,
            KeycloakConnectionDto> assembler,
        CancellationToken cancellationToken)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken))
        {
            return this.ToForbiddenProblem(
                detail: "Current setup or instance-administrator authority is required.");
        }

        KeycloakConnectionDto dto = await connection.QueryAsync(
            new GetKeycloakConnectionQuery(),
            cancellationToken);
        return Ok(await assembler.ToResource(dto, HttpContext));
    }

    [HttpPost("inspect", Name = RouteNames.InspectInstanceKeycloak)]
    [InstanceManagement]
    [EndpointSummary("Inspect Keycloak Configuration")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [Consumes("application/json")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(
        typeof(HalResource<KeycloakInspectionDto>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<KeycloakInspectionDto>>> Inspect(
        [FromBody] KeycloakOperationInput input,
        [FromServices] IResourceAssembler<
            KeycloakInspectionDto,
            KeycloakInspectionDto> assembler,
        CancellationToken cancellationToken)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken))
        {
            return this.ToForbiddenProblem(
                detail: "Current setup or instance-administrator authority is required.");
        }

        KeycloakInspectionDto dto = await inspect.QueryAsync(
            new InspectKeycloakOperationQuery(input),
            cancellationToken);
        return Ok(await assembler.ToResource(dto, HttpContext));
    }

    [HttpPost("plans", Name = RouteNames.PlanInstanceKeycloak)]
    [InstanceManagement]
    [EndpointSummary("Plan Keycloak Operation")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [Consumes("application/json")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(
        typeof(HalResource<KeycloakOperationDto>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<KeycloakOperationDto>>> Plan(
        [FromBody] KeycloakOperationInput input,
        [FromServices] IResourceAssembler<
            KeycloakOperationDto,
            KeycloakOperationDto> assembler,
        CancellationToken cancellationToken)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken))
        {
            return this.ToForbiddenProblem(
                detail: "Current setup or instance-administrator authority is required.");
        }

        KeycloakOperationDto dto = await plan.ExecuteAsync(
            new PlanKeycloakOperationCommand(input),
            cancellationToken);
        return Ok(await assembler.ToResource(dto, HttpContext));
    }

    [HttpGet(
        "operations/{operationId:guid}",
        Name = RouteNames.GetInstanceKeycloakOperation)]
    [InstanceManagement]
    [EndpointSummary("Get Keycloak Operation")]
    [PrivateNoStore]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(
        typeof(HalResource<KeycloakOperationDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<KeycloakOperationDto>>> Operation(
        Guid operationId,
        [FromServices] IResourceAssembler<
            KeycloakOperationDto,
            KeycloakOperationDto> assembler,
        CancellationToken cancellationToken)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken))
        {
            return this.ToForbiddenProblem(
                detail: "Current setup or instance-administrator authority is required.");
        }

        KeycloakOperationDto? dto = await operation.QueryAsync(
            new GetKeycloakOperationQuery(operationId),
            cancellationToken);
        return dto is null
            ? NotFound()
            : Ok(await assembler.ToResource(dto, HttpContext));
    }

    [HttpPost(
        "operations/{operationId:guid}/apply",
        Name = RouteNames.ApplyInstanceKeycloakOperation)]
    [InstanceManagement]
    [EndpointSummary("Apply Keycloak Operation")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [Consumes("application/json")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(
        typeof(HalResource<KeycloakOperationDto>),
        StatusCodes.Status200OK)]
    public Task<ActionResult<HalResource<KeycloakOperationDto>>> Apply(
        Guid operationId,
        [FromBody] KeycloakOperationInput input,
        [FromServices] IResourceAssembler<
            KeycloakOperationDto,
            KeycloakOperationDto> assembler,
        CancellationToken cancellationToken) =>
        ExecuteOperationCommandAsync(
            () => apply.ExecuteAsync(
                new ApplyKeycloakOperationCommand(operationId, input),
                cancellationToken),
            assembler,
            cancellationToken);

    [HttpPost(
        "operations/{operationId:guid}/reconcile",
        Name = RouteNames.ReconcileInstanceKeycloakOperation)]
    [InstanceManagement]
    [EndpointSummary("Reconcile Keycloak Operation")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [Consumes("application/json")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(
        typeof(HalResource<KeycloakOperationDto>),
        StatusCodes.Status200OK)]
    public Task<ActionResult<HalResource<KeycloakOperationDto>>> Reconcile(
        Guid operationId,
        [FromBody] KeycloakOperationInput input,
        [FromServices] IResourceAssembler<
            KeycloakOperationDto,
            KeycloakOperationDto> assembler,
        CancellationToken cancellationToken) =>
        ExecuteOperationCommandAsync(
            () => reconcile.ExecuteAsync(
                new ReconcileKeycloakOperationCommand(operationId, input),
                cancellationToken),
            assembler,
            cancellationToken);

    [HttpPost(
        "operations/{operationId:guid}/cancel",
        Name = RouteNames.CancelInstanceKeycloakOperation)]
    [InstanceManagement]
    [EndpointSummary("Cancel Keycloak Operation")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.SetupSecretPolicy)]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(
        typeof(HalResource<KeycloakOperationDto>),
        StatusCodes.Status200OK)]
    public Task<ActionResult<HalResource<KeycloakOperationDto>>> Cancel(
        Guid operationId,
        [FromServices] IResourceAssembler<
            KeycloakOperationDto,
            KeycloakOperationDto> assembler,
        CancellationToken cancellationToken) =>
        ExecuteOperationCommandAsync(
            () => cancel.ExecuteAsync(
                new CancelKeycloakOperationCommand(operationId),
                cancellationToken),
            assembler,
            cancellationToken);

    private async Task<ActionResult<HalResource<KeycloakOperationDto>>>
        ExecuteOperationCommandAsync(
            Func<Task<KeycloakOperationDto>> execute,
            IResourceAssembler<
                KeycloakOperationDto,
                KeycloakOperationDto> assembler,
            CancellationToken cancellationToken)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken))
        {
            return this.ToForbiddenProblem(
                detail: "Current setup or instance-administrator authority is required.");
        }

        KeycloakOperationDto dto = await execute();
        return Ok(await assembler.ToResource(dto, HttpContext));
    }
}
