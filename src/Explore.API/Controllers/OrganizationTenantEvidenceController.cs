using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Commands;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/organizations/{organizationId:guid}/legitimacy-evidence")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class OrganizationTenantEvidenceController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor SubmitValidationProblem = new(
        "organizationTenantEvidence",
        "Organization legitimacy evidence validation failed",
        "Organization legitimacy evidence submission failed.");

    private static readonly ApiValidationProblemDescriptor ReviewValidationProblem = new(
        "organizationTenantEvidence",
        "Organization legitimacy evidence validation failed",
        "Organization legitimacy evidence review failed.");

    private static readonly ApiNotFoundProblemDescriptor EvidenceNotFoundProblem = new(
        "Organization legitimacy evidence not found",
        "The requested Organization legitimacy evidence was not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<OrganizationTenantEvidenceDto, OrganizationTenantEvidenceDto> _resourceAssembler;

    public OrganizationTenantEvidenceController(
        IMediator mediator,
        IResourceAssembler<OrganizationTenantEvidenceDto, OrganizationTenantEvidenceDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [HttpGet("", Name = RouteNames.GetOrganizationTenantEvidenceCollection)]
    [ProducesResponseType(typeof(HalCollectionResource<OrganizationTenantEvidenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<HalCollectionResource<OrganizationTenantEvidenceDto>>> GetAll(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var evidence = await _mediator.Send(
            new GetOrganizationTenantEvidenceCollectionRequest(organizationId),
            cancellationToken);
        var resource = await _resourceAssembler.ToCollectionResource(
            evidence,
            RouteNames.GetOrganizationTenantEvidenceCollection,
            new { organizationId },
            HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [HttpGet("{evidenceId:guid}", Name = RouteNames.GetOrganizationTenantEvidence)]
    [ProducesResponseType(typeof(HalResource<OrganizationTenantEvidenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<HalResource<OrganizationTenantEvidenceDto>>> GetById(
        Guid organizationId,
        Guid evidenceId,
        CancellationToken cancellationToken = default)
    {
        var evidence = await _mediator.Send(
            new GetOrganizationTenantEvidenceRequest(organizationId, evidenceId),
            cancellationToken);
        if (evidence is null)
        {
            return this.ToNotFoundProblem(EvidenceNotFoundProblem);
        }

        return Ok(await _resourceAssembler.ToResource(evidence, HttpContext));
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("upload-session", Name = RouteNames.CreateOrganizationTenantEvidenceUploadSession)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<StorageUploadSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<StorageUploadSessionDto>>> CreateUploadSession(
        Guid organizationId,
        [FromBody] CreateOrganizationTenantEvidenceUploadSessionDto upload,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new CreateOrganizationTenantEvidenceUploadSessionCommand
            {
                OrganizationId = organizationId,
                Upload = upload
            },
            cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, SubmitValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("", Name = RouteNames.SubmitOrganizationTenantEvidence)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Submit(
        Guid organizationId,
        [FromBody] SubmitOrganizationTenantEvidenceDto evidence,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new SubmitOrganizationTenantEvidenceCommand
            {
                OrganizationId = organizationId,
                Evidence = evidence
            },
            cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, SubmitValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetOrganizationTenantEvidence,
            new { organizationId, evidenceId = response.Id },
            response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("{evidenceId:guid}/review", Name = RouteNames.ReviewOrganizationTenantEvidence)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Review(
        Guid organizationId,
        Guid evidenceId,
        [FromBody] ReviewOrganizationTenantEvidenceDto review,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new ReviewOrganizationTenantEvidenceCommand
            {
                OrganizationId = organizationId,
                EvidenceId = evidenceId,
                Review = review
            },
            cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, ReviewValidationProblem);
        }

        return Ok(response);
    }
}
