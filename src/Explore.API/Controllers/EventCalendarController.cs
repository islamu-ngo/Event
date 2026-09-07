using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.API.Services.Calendar;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.Events.Moderation;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Specifications.Events;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Controllers;

/// <summary>
/// Calendar export endpoints for an event, including attendee-scoped access.
/// </summary>
/// <remarks>
/// Split out of the original EventController by route capability. The route template is stated
/// explicitly rather than via the [controller] token so the public URLs are unchanged, and every action
/// keeps its original <c>Name = RouteNames.*</c>, which is what pins the generated operationId.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/Event")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventCalendarController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor EventNotFoundProblem = new(
        "Event not found",
        "Event not found.");

    private readonly IMediator _mediator;
    private readonly IEventCalendarFileBuilder _calendarFileBuilder;
    private readonly Explore.Application.Contracts.Infrastructure.IPublicUrlBuilder _publicUrlBuilder;

    private const string CalendarRetentionWarning =
        "Third-party calendar providers may retain imported event and location details after access is revoked or the event changes.";
    private const string CalendarRetentionWarningHeader = "X-Calendar-Retention-Warning";

    public EventCalendarController(
        IMediator mediator,
        IEventCalendarFileBuilder calendarFileBuilder,
        Explore.Application.Contracts.Infrastructure.IPublicUrlBuilder publicUrlBuilder)
    {
        _mediator = mediator;
        _calendarFileBuilder = calendarFileBuilder;
        _publicUrlBuilder = publicUrlBuilder;
    }

    /// <summary>
    /// Download an event as an iCalendar (.ics) file.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}/calendar", Name = RouteNames.GetEventCalendar)]
    [EndpointSummary("Download Event Calendar")]
    [EndpointDescription("Downloads a published public event as an RFC 5545 iCalendar file using only public-purpose location disclosure. Third-party calendar providers may retain imported event and location details after access is revoked or the event changes; the warning is also returned in X-Calendar-Retention-Warning.")]
    [Produces("text/calendar")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCalendar(Guid id, CancellationToken cancellationToken = default)
    {
        AddCalendarRetentionWarning();
        var export = await _mediator.Send(new GetEventCalendarExportRequest(id), cancellationToken);
        if (export is null)
        {
            return this.ToNotFoundProblem(EventNotFoundProblem);
        }

        Uri canonicalUrl = new(_publicUrlBuilder.GetEventUrl(export.EventId));
        string calendarContent = _calendarFileBuilder.Build(export, canonicalUrl);
        string fileName = $"{SanitizeCalendarFileName(export.Slug ?? export.Title)}.ics";

        return File(
            System.Text.Encoding.UTF8.GetBytes(calendarContent),
            "text/calendar; charset=utf-8",
            fileName);
    }

    /// <summary>
    /// Download registration-scoped attendee calendar details as an iCalendar (.ics) file.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{id:guid}/calendar/my-access", Name = RouteNames.GetAttendeeEventCalendar)]
    [EndpointSummary("Download Attendee Event Calendar")]
    [EndpointDescription("Downloads an RFC 5545 iCalendar file using attendee-purpose location disclosure after registration authorization. Third-party calendar providers may retain imported event and location details after access is revoked or the event changes; the warning is also returned in X-Calendar-Retention-Warning.")]
    [Produces("text/calendar")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttendeeCalendar(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        AddCalendarRetentionWarning();
        AttendeeEventCalendarExportDto? export = await _mediator.Send(
            new GetAttendeeEventCalendarExportRequest(id),
            cancellationToken);
        if (export is null)
        {
            return this.ToNotFoundProblem(EventNotFoundProblem);
        }

        Uri canonicalUrl = new(_publicUrlBuilder.GetEventUrl(export.EventId));
        string calendarContent = _calendarFileBuilder.Build(export, canonicalUrl);
        string fileName = $"{SanitizeCalendarFileName(export.Slug ?? export.Title)}.ics";

        return File(
            System.Text.Encoding.UTF8.GetBytes(calendarContent),
            "text/calendar; charset=utf-8",
            fileName);
    }

    private static string SanitizeCalendarFileName(string value)
    {
        string sanitized = string.Concat(value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));

        sanitized = string.Join(
            '-',
            sanitized.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.IsNullOrWhiteSpace(sanitized)
            ? "event"
            : sanitized.ToLowerInvariant();
    }
    private void AddCalendarRetentionWarning()
    {
        Response.Headers[CalendarRetentionWarningHeader] = CalendarRetentionWarning;
    }
}
