using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.RegistrationAnalytics;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Features.RegistrationAnalytics;
using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/events/{eventId:guid}")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class RegistrationFormsController(
    IMediator mediator,
    IResourceAssembler<RegistrationWorkflowDto, RegistrationWorkflowDto> workflowAssembler,
    IResourceAssembler<RegistrationFormDto, RegistrationFormDto> formAssembler,
    IResourceAssembler<RegistrationFormVersionDto, RegistrationFormVersionDto> versionAssembler,
    IResourceAssembler<RegistrationAnswerAnalyticsDto, RegistrationAnswerAnalyticsDto> analyticsAssembler,
    IResourceAssembler<RegistrationFormTemplateDto, RegistrationFormTemplateDto> templateAssembler,
    IResourceAssembler<RegistrationFormPublishPreflightDto, RegistrationFormPublishPreflightDto> preflightAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor RegistrationValidationProblem = new(
        "registrationForm", "Registration form validation failed", "Registration form authoring failed.");
    private static readonly ApiNotFoundProblemDescriptor NotFoundProblem = new(
        "Registration authoring resource not found", "The requested registration authoring resource was not found.");

    [HttpGet("registration-workflows", Name = RouteNames.GetRegistrationWorkflow)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationWorkflowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationWorkflowDto>>> GetWorkflow(Guid eventId, [FromQuery] string purpose, CancellationToken ct)
        => await ToResource(await mediator.Send(new GetRegistrationWorkflowQuery(eventId, purpose), ct), workflowAssembler);

    [HttpGet("registration-answer-analytics", Name = RouteNames.GetRegistrationAnswerAnalytics)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationAnswerAnalyticsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationAnswerAnalyticsDto>>> GetAnswerAnalytics(
        Guid eventId,
        [FromQuery] Guid formId,
        [FromQuery] Guid formVersionId,
        CancellationToken ct)
        => await ToResource(await mediator.Send(new GetRegistrationAnswerAnalyticsQuery(eventId, formId, formVersionId), ct), analyticsAssembler);

    [HttpPost("registration-workflows", Name = RouteNames.CreateRegistrationWorkflow)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> CreateWorkflow(Guid eventId, RegistrationWorkflowInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new CreateRegistrationWorkflowCommand(eventId, input.Purpose, stamp), RouteNames.GetRegistrationWorkflow, _ => new { eventId, purpose = input.Purpose }, ct);

    [HttpPatch("registration-workflows/{workflowId:guid}", Name = RouteNames.UpdateRegistrationWorkflow)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateWorkflow(Guid eventId, Guid workflowId, RegistrationWorkflowInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new UpdateRegistrationWorkflowCommand(eventId, workflowId, input.Purpose, stamp), null, null, ct);

    [HttpPost("registration-workflows/{workflowId:guid}/requirements", Name = RouteNames.CreateRegistrationRequirement)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> CreateRequirement(Guid eventId, Guid workflowId, RegistrationRequirementInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new CreateRegistrationRequirementCommand(eventId, workflowId, input.Ordinal, input.CriticalityId, input.CanSkip, input.CompletionEffectId, input.AnswerSyncModeId, input.AppliesToSubjectTypeId, input.AppliesToSubjectId, stamp), RouteNames.GetRegistrationWorkflow, _ => new { eventId, purpose = "registration" }, ct);

    [HttpPatch("registration-workflows/{workflowId:guid}/requirements/{requirementId:guid}", Name = RouteNames.UpdateRegistrationRequirement)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateRequirement(Guid eventId, Guid workflowId, Guid requirementId, RegistrationRequirementInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new UpdateRegistrationRequirementCommand(eventId, workflowId, requirementId, input.Ordinal, input.CriticalityId, input.CanSkip, input.CompletionEffectId, input.AnswerSyncModeId, input.AppliesToSubjectTypeId, input.AppliesToSubjectId, stamp), null, null, ct);

    [HttpDelete("registration-workflows/{workflowId:guid}/requirements/{requirementId:guid}", Name = RouteNames.DeleteRegistrationRequirement)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DeleteRequirement(Guid eventId, Guid workflowId, Guid requirementId, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new DeleteRegistrationRequirementCommand(eventId, workflowId, requirementId, stamp), null, null, ct);

    [HttpGet("registration-forms/{formId:guid}", Name = RouteNames.GetRegistrationForm)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationFormDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationFormDto>>> GetForm(Guid eventId, Guid formId, CancellationToken ct)
        => await ToResource(await mediator.Send(new GetRegistrationFormQuery(eventId, formId), ct), formAssembler);

    [HttpPost("registration-workflows/{workflowId:guid}/forms", Name = RouteNames.CreateRegistrationForm)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> CreateForm(Guid eventId, Guid workflowId, RegistrationFormInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new CreateRegistrationFormCommand(eventId, workflowId, input.Namespace, input.Key, input.Name, input.LanguageTag, stamp), RouteNames.GetRegistrationForm, id => new { eventId, formId = id }, ct);

    [HttpGet("registration-forms/{formId:guid}/versions/{versionId:guid}", Name = RouteNames.GetRegistrationFormVersion)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationFormVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationFormVersionDto>>> GetVersion(Guid eventId, Guid formId, Guid versionId, CancellationToken ct)
        => await ToResource(await mediator.Send(new GetRegistrationFormVersionQuery(eventId, formId, versionId), ct), versionAssembler);

    [HttpPost("registration-forms/{formId:guid}/versions", Name = RouteNames.CreateRegistrationFormVersion)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> CreateVersion(Guid eventId, Guid formId, RegistrationFormVersionInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new CreateRegistrationFormVersionCommand(eventId, formId, input.CloneFromVersionId, input.LanguageTag, stamp), RouteNames.GetRegistrationFormVersion, id => new { eventId, formId, versionId = id }, ct);

    [HttpPost("registration-forms/{formId:guid}/versions/{versionId:guid}/sections", Name = RouteNames.AddRegistrationFormSection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AddSection(Guid eventId, Guid formId, Guid versionId, RegistrationFormSectionInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new AddRegistrationFormSectionCommand(eventId, formId, versionId, input.Ordinal, input.Title, stamp), RouteNames.GetRegistrationFormVersion, _ => new { eventId, formId, versionId }, ct);

    [HttpPatch("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}", Name = RouteNames.UpdateRegistrationFormSection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateSection(Guid eventId, Guid formId, Guid versionId, Guid sectionId, RegistrationFormSectionInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new UpdateRegistrationFormSectionCommand(eventId, formId, versionId, sectionId, input.Ordinal, input.Title, stamp), null, null, ct);

    [HttpPut("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/reorder", Name = RouteNames.ReorderRegistrationFormSections)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(HalResource<RegistrationFormVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<HalResource<RegistrationFormVersionDto>>> ReorderSections(Guid eventId, Guid formId, Guid versionId, RegistrationFormReorderInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Reorder(ifMatch, stamp => new ReorderRegistrationFormSectionsCommand(eventId, formId, versionId, input.OrderedIds, stamp), eventId, formId, versionId, ct);

    [HttpDelete("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}", Name = RouteNames.DeleteRegistrationFormSection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DeleteSection(Guid eventId, Guid formId, Guid versionId, Guid sectionId, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new DeleteRegistrationFormSectionCommand(eventId, formId, versionId, sectionId, stamp), null, null, ct);

    [HttpPost("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}/fields", Name = RouteNames.AddRegistrationFormField)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AddField(Guid eventId, Guid formId, Guid versionId, Guid sectionId, RegistrationFormFieldCreateInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new AddRegistrationFormFieldCommand(eventId, formId, versionId, sectionId, input.Ordinal, input.Namespace, input.Key, input.Label, input.FieldTypeId, input.RetentionPolicyId, input.OrganizerVisibilityId, input.RequiresExplicitConsent, input.IsProviderTransferAllowed, input.IsExportable, input.ExportPurposeCode, input.IsAnalyticsRelevant, input.IsOperationallyFilterable, input.ConsentPurposeCode, input.ConsentTextVersion, input.ConsentText, stamp), RouteNames.GetRegistrationFormVersion, _ => new { eventId, formId, versionId }, ct);

    [HttpPatch("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}/fields/{fieldId:guid}", Name = RouteNames.UpdateRegistrationFormField)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateField(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId, RegistrationFormFieldUpdateInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new UpdateRegistrationFormFieldCommand(eventId, formId, versionId, sectionId, fieldId, input.Ordinal, input.Label, input.RetentionPolicyId, input.OrganizerVisibilityId, input.RequiresExplicitConsent, input.IsProviderTransferAllowed, input.IsExportable, input.ExportPurposeCode, input.IsAnalyticsRelevant, input.IsOperationallyFilterable, input.ConsentPurposeCode, input.ConsentTextVersion, input.ConsentText, input.IsRequired, input.IsMulti, input.MinLength, input.MaxLength, input.RegexPattern, input.MinNumber, input.MaxNumber, input.MinDateTime, input.MaxDateTime, input.AllowedUrlSchemes, stamp), null, null, ct);

    [HttpPut("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}/fields/reorder", Name = RouteNames.ReorderRegistrationFormFields)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(HalResource<RegistrationFormVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<HalResource<RegistrationFormVersionDto>>> ReorderFields(Guid eventId, Guid formId, Guid versionId, Guid sectionId, RegistrationFormReorderInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Reorder(ifMatch, stamp => new ReorderRegistrationFormFieldsCommand(eventId, formId, versionId, sectionId, input.OrderedIds, stamp), eventId, formId, versionId, ct);

    [HttpDelete("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}/fields/{fieldId:guid}", Name = RouteNames.DeleteRegistrationFormField)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DeleteField(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new DeleteRegistrationFormFieldCommand(eventId, formId, versionId, sectionId, fieldId, stamp), null, null, ct);

    [HttpPost("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}/fields/{fieldId:guid}/options", Name = RouteNames.AddRegistrationFormFieldOption)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AddOption(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId, RegistrationFormOptionInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new AddRegistrationFormFieldOptionCommand(eventId, formId, versionId, sectionId, fieldId, input.Ordinal, input.Key, input.Label, stamp), RouteNames.GetRegistrationFormVersion, _ => new { eventId, formId, versionId }, ct);

    [HttpPatch("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}/fields/{fieldId:guid}/options/{optionId:guid}", Name = RouteNames.UpdateRegistrationFormFieldOption)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateOption(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId, Guid optionId, RegistrationFormOptionInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new UpdateRegistrationFormFieldOptionCommand(eventId, formId, versionId, sectionId, fieldId, optionId, input.Ordinal, input.Key, input.Label, stamp), null, null, ct);

    [HttpDelete("registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}/fields/{fieldId:guid}/options/{optionId:guid}", Name = RouteNames.RetireRegistrationFormFieldOption)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> RetireOption(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId, Guid optionId, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new RetireRegistrationFormFieldOptionCommand(eventId, formId, versionId, sectionId, fieldId, optionId, stamp), null, null, ct);

    [HttpPost("registration-forms/{formId:guid}/versions/{versionId:guid}/rules", Name = RouteNames.AddRegistrationFormRule)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AddRule(Guid eventId, Guid formId, Guid versionId, RegistrationFormRuleInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new AddRegistrationFormRuleCommand(eventId, formId, versionId, input.Ordinal, input.TargetNamespace, input.TargetKey, input.Effect, input.Condition, stamp), RouteNames.GetRegistrationFormVersion, _ => new { eventId, formId, versionId }, ct);

    [HttpPatch("registration-forms/{formId:guid}/versions/{versionId:guid}/rules/{ruleId:guid}", Name = RouteNames.UpdateRegistrationFormRule)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateRule(Guid eventId, Guid formId, Guid versionId, Guid ruleId, RegistrationFormRuleInput input, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new UpdateRegistrationFormRuleCommand(eventId, formId, versionId, ruleId, input.Ordinal, input.TargetNamespace, input.TargetKey, input.Effect, input.Condition, stamp), null, null, ct);

    [HttpDelete("registration-forms/{formId:guid}/versions/{versionId:guid}/rules/{ruleId:guid}", Name = RouteNames.DeleteRegistrationFormRule)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DeleteRule(Guid eventId, Guid formId, Guid versionId, Guid ruleId, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new DeleteRegistrationFormRuleCommand(eventId, formId, versionId, ruleId, stamp), null, null, ct);

    [HttpPost("registration-forms/{formId:guid}/versions/{versionId:guid}/preflight", Name = RouteNames.GetRegistrationFormPublishPreflight)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationFormPublishPreflightDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationFormPublishPreflightDto>>> Preflight(Guid eventId, Guid formId, Guid versionId, CancellationToken ct)
    {
        RegistrationFormPublishPreflightDto? preflight = await mediator.Send(new GetRegistrationFormPublishPreflightQuery(eventId, formId, versionId), ct);
        if (preflight is { CanPublish: false })
            return this.ToValidationProblem(RegistrationValidationProblem,
                preflight.Issues.Count == 0 ? "Registration form publication preflight failed." : string.Join("; ", preflight.Issues.Select(issue => $"{issue.Code}: {issue.Message}")),
                "registration_form_preflight_failed");
        return await ToResource(preflight, preflightAssembler);
    }

    [HttpPost("registration-forms/{formId:guid}/versions/{versionId:guid}/publish", Name = RouteNames.PublishRegistrationFormVersion)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> Publish(Guid eventId, Guid formId, Guid versionId, [FromHeader(Name = "If-Match"), Required] string? ifMatch, CancellationToken ct)
        => Send(ifMatch, stamp => new PublishRegistrationFormVersionCommand(eventId, formId, versionId, stamp), null, null, ct);

    [HttpGet("/api/registration-form-templates", Name = RouteNames.GetRegistrationFormTemplates)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationFormTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<RegistrationFormTemplateDto>>> GetTemplates(CancellationToken ct)
    {
        IReadOnlyList<RegistrationFormTemplateDto> templates = await mediator.Send(new ListRegistrationFormTemplatesQuery(), ct);
        var result = new ObjectResult(await templateAssembler.ToCollectionResource(templates, RouteNames.GetRegistrationFormTemplates, HttpContext)) { StatusCode = StatusCodes.Status200OK };
        result.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return result;
    }

    [HttpGet("/api/registration-form-templates/{templateId:guid}", Name = RouteNames.GetRegistrationFormTemplate)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationFormTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationFormTemplateDto>>> GetTemplate(Guid templateId, CancellationToken ct)
        => await ToResource(await mediator.Send(new GetRegistrationFormTemplateQuery(templateId), ct), templateAssembler);

    [HttpPost("/api/registration-form-templates", Name = RouteNames.CreateRegistrationFormTemplate)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateTemplate(RegistrationFormTemplateInputDto input, CancellationToken ct)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(new CreateRegistrationFormTemplateCommand(input), ct);
        return response.IsSuccess
            ? CreatedAtRoute(RouteNames.GetRegistrationFormTemplate, new { templateId = response.Id }, response)
            : this.ToCommandValidationProblem(response, RegistrationValidationProblem);
    }

    [HttpPost("/api/registration-form-templates/{templateId:guid}/instantiate", Name = RouteNames.InstantiateRegistrationFormTemplate)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> InstantiateTemplate(Guid templateId, InstantiateRegistrationFormTemplateInputDto input, CancellationToken ct)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(new InstantiateRegistrationFormTemplateCommand(templateId, input), ct);
        return response.IsSuccess
            ? CreatedAtRoute(RouteNames.GetRegistrationForm, new { eventId = input.EventId, formId = response.Id }, response)
            : this.ToCommandValidationProblem(response, RegistrationValidationProblem);
    }

    private async Task<ActionResult<HalResource<T>>> ToResource<T>(T? dto, IResourceAssembler<T, T> assembler) where T : class
    {
        if (dto is null) return this.ToNotFoundProblem(NotFoundProblem);
        var result = new ObjectResult(await assembler.ToResource(dto, HttpContext)) { StatusCode = StatusCodes.Status200OK };
        result.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return result;
    }

    private async Task<ActionResult<BaseCommandResponse<Guid>>> Send<T>(string? ifMatch, Func<Guid, T> command, string? createdRoute, Func<Guid, object>? createdValues, CancellationToken ct)
        where T : IRequest<BaseCommandResponse<Guid>>
    {
        if (!TryParseConcurrencyStamp(ifMatch, out Guid stamp))
            return this.ToValidationProblem(RegistrationValidationProblem, "If-Match must be a strong quoted non-empty GUID concurrency stamp.");
        BaseCommandResponse<Guid> response = await mediator.Send(command(stamp), ct);
        if (!response.IsSuccess) return this.ToCommandValidationProblem(response, RegistrationValidationProblem);
        return createdRoute is null ? Ok(response) : CreatedAtRoute(createdRoute, createdValues!(response.Id), response);
    }

    private async Task<ActionResult<HalResource<RegistrationFormVersionDto>>> Reorder<T>(
        string? ifMatch,
        Func<Guid, T> command,
        Guid eventId,
        Guid formId,
        Guid versionId,
        CancellationToken ct)
        where T : IRequest<BaseCommandResponse<Guid>>
    {
        if (!TryParseConcurrencyStamp(ifMatch, out Guid stamp))
            return this.ToValidationProblem(RegistrationValidationProblem,
                "If-Match must be a strong quoted non-empty GUID concurrency stamp.",
                "registration_form_reorder_invalid");
        BaseCommandResponse<Guid> response = await mediator.Send(command(stamp), ct);
        if (!response.IsSuccess) return this.ToCommandValidationProblem(response, RegistrationValidationProblem);
        RegistrationFormVersionDto? version = await mediator.Send(
            new GetRegistrationFormVersionQuery(eventId, formId, versionId), ct);
        return await ToResource(version, versionAssembler);
    }
}

public sealed record RegistrationWorkflowInput(string Purpose);
public sealed record RegistrationRequirementInput(int Ordinal, int CriticalityId, bool CanSkip, int CompletionEffectId, int AnswerSyncModeId, int AppliesToSubjectTypeId, Guid? AppliesToSubjectId);
public sealed record RegistrationFormInput(string Namespace, string Key, string Name, string LanguageTag);
public sealed record RegistrationFormVersionInput(Guid? CloneFromVersionId, string LanguageTag);
public sealed record RegistrationFormSectionInput(int Ordinal, string Title);
public sealed record RegistrationFormReorderInput(IReadOnlyList<Guid> OrderedIds);
public sealed record RegistrationFormFieldCreateInput(int Ordinal, string Namespace, string Key, string Label, int FieldTypeId, int RetentionPolicyId, int OrganizerVisibilityId, bool RequiresExplicitConsent, bool IsProviderTransferAllowed, bool IsExportable, string? ExportPurposeCode, bool IsAnalyticsRelevant, bool IsOperationallyFilterable, string? ConsentPurposeCode, string? ConsentTextVersion, string? ConsentText);
public sealed record RegistrationFormFieldUpdateInput(int Ordinal, string Label, int RetentionPolicyId, int OrganizerVisibilityId, bool RequiresExplicitConsent, bool IsProviderTransferAllowed, bool IsExportable, string? ExportPurposeCode, bool IsAnalyticsRelevant, bool IsOperationallyFilterable, string? ConsentPurposeCode, string? ConsentTextVersion, string? ConsentText, bool IsRequired, bool IsMulti, int? MinLength, int? MaxLength, string? RegexPattern, decimal? MinNumber, decimal? MaxNumber, DateTimeOffset? MinDateTime, DateTimeOffset? MaxDateTime, string? AllowedUrlSchemes);
public sealed record RegistrationFormOptionInput(int Ordinal, string Key, string Label);
public sealed record RegistrationFormRuleInput(int Ordinal, string TargetNamespace, string TargetKey, int Effect, RegistrationFormConditionInputDto Condition);
