using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Registration;
using Explore.Application.Features.RegistrationAnswerFiles.Commands;
using Explore.Application.Features.RegistrationAnswerFiles.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/registration-answer-files")]
[Authorize(Policy = ApiAuthorizationPolicies.Admin)]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class RegistrationAnswerFilesController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<RegistrationAnswerFileDto, RegistrationAnswerFileDto> resourceAssembler) : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor NotFoundProblem = new(
        "Registration answer file not found",
        "Registration answer file not found.");
    private static readonly ApiValidationProblemDescriptor ReleaseValidationProblem = new(
        "registrationAnswerFile",
        "Registration answer file release failed",
        "The registration answer file could not be released.");

    [HttpGet("{id:guid}", Name = RouteNames.GetRegistrationAnswerFile)]
    [ProducesResponseType(typeof(HalResource<RegistrationAnswerFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationAnswerFileDto>>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        RegistrationAnswerFileDto? file = await mediator.Send(
            new GetRegistrationAnswerFileQuery(tenantContext.TenantId, id), cancellationToken);
        return file is null
            ? this.ToNotFoundProblem(NotFoundProblem)
            : Ok(await resourceAssembler.ToResource(file, HttpContext));
    }

    [HttpPost("{id:guid}/release", Name = RouteNames.ReleaseRegistrationAnswerFile)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(HalResource<RegistrationAnswerFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationAnswerFileDto>>> Release(
        Guid id,
        RegistrationAnswerFileReleaseInputDto input,
        CancellationToken cancellationToken)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(
            new ReleaseRegistrationAnswerFileCommand(tenantContext.TenantId, id, input.Reason), cancellationToken);
        if (!response.IsSuccess)
        {
            return response.FailureCode == "registration_answer_file_not_found"
                ? this.ToNotFoundProblem(NotFoundProblem)
                : this.ToCommandValidationProblem(response, ReleaseValidationProblem);
        }

        RegistrationAnswerFileDto file = await mediator.Send(
            new GetRegistrationAnswerFileQuery(tenantContext.TenantId, id), cancellationToken)
            ?? throw new InvalidOperationException("Released registration answer file could not be reloaded.");
        return Ok(await resourceAssembler.ToResource(file, HttpContext));
    }
}
