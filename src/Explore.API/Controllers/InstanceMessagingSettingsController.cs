using Explore.Application.Authentication;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Filters;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
using Explore.API.Models;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Analytics;
using Explore.Application.DTOs.Footer;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Instance messaging configuration: SMTP delivery and resolver settings.
/// </summary>
/// <remarks>
/// Split out of InstanceSettingsController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/instance/settings")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class InstanceMessagingSettingsController : InstanceSettingsControllerBase
{
    private readonly IMediator _mediator;

    public InstanceMessagingSettingsController(
        IMediator mediator,
        IAdminContext adminContext,
        ISetupSecretProvider setupSecretProvider)
        : base(adminContext, setupSecretProvider)
    {
        _mediator = mediator;
    }

    [HttpGet("smtp", Name = RouteNames.GetInstanceSmtpSettings)]
    [EndpointSummary("Get Instance SMTP Settings")]
    [EndpointDescription("Returns instance SMTP settings. Only instance admins can access.")]
    [PrivateNoStore]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<InstanceSmtpSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<InstanceSmtpSettingsDto>>> GetSmtpSettings(
        [FromServices] IResourceAssembler<InstanceSmtpSettingsDto, InstanceSmtpSettingsDto> assembler,
        CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceSmtpSettingsQuery(), cancellationToken);
        return Ok(await assembler.ToResource(settings, HttpContext));
    }

    [HttpPost("smtp/disable-preview", Name = RouteNames.PreviewInstanceSmtpDisable)]
    [EndpointClassification(EndpointClass.Admin)]
    [EndpointSummary("Preview Instance SMTP Disable")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<EmailDeliveryDisablePreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EmailDeliveryDisablePreviewDto>>> PreviewSmtpDisable(
        [FromServices] IResourceAssembler<EmailDeliveryDisablePreviewDto, EmailDeliveryDisablePreviewDto> assembler,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new PreviewEmailDeliveryDisableQuery(TenantId: null), cancellationToken);
        return response.IsSuccess
            ? Ok(await assembler.ToResource(response.Id!, HttpContext))
            : this.ToEmailDeliveryDisableProblem(response);
    }

    [HttpPost("smtp/disable", Name = RouteNames.DisableInstanceSmtp)]
    [EndpointClassification(EndpointClass.Admin)]
    [EndpointSummary("Disable Instance SMTP")]
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
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DisableSmtp(
        [FromBody] EmailDeliveryDisableRequest body, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DisableEmailDeliveryCommand(TenantId: null,
            ExpectedRevision: body.ExpectedRevision, Acknowledgement: body.Acknowledgement,
            ConfirmationToken: body.ConfirmationToken), cancellationToken);
        return response.IsSuccess ? Ok(response) : this.ToEmailDeliveryDisableProblem(response);
    }

    [HttpPatch("smtp", Name = RouteNames.UpdateInstanceSmtpSettings)]
    [PrivateNoStore]
    [EndpointSummary("Update Instance SMTP Settings")]
    [EndpointDescription("Updates instance SMTP settings. Requires instance administrator.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateSmtpSettings(
        [FromBody] PatchInstanceSmtpSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateInstanceSmtpSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpPost("smtp/test", Name = RouteNames.TestInstanceSmtpConnection)]
    [EndpointSummary("Test SMTP Connection")]
    [EndpointDescription("Tests the SMTP connection using current settings.")]
    [ProducesResponseType(typeof(SmtpConnectionTestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SmtpConnectionTestResultDto>> TestSmtpConnection(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");

        var result = await _mediator.Send(new TestInstanceSmtpConnectionQuery(), cancellationToken);

        var message = result.Success
            ? (string.IsNullOrWhiteSpace(result.Message) ? "Connection successful." : result.Message)
            : (string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Connection failed. Please verify your SMTP settings." : result.ErrorMessage);

        return Ok(new SmtpConnectionTestResultDto(result.Success, message));
    }

    [HttpGet("resolver-config", Name = RouteNames.GetInstanceResolverConfiguration)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get Tenant Resolver Configuration")]
    [EndpointDescription("Returns the non-sensitive instance-level tenant resolver configuration used by API clients and the BFF routing bootstrap.")]
    [ProducesResponseType(typeof(ResolverConfigurationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResolverConfigurationDto>> GetResolverConfiguration(CancellationToken cancellationToken = default)
    {
        var configuration = await _mediator.Send(new GetResolverConfigurationQuery(), cancellationToken);
        return Ok(configuration);
    }

    [HttpPatch("resolver-config", Name = RouteNames.UpdateInstanceResolverConfiguration)]
    [EndpointSummary("Update Tenant Resolver Configuration")]
    [EndpointDescription("Updates instance-level tenant resolver configuration. Requires instance administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateResolverConfiguration(
        [FromBody] PatchResolverConfigurationDto configuration, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateResolverConfigurationCommand { UserId = userId.Value, Patch = configuration }, cancellationToken);
        return HandleCommandResponse(response);
    }
}
