using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/tenants/{tenantId:guid}/events/{eventId:guid}/registration-providers")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[Tags("RegistrationProviderManagement")]
public sealed class RegistrationProviderBindingsController(
    ICommandHandler<ImportExternalRegistrationProviderFormVersionCommand, BaseCommandResponse<Guid>> importHandler,
    IQueryHandler<GetRegistrationProviderBindingsQuery, IReadOnlyList<RegistrationProviderBindingDto>> bindingsQueryHandler,
    IQueryHandler<GetRegistrationProviderBindingQuery, RegistrationProviderBindingDto?> bindingQueryHandler,
    ICommandHandler<CreateRegistrationProviderBindingCommand, BaseCommandResponse<Guid>> createBindingHandler,
    ICommandHandler<UpdateRegistrationProviderBindingCommand, BaseCommandResponse<Guid>> updateBindingHandler,
    ICommandHandler<DeleteRegistrationProviderBindingCommand, BaseCommandResponse<Guid>> deleteBindingHandler,
    ICommandHandler<PublishEventRegistrationProviderBindingCommand, BaseCommandResponse<Guid>> publishBindingHandler,
    ICommandHandler<ReplaceEventDraftRegistrationProviderMappingsCommand, BaseCommandResponse<Guid>> replaceMappingsHandler,
    IResourceAssembler<RegistrationProviderBindingDto, RegistrationProviderBindingDto> bindingAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor ProviderManagementValidationProblem = new(
        "registrationProviderManagement",
        "Registration provider management request failed",
        "The registration provider management request was invalid.");

    [HttpPost("connections/{connectionId:guid}/external-imports", Name = RouteNames.ImportExternalRegistrationProviderFormVersion)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ImportExternalFormVersion(Guid tenantId, Guid eventId, Guid connectionId, [FromBody] ImportExternalRegistrationProviderFormVersionRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await importHandler.ExecuteAsync(new ImportExternalRegistrationProviderFormVersionCommand(tenantId, eventId, connectionId, request), cancellationToken));

    [HttpGet("bindings", Name = RouteNames.GetRegistrationProviderBindings)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationProviderBindingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<RegistrationProviderBindingDto>>> GetBindings(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default) =>
        Ok(await bindingAssembler.ToCollectionResource(await bindingsQueryHandler.QueryAsync(new GetRegistrationProviderBindingsQuery(tenantId, eventId), cancellationToken), RouteNames.GetRegistrationProviderBindings, new RegistrationProviderEventCollectionContext(tenantId, eventId), HttpContext));

    [HttpGet("bindings/{bindingId:guid}", Name = RouteNames.GetRegistrationProviderBinding)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationProviderBindingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationProviderBindingDto>>> GetBinding(Guid tenantId, Guid eventId, Guid bindingId, CancellationToken cancellationToken = default) =>
        await bindingQueryHandler.QueryAsync(new GetRegistrationProviderBindingQuery(tenantId, eventId, bindingId), cancellationToken) is { } result ? Ok(await bindingAssembler.ToResource(result, HttpContext)) : NotFound();

    [HttpPost("bindings", Name = RouteNames.CreateRegistrationProviderBinding)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateBinding(Guid tenantId, Guid eventId, [FromBody] RegistrationProviderBindingRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await createBindingHandler.ExecuteAsync(new CreateRegistrationProviderBindingCommand(tenantId, eventId, request), cancellationToken));

    [HttpPut("bindings/{bindingId:guid}", Name = RouteNames.UpdateRegistrationProviderBinding)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateBinding(Guid tenantId, Guid eventId, Guid bindingId, [FromBody] RegistrationProviderBindingRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await updateBindingHandler.ExecuteAsync(new UpdateRegistrationProviderBindingCommand(tenantId, eventId, bindingId, request), cancellationToken));

    [HttpDelete("bindings/{bindingId:guid}", Name = RouteNames.DeleteRegistrationProviderBinding)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DeleteBinding(Guid tenantId, Guid eventId, Guid bindingId, CancellationToken cancellationToken = default) =>
        ToActionResult(await deleteBindingHandler.ExecuteAsync(new DeleteRegistrationProviderBindingCommand(tenantId, eventId, bindingId), cancellationToken));

    [HttpPost("bindings/{bindingId:guid}/publish", Name = RouteNames.PublishRegistrationProviderBinding)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> PublishBinding(Guid tenantId, Guid eventId, Guid bindingId, CancellationToken cancellationToken = default) =>
        ToActionResult(await publishBindingHandler.ExecuteAsync(new PublishEventRegistrationProviderBindingCommand(tenantId, eventId, bindingId), cancellationToken));

    [HttpPut("bindings/{bindingId:guid}/mappings", Name = RouteNames.ReplaceRegistrationProviderMappings)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ReplaceMappings(Guid tenantId, Guid eventId, Guid bindingId, [FromBody] ReplaceRegistrationProviderMappingsRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await replaceMappingsHandler.ExecuteAsync(new ReplaceEventDraftRegistrationProviderMappingsCommand(tenantId, eventId, bindingId, request), cancellationToken));

    private ActionResult<BaseCommandResponse<Guid>> ToActionResult(BaseCommandResponse<Guid> result) => result.IsSuccess
        ? Ok(result)
        : this.ToCommandValidationProblem(result, ProviderManagementValidationProblem);
}
