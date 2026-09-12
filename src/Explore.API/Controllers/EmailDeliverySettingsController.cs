using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Filters;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/settings")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Tags("Settings")]
public class EmailDeliverySettingsController(
    ICommandHandler<PreviewEmailDeliveryDisableCommand, BaseCommandResponse<EmailDeliveryDisablePreviewDto>> previewCommand,
    ICommandHandler<DisableEmailDeliveryCommand, BaseCommandResponse<Guid>> disableCommand) : ControllerBase
{
    [HttpPost("email-delivery/disable-preview", Name = RouteNames.PreviewTenantSmtpDisable)]
    [EndpointSummary("Preview Tenant SMTP Disable")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<EmailDeliveryDisablePreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EmailDeliveryDisablePreviewDto>>> PreviewEmailDeliveryDisable(
        [FromServices] ITenantContext tenantContext,
        [FromServices] IResourceAssembler<EmailDeliveryDisablePreviewDto, EmailDeliveryDisablePreviewDto> assembler,
        CancellationToken cancellationToken = default)
    {
        var response = await previewCommand.ExecuteAsync(new PreviewEmailDeliveryDisableCommand(TenantId: tenantContext.TenantId), cancellationToken);
        return response.IsSuccess
            ? Ok(await assembler.ToResource(response.Id!, HttpContext))
            : this.ToEmailDeliveryDisableProblem(response);
    }

    [HttpPost("email-delivery/disable", Name = RouteNames.DisableTenantSmtp)]
    [EndpointSummary("Disable Tenant SMTP")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DisableEmailDelivery(
        [FromBody] EmailDeliveryDisableRequest body,
        [FromServices] ITenantContext tenantContext, CancellationToken cancellationToken = default)
    {
        var response = await disableCommand.ExecuteAsync(new DisableEmailDeliveryCommand(TenantId: tenantContext.TenantId,
            ExpectedRevision: body.ExpectedRevision, Acknowledgement: body.Acknowledgement,
            ConfirmationToken: body.ConfirmationToken), cancellationToken);
        return response.IsSuccess ? Ok(response) : this.ToEmailDeliveryDisableProblem(response);
    }
}
