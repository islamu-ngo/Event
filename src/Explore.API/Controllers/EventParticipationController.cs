using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Features.EventParticipation.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/participation")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class EventParticipationController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor ConfigureValidationProblem = new(
        "eventParticipation",
        "Event participation validation failed",
        "Event participation configuration failed.");

    private static readonly ApiNotFoundProblemDescriptor EventParticipationNotFoundProblem = new(
        "Event participation configuration not found",
        "Event participation configuration was not found.");

    private static readonly ApiValidationProblemDescriptor RequirementValidationProblem = new(
        "registrationRequirement",
        "Registration requirement validation failed",
        "Registration requirement attachment failed.");

    private static readonly ApiNotFoundProblemDescriptor OptionalQuestionnaireNotFoundProblem = new(
        "Optional questionnaire not found",
        "Optional questionnaire was not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<OptionalQuestionnaireDto, OptionalQuestionnaireDto> _questionnaireAssembler;

    public EventParticipationController(
        IMediator mediator,
        IResourceAssembler<OptionalQuestionnaireDto, OptionalQuestionnaireDto> questionnaireAssembler)
    {
        _mediator = mediator;
        _questionnaireAssembler = questionnaireAssembler;
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPatch("", Name = RouteNames.ConfigureEventParticipation)]
    [EndpointSummary("Configure event participation")]
    [EndpointDescription("Configures event participation for the event using the supplied concurrency stamp.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Configure(
        Guid eventId,
        [FromBody] ConfigureEventParticipationDto participationConfiguration,
        [FromHeader(Name = "If-Match"), Required] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                ConfigureValidationProblem,
                "If-Match header is required and must contain the current event participation concurrency stamp.");
        }

        var response = await _mediator.Send(new ConfigureEventParticipationCommand
        {
            EventId = eventId,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            ParticipationConfiguration = participationConfiguration
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode switch
            {
                "event_participation_configuration_not_found" =>
                    this.ToNotFoundProblem(EventParticipationNotFoundProblem, response.Message),
                "event_participation_configuration_concurrency_conflict" =>
                    this.ToCommandConflictProblem(
                        response,
                        "Event participation configuration conflict",
                        "Event participation configuration conflict."),
                _ => this.ToCommandValidationProblem(response, ConfigureValidationProblem)
            };
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("requirements/{requirementId:guid}", Name = RouteNames.AttachRegistrationRequirement)]
    [EndpointSummary("Attach a participation requirement")]
    [EndpointDescription("Attaches an event-owned registration requirement using the current participation concurrency stamp.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> AttachRequirement(
        Guid eventId,
        Guid requirementId,
        [FromBody] AttachRegistrationRequirementInputDto input,
        [FromHeader(Name = "If-Match"), Required] string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out Guid stamp))
        {
            return this.ToValidationProblem(
                RequirementValidationProblem,
                "If-Match must be a strong quoted non-empty GUID concurrency stamp.");
        }

        BaseCommandResponse<Guid> response = await _mediator.Send(
            new AttachRegistrationRequirementCommand(
                eventId,
                input.WorkflowId,
                requirementId,
                input.StandaloneQuestionnaire,
                input.RegistrationFormId,
                input.RegistrationFormVersionId,
                stamp),
            cancellationToken);
        return ToRequirementResult(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpDelete("requirements/{requirementId:guid}", Name = RouteNames.DetachRegistrationRequirement)]
    [EndpointSummary("Detach a participation requirement")]
    [EndpointDescription("Idempotently detaches a requirement without changing the requirement or registration state.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DetachRequirement(
        Guid eventId,
        Guid requirementId,
        [FromHeader(Name = "If-Match"), Required] string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out Guid stamp))
        {
            return this.ToValidationProblem(
                RequirementValidationProblem,
                "If-Match must be a strong quoted non-empty GUID concurrency stamp.");
        }

        BaseCommandResponse<Guid> response = await _mediator.Send(
            new DetachRegistrationRequirementCommand(eventId, requirementId, stamp),
            cancellationToken);
        return ToRequirementResult(response);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("optional-questionnaire", Name = RouteNames.GetOptionalQuestionnaire)]
    [EndpointSummary("Get the optional walk-in questionnaire")]
    [EndpointDescription("Returns the active published standalone questionnaire descriptor or a non-disclosing not-found response.")]
    [ProducesResponseType(typeof(HalResource<OptionalQuestionnaireDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<OptionalQuestionnaireDto>>> GetOptionalQuestionnaire(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        OptionalQuestionnaireDto? dto = await _mediator.Send(
            new GetOptionalQuestionnaireQuery(eventId), cancellationToken);
        if (dto is null)
        {
            return this.ToNotFoundProblem(OptionalQuestionnaireNotFoundProblem);
        }

        var result = new ObjectResult(await _questionnaireAssembler.ToResource(dto, HttpContext))
        {
            StatusCode = StatusCodes.Status200OK
        };
        result.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return result;
    }

    private ActionResult<BaseCommandResponse<Guid>> ToRequirementResult(BaseCommandResponse<Guid> response)
    {
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return response.FailureCode switch
        {
            "registration_requirement_not_found" =>
                this.ToNotFoundProblem(EventParticipationNotFoundProblem, response.Message),
            "registration_requirement_concurrency_conflict" =>
                this.ToCommandConflictProblem(
                    response,
                    "Registration requirement concurrency conflict",
                    "Registration requirement concurrency conflict."),
            _ => this.ToCommandValidationProblem(response, RequirementValidationProblem)
        };
    }
}
