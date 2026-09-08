using System.Security.Cryptography;
using System.Text;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/event-reports")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class EventReportsController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor EventReportOptionsNotFoundProblem = new(
        "Event report options not found",
        "Event report options were not found.");

    private static readonly ApiNotFoundProblemDescriptor MyEventReportNotFoundProblem = new(
        "Event report not found",
        "Event report was not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventReportOptionsDto, EventReportOptionsDto> _optionsResourceAssembler;
    private readonly IResourceAssembler<MyEventReportDto, MyEventReportDto> _myReportResourceAssembler;
    private readonly ITenantContext _tenantContext;
    private readonly IOptions<EventReportSubmissionOptions> _submissionOptions;
    private readonly ILogger<EventReportsController> _logger;

    public EventReportsController(
        IMediator mediator,
        IResourceAssembler<EventReportOptionsDto, EventReportOptionsDto> optionsResourceAssembler,
        IResourceAssembler<MyEventReportDto, MyEventReportDto> myReportResourceAssembler,
        ITenantContext tenantContext,
        IOptions<EventReportSubmissionOptions> submissionOptions,
        ILogger<EventReportsController> logger)
    {
        _mediator = mediator;
        _optionsResourceAssembler = optionsResourceAssembler;
        _myReportResourceAssembler = myReportResourceAssembler;
        _tenantContext = tenantContext;
        _submissionOptions = submissionOptions;
        _logger = logger;
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("events/{eventId:guid}/options", Name = RouteNames.GetEventReportOptions)]
    [EndpointSummary("Get Event Report Options")]
    [EndpointDescription("Returns safe reporter-facing reportability state, input limits, and reason options for a single event.")]
    [ProducesResponseType(typeof(HalResource<EventReportOptionsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<EventReportOptionsDto>>> GetOptions(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var options = await _mediator.Send(new GetEventReportOptionsRequest { EventId = eventId }, cancellationToken);
        if (options is null)
        {
            return this.ToNotFoundProblem(EventReportOptionsNotFoundProblem);
        }

        var resource = await _optionsResourceAssembler.ToResource(options, HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.SubmitEventReport)]
    [EndpointSummary("Submit Event Report")]
    [EndpointDescription("Submits an authenticated report about a published event. Raw request fingerprints are hashed before the command leaves the API layer.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> Submit(
        [FromBody] SubmitEventReportDto request,
        CancellationToken cancellationToken = default) =>
        SubmitCore(
            request,
            EventReportSubmissionChannel.General,
            cancellationToken);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("corrections", Name = RouteNames.SubmitEventCorrection)]
    [EndpointSummary("Submit Event Correction")]
    [EndpointDescription("Submits a local correction request independently of general event-report intake and optional external reporting providers.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> SubmitCorrection(
        [FromBody] SubmitEventReportDto request,
        CancellationToken cancellationToken = default) =>
        SubmitCore(
            request,
            EventReportSubmissionChannel.Correction,
            cancellationToken);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(
        "unsafe-external-links",
        Name = RouteNames.SubmitUnsafeExternalLinkReport)]
    [EndpointSummary("Submit Unsafe External Link Report")]
    [EndpointDescription("Submits a local unsafe-link report independently of general event-report intake and optional external reporting providers.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> SubmitUnsafeExternalLink(
        [FromBody] SubmitEventReportDto request,
        CancellationToken cancellationToken = default) =>
        SubmitCore(
            request,
            EventReportSubmissionChannel.UnsafeExternalLink,
            cancellationToken);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(
        "legal-or-copyright-complaints",
        Name = RouteNames.SubmitLegalOrCopyrightComplaint)]
    [EndpointSummary("Submit Event Legal Or Copyright Complaint")]
    [EndpointDescription("Submits a local legal or copyright complaint independently of general event-report intake and optional external reporting providers.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> SubmitLegalOrCopyrightComplaint(
        [FromBody] SubmitEventReportDto request,
        CancellationToken cancellationToken = default) =>
        SubmitCore(
            request,
            EventReportSubmissionChannel.LegalOrCopyright,
            cancellationToken);

    private async Task<ActionResult<BaseCommandResponse<Guid>>> SubmitCore(
        SubmitEventReportDto request,
        EventReportSubmissionChannel submissionChannel,
        CancellationToken cancellationToken)
    {
        var command = new SubmitEventReportCommand
        {
            Request = request,
            SubmissionChannel = submissionChannel,
            ReporterIpHash = ComputeReporterFingerprintHash(
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                "ip"),
            ReporterUserAgentHash = ComputeReporterFingerprintHash(
                HttpContext.Request.Headers.UserAgent.ToString(),
                "user-agent"),
            CorrelationId = HttpContext.Items["CorrelationId"] as string ?? HttpContext.TraceIdentifier
        };

        var response = await _mediator.Send(command, cancellationToken);
        _logger.LogInformation(
            "Event report submission completed for event {EventId} report {ReportId} channel {SubmissionChannel} outcome {Outcome} failure {FailureCategory}",
            request.EventId,
            response.Id,
            submissionChannel,
            response.IsSuccess ? "succeeded" : "failed",
            response.FailureCode ?? "none");

        if (!response.IsSuccess)
        {
            return this.ToEventReportProblem(response);
        }

        return CreatedAtRoute(
            RouteNames.GetMyEventReport,
            new { reportId = response.Id },
            response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("my", Name = RouteNames.GetMyEventReports)]
    [EndpointSummary("Get My Event Reports")]
    [EndpointDescription("Returns a paged reporter-owned event-report status list without evidence or moderation internals.")]
    [ProducesResponseType(typeof(HalCollectionResource<MyEventReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<MyEventReportDto>>> GetMyReports(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = PaginatedResult<MyEventReportDto>.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetMyReportsRequest
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken);

        var resource = await _myReportResourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetMyEventReports,
            additionalRouteValues: null,
            HttpContext);

        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("my/{reportId:guid}", Name = RouteNames.GetMyEventReport)]
    [EndpointSummary("Get My Event Report")]
    [EndpointDescription("Returns a limited reporter-owned event-report status projection without evidence or moderation internals.")]
    [ProducesResponseType(typeof(HalResource<MyEventReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalResource<MyEventReportDto>>> GetMyReport(
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        var report = await _mediator.Send(new GetMyReportRequest { ReportId = reportId }, cancellationToken);
        if (report is null)
        {
            return this.ToNotFoundProblem(MyEventReportNotFoundProblem);
        }

        var resource = await _myReportResourceAssembler.ToResource(report, HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch(
        "my/{reportId:guid}/communication-consent",
        Name = RouteNames.UpdateMyEventReportCommunicationConsent)]
    [EndpointSummary("Update My Event Report Communication Consent")]
    [EndpointDescription("Updates case-update and follow-up communication consent on the authenticated reporter's own event report.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(HalResource<MyEventReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult<HalResource<MyEventReportDto>>> UpdateCommunicationConsent(
        Guid reportId,
        [FromBody] UpdateMyReportCommunicationConsentDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new UpdateMyReportCommunicationConsentCommand
            {
                ReportId = reportId,
                Request = request
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToEventReportProblem(response);
        }

        var report = await _mediator.Send(
            new GetMyReportRequest { ReportId = reportId },
            cancellationToken);
        if (report is null)
        {
            return this.ToNotFoundProblem(MyEventReportNotFoundProblem);
        }

        var resource = await _myReportResourceAssembler.ToResource(report, HttpContext);
        return Ok(resource);
    }

    private string? ComputeReporterFingerprintHash(string? value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Length == 0)
        {
            return null;
        }

        var material = $"{_tenantContext.TenantId:N}:{kind}:{normalizedValue}";
        var materialBytes = Encoding.UTF8.GetBytes(material);
        var pepper = _submissionOptions.Value.ReporterFingerprintPepper;
        var hash = string.IsNullOrWhiteSpace(pepper)
            ? SHA256.HashData(materialBytes)
            : HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper.Trim()), materialBytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
