using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EmailUnsubscribe;
using Explore.Application.Features.EmailUnsubscribe.Requests.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/email/unsubscribe")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public sealed class EmailUnsubscribeController(
    IEmailUnsubscribeTokenService tokenService,
    IMediator mediator,
    ILogger<EmailUnsubscribeController> logger) : EventControllerBase
{
    [HttpGet(Name = RouteNames.GetEmailUnsubscribe)]
    [AllowAnonymous]
    [EndpointSummary("Get Email Unsubscribe Status")]
    [EndpointDescription("Validates an unsubscribe token and returns the confirmation state without changing preferences.")]
    [ProducesResponseType(typeof(EmailUnsubscribeResponseDto), StatusCodes.Status200OK)]
    public ActionResult<EmailUnsubscribeResponseDto> Get([FromQuery] string? token = null)
    {
        var validation = tokenService.ValidateToken(token);

        if (!validation.IsValid || validation.Payload is null)
        {
            return Ok(new EmailUnsubscribeResponseDto(
                "invalid",
                "This unsubscribe link is invalid or has expired."));
        }

        return Ok(new EmailUnsubscribeResponseDto(
            "confirmation_required",
            "Confirm to stop receiving this category of email.",
            validation.Payload.Category,
            RequiresConfirmation: true));
    }

    [HttpPost(Name = RouteNames.OneClickEmailUnsubscribe)]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.GlobalPolicy)]
    [EndpointSummary("One-Click Email Unsubscribe")]
    [EndpointDescription("Processes RFC 8058 one-click unsubscribe POSTs and manual confirmation posts.")]
    [ProducesResponseType(typeof(EmailUnsubscribeResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailUnsubscribeResponseDto>> Post([FromQuery] string? token = null, CancellationToken cancellationToken = default)
    {
        var validation = tokenService.ValidateToken(token);

        if (validation.IsValid && validation.Payload is not null)
        {
            await mediator.Send(new UnsubscribeFromEmailCategoryCommand
            {
                TenantId = validation.Payload.TenantId,
                UserId = validation.Payload.UserId,
                Category = validation.Payload.Category
            }, cancellationToken);
        }
        else
        {
            logger.LogInformation("Ignored invalid or expired email unsubscribe token with reason {FailureReason}", validation.FailureReason);
        }

        return Ok(new EmailUnsubscribeResponseDto(
            "unsubscribed",
            "If this email address was subscribed, it has been unsubscribed from this category."));
    }
}
