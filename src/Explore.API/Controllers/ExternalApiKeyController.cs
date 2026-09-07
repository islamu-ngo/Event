using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public class ExternalApiKeyController(IMediator mediator) : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "externalApiKey",
        "External API key validation failed",
        "External API key creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "externalApiKey",
        "External API key validation failed",
        "External API key update failed.");

    private static readonly ApiNotFoundProblemDescriptor ExternalApiKeyNotFoundProblem = new(
        "External API key not found",
        "External API key not found.");

    [HttpGet(Name = RouteNames.GetExternalApiKeys)]
    [EndpointSummary("Get visible external API keys")]
    [EndpointDescription("Retrieve API keys owned by the current user or organizations they can manage.")]
    [ProducesResponseType(typeof(List<ExternalApiKeyListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<List<ExternalApiKeyListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var keys = await mediator.Send(new GetExternalApiKeyListRequest(), cancellationToken);
        return Ok(keys);
    }

    [HttpGet("{id}", Name = RouteNames.GetExternalApiKeyById)]
    [EndpointSummary("Get external API key details")]
    [EndpointDescription("Retrieve metadata for a specific external API key visible to the current caller.")]
    [ProducesResponseType(typeof(ExternalApiKeyListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<ExternalApiKeyListDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var key = await mediator.Send(new GetExternalApiKeyDetailsRequest(id), cancellationToken);
        if (key == null)
            return this.ToNotFoundProblem(ExternalApiKeyNotFoundProblem);

        return Ok(key);
    }

    [HttpPost(Name = RouteNames.CreateExternalApiKey)]
    [EndpointSummary("Create a new external API key")]
    [EndpointDescription("Issue a tenant-bound external API key and reveal the raw secret once.")]
    [ProducesResponseType(typeof(CreateExternalApiKeyCommandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult<CreateExternalApiKeyCommandResponse>> Create([FromBody] CreateExternalApiKeyDto dto, CancellationToken cancellationToken = default)
    {
        var command = new CreateExternalApiKeyCommand { ExternalApiKeyDto = dto };
        var response = await mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return Ok(response);
    }

    [HttpPatch("{id}", Name = RouteNames.UpdateExternalApiKey)]
    [EndpointSummary("Update an external API key policy")]
    [EndpointDescription("Update editable policy fields for a visible external API key.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(Guid id, [FromBody] UpdateExternalApiKeyPolicyDto dto, CancellationToken cancellationToken = default)
    {
        var command = new UpdateExternalApiKeyPolicyCommand
        {
            ExternalApiKeyId = id,
            ExternalApiKeyPolicyDto = dto
        };
        var response = await mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.FailureCode == FailureCodes.NotFound)
            {
                return this.ToNotFoundProblem(ExternalApiKeyNotFoundProblem);
            }

            return this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    [HttpDelete("{id}", Name = RouteNames.DeleteExternalApiKey)]
    [EndpointSummary("Revoke an external API key")]
    [EndpointDescription("Revoke an external API key visible to the current caller.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var revoked = await mediator.Send(new RevokeExternalApiKeyCommand(id), cancellationToken);

        return revoked ? NoContent() : this.ToNotFoundProblem(ExternalApiKeyNotFoundProblem);
    }

    [HttpGet("usage-report", Name = RouteNames.GetExternalApiKeyUsageReport)]
    [EndpointSummary("Get API key usage report")]
    [EndpointDescription("Aggregated request counts and credit usage per API key. Instance admins see platform-wide data; tenant admins see their tenant only. Does not expose secret material.")]
    [ProducesResponseType(typeof(List<ExternalApiKeyUsageReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<List<ExternalApiKeyUsageReportDto>>> GetUsageReport(
        [FromQuery] ExternalApiKeyUsageReportQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var report = await mediator.Send(new GetExternalApiKeyUsageReportRequest
        {
            From = query.From,
            To = query.To,
            TenantId = query.TenantId
        }, cancellationToken);

        return Ok(report);
    }
}
