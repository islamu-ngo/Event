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
public sealed class RegistrationProviderManagementController(
    IMediator mediator,
    IResourceAssembler<RegistrationProviderBindingHealthDto, RegistrationProviderBindingHealthDto> healthAssembler,
    IResourceAssembler<RegistrationProviderParkedQueueItemDto, RegistrationProviderParkedQueueItemDto> queueAssembler,
    IResourceAssembler<RegistrationProviderConnectionDto, RegistrationProviderConnectionDto> connectionAssembler,
    IResourceAssembler<RegistrationProviderBindingDto, RegistrationProviderBindingDto> bindingAssembler,
    IResourceAssembler<RegistrationChannelDto, RegistrationChannelDto> channelAssembler,
    IResourceAssembler<RegistrationProviderLaunchDescriptorDto, RegistrationProviderLaunchDescriptorDto> launchDescriptorAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor ProviderManagementValidationProblem = new(
        "registrationProviderManagement",
        "Registration provider management request failed",
        "The registration provider management request was invalid.");

    [HttpGet("health", Name = RouteNames.GetRegistrationProviderHealth)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationProviderBindingHealthDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<RegistrationProviderBindingHealthDto>>> GetHealth(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RegistrationProviderBindingHealthDto> result = await mediator.Send(new GetRegistrationProviderHealthQuery(tenantId, eventId), cancellationToken);
        return Ok(healthAssembler.ToCollectionResource(result, RouteNames.GetRegistrationProviderHealth, new { eventId, tenantId }, HttpContext));
    }

    [HttpGet("queue", Name = RouteNames.GetRegistrationProviderQueue)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationProviderParkedQueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<RegistrationProviderParkedQueueItemDto>>> GetQueue(
        Guid tenantId,
        Guid eventId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RegistrationProviderParkedQueueItemDto> result = await mediator.Send(new GetRegistrationProviderQueueQuery(tenantId, eventId, limit), cancellationToken);
        return Ok(queueAssembler.ToCollectionResource(result, RouteNames.GetRegistrationProviderQueue, new RegistrationProviderEventCollectionContext(tenantId, eventId), HttpContext));
    }

    [HttpGet("connections", Name = RouteNames.GetRegistrationProviderConnections)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationProviderConnectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<RegistrationProviderConnectionDto>>> GetConnections(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default) =>
        Ok(await connectionAssembler.ToCollectionResource(await mediator.Send(new GetRegistrationProviderConnectionsQuery(tenantId, eventId), cancellationToken), RouteNames.GetRegistrationProviderConnections, new RegistrationProviderEventCollectionContext(tenantId, eventId), HttpContext));

    [HttpGet("connections/{connectionId:guid}", Name = RouteNames.GetRegistrationProviderConnection)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationProviderConnectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationProviderConnectionDto>>> GetConnection(Guid tenantId, Guid eventId, Guid connectionId, CancellationToken cancellationToken = default) =>
        await mediator.Send(new GetRegistrationProviderConnectionQuery(tenantId, eventId, connectionId), cancellationToken) is { } result ? Ok(await connectionAssembler.ToResource(result, HttpContext)) : NotFound();

    [HttpPost("connections", Name = RouteNames.CreateRegistrationProviderConnection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateConnection(Guid tenantId, Guid eventId, [FromBody] RegistrationProviderConnectionRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new UpsertRegistrationProviderConnectionCommand(tenantId, eventId, null, request), cancellationToken));

    [HttpPut("connections/{connectionId:guid}", Name = RouteNames.UpdateRegistrationProviderConnection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateConnection(Guid tenantId, Guid eventId, Guid connectionId, [FromBody] RegistrationProviderConnectionRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new UpsertRegistrationProviderConnectionCommand(tenantId, eventId, connectionId, request), cancellationToken));

    [HttpDelete("connections/{connectionId:guid}", Name = RouteNames.DeleteRegistrationProviderConnection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DeleteConnection(Guid tenantId, Guid eventId, Guid connectionId, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new DeleteRegistrationProviderConnectionCommand(tenantId, eventId, connectionId), cancellationToken));

    [HttpPut("connections/{connectionId:guid}/approved-origins", Name = RouteNames.ReplaceRegistrationProviderApprovedOrigins)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ReplaceApprovedOrigins(Guid tenantId, Guid eventId, Guid connectionId, [FromBody] ReplaceRegistrationProviderApprovedOriginsRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new ReplaceRegistrationProviderApprovedOriginsCommand(tenantId, eventId, connectionId, request.Origins), cancellationToken));

    [HttpPost("connections/{connectionId:guid}/external-imports", Name = RouteNames.ImportExternalRegistrationProviderFormVersion)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ImportExternalFormVersion(Guid tenantId, Guid eventId, Guid connectionId, [FromBody] ImportExternalRegistrationProviderFormVersionRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new ImportExternalRegistrationProviderFormVersionCommand(tenantId, eventId, connectionId, request), cancellationToken));

    [HttpGet("bindings", Name = RouteNames.GetRegistrationProviderBindings)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationProviderBindingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<RegistrationProviderBindingDto>>> GetBindings(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default) =>
        Ok(await bindingAssembler.ToCollectionResource(await mediator.Send(new GetRegistrationProviderBindingsQuery(tenantId, eventId), cancellationToken), RouteNames.GetRegistrationProviderBindings, new RegistrationProviderEventCollectionContext(tenantId, eventId), HttpContext));

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
        await mediator.Send(new GetRegistrationProviderBindingQuery(tenantId, eventId, bindingId), cancellationToken) is { } result ? Ok(await bindingAssembler.ToResource(result, HttpContext)) : NotFound();

    [HttpPost("bindings", Name = RouteNames.CreateRegistrationProviderBinding)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateBinding(Guid tenantId, Guid eventId, [FromBody] RegistrationProviderBindingRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new CreateRegistrationProviderBindingCommand(tenantId, eventId, request), cancellationToken));

    [HttpPut("bindings/{bindingId:guid}", Name = RouteNames.UpdateRegistrationProviderBinding)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateBinding(Guid tenantId, Guid eventId, Guid bindingId, [FromBody] RegistrationProviderBindingRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new UpdateRegistrationProviderBindingCommand(tenantId, eventId, bindingId, request), cancellationToken));

    [HttpDelete("bindings/{bindingId:guid}", Name = RouteNames.DeleteRegistrationProviderBinding)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DeleteBinding(Guid tenantId, Guid eventId, Guid bindingId, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new DeleteRegistrationProviderBindingCommand(tenantId, eventId, bindingId), cancellationToken));

    [HttpPost("bindings/{bindingId:guid}/publish", Name = RouteNames.PublishRegistrationProviderBinding)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> PublishBinding(Guid tenantId, Guid eventId, Guid bindingId, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new PublishEventRegistrationProviderBindingCommand(tenantId, eventId, bindingId), cancellationToken));

    [HttpPut("bindings/{bindingId:guid}/mappings", Name = RouteNames.ReplaceRegistrationProviderMappings)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ReplaceMappings(Guid tenantId, Guid eventId, Guid bindingId, [FromBody] ReplaceRegistrationProviderMappingsRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new ReplaceEventDraftRegistrationProviderMappingsCommand(tenantId, eventId, bindingId, request), cancellationToken));

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

    [HttpPost("{bindingId:guid}/reconcile", Name = RouteNames.PollRegistrationProviderReconciliation)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> PollReconciliation(
        Guid tenantId,
        Guid eventId,
        Guid bindingId,
        [FromQuery] DateTime sinceUtc,
        CancellationToken cancellationToken = default) => ToActionResult(await mediator.Send(new PollRegistrationProviderReconciliationCommand(tenantId, eventId, bindingId, DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc)), cancellationToken));

    [HttpPost("manual-imports", Name = RouteNames.QueueManualRegistrationProviderImport)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> QueueManualImport(
        Guid tenantId,
        Guid eventId,
        [FromBody] ManualRegistrationProviderImportRequestDto request,
        CancellationToken cancellationToken = default) => ToActionResult(await mediator.Send(new QueueManualRegistrationProviderImportCommand(tenantId, eventId, request.BindingId, request.StorageReference, request.SourceReference), cancellationToken));

    [HttpPost("queue/retry", Name = RouteNames.RetryRegistrationProviderParkedItem)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RetryQueueItem(
        Guid tenantId,
        Guid eventId,
        [FromBody] RetryRegistrationProviderParkedItemRequestDto request,
        CancellationToken cancellationToken = default) => ToActionResult(await mediator.Send(new RetryRegistrationProviderParkedItemCommand(tenantId, eventId, request.SubmissionId, request.EffectOutboxId, request.ExpectedProcessingGeneration, request.Reason), cancellationToken));

    [HttpPost("queue/resolve", Name = RouteNames.ResolveRegistrationProviderQueueItem)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResolveQueueItem(
        Guid tenantId,
        Guid eventId,
        [FromBody] ResolveRegistrationProviderQueueItemRequestDto request,
        CancellationToken cancellationToken = default) => ToActionResult(await mediator.Send(new ResolveRegistrationProviderQueueItemCommand(tenantId, eventId, request.SubmissionId, request.EffectOutboxId, request.DecisionCode, request.NoteReference), cancellationToken));

    private ActionResult<BaseCommandResponse<Guid>> ToActionResult(BaseCommandResponse<Guid> result) => result.IsSuccess
        ? Ok(result)
        : this.ToCommandValidationProblem(result, ProviderManagementValidationProblem);
}
