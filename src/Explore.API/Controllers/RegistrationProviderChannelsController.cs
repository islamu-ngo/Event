using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
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
public sealed class RegistrationProviderChannelsController(
    IMediator mediator,
    IResourceAssembler<RegistrationChannelDto, RegistrationChannelDto> channelAssembler,
    IResourceAssembler<RegistrationProviderLaunchDescriptorDto, RegistrationProviderLaunchDescriptorDto> launchDescriptorAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor ProviderManagementValidationProblem = new(
        "registrationProviderManagement",
        "Registration provider management request failed",
        "The registration provider management request was invalid.");

    [HttpGet("workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels/{channelId:guid}/bindings/{bindingId:guid}/launch-descriptor", Name = RouteNames.GetRegistrationProviderLaunchDescriptor)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationProviderLaunchDescriptorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationProviderLaunchDescriptorDto>>> GetLaunchDescriptor(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, Guid bindingId, CancellationToken cancellationToken = default) =>
        Ok(await launchDescriptorAssembler.ToResource(await mediator.Send(new GetRegistrationProviderLaunchDescriptorQuery(tenantId, eventId, workflowId, requirementId, channelId, bindingId), cancellationToken), HttpContext));

    [HttpGet("workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels", Name = RouteNames.GetRegistrationChannels)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationChannelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<RegistrationChannelDto>>> GetChannels(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, CancellationToken cancellationToken = default) =>
        Ok(await channelAssembler.ToCollectionResource(await mediator.Send(new GetRegistrationChannelsQuery(tenantId, eventId, workflowId, requirementId), cancellationToken), RouteNames.GetRegistrationChannels, new RegistrationProviderChannelCollectionContext(tenantId, eventId, workflowId, requirementId), HttpContext));

    [HttpPost("workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels", Name = RouteNames.CreateRegistrationChannel)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateChannel(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, [FromBody] RegistrationChannelRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new UpsertRegistrationChannelCommand(tenantId, eventId, workflowId, requirementId, null, request), cancellationToken));

    [HttpPut("workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels/{channelId:guid}", Name = RouteNames.UpdateRegistrationChannel)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateChannel(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, [FromBody] RegistrationChannelRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new UpsertRegistrationChannelCommand(tenantId, eventId, workflowId, requirementId, channelId, request), cancellationToken));

    [HttpDelete("workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels/{channelId:guid}", Name = RouteNames.DeleteRegistrationChannel)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DeleteChannel(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new DeleteRegistrationChannelCommand(tenantId, eventId, workflowId, requirementId, channelId), cancellationToken));

    private ActionResult<BaseCommandResponse<Guid>> ToActionResult(BaseCommandResponse<Guid> result) => result.IsSuccess
        ? Ok(result)
        : this.ToCommandValidationProblem(result, ProviderManagementValidationProblem);
}
