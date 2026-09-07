using System.Reflection;
using System.Text;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Services.Calendar;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Calendar;

public sealed class EventControllerCalendarTests
{
    private const string PrivateHomeCanary = "PRIVATE-HOME-CALENDAR-CANARY";
    private const string AddressCanary = "17 Confidential Crescent";
    private const string PostcodeCanary = "SECRET-1040";
    private const string CoordinateCanary = "50.84673,4.35247";
    private const string RoomCanary = "FAMILY-ROOM-CANARY";

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IPublicUrlBuilder _publicUrlBuilder = Substitute.For<IPublicUrlBuilder>();
    private readonly EventCalendarController _controller;

    public EventControllerCalendarTests()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            TraceIdentifier = "trace-calendar-test"
        };
        httpContext.Request.Path = "/api/event/22222222-3333-4444-5555-666666666666/calendar";

        _controller = new EventCalendarController(
            _mediator,
            new IcalNetEventCalendarFileBuilder(),
            _publicUrlBuilder)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Test]
    [Category("CalendarPrivacy")]
    public async Task PublicCalendarCharacterization_ReturnsSafeTextCalendarAttachment()
    {
        var eventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var export = new EventCalendarExportDto(
            eventId,
            "Pinned Public Calendar",
            "Pinned public description.",
            "Pinned Public Calendar!",
            new DateTimeOffset(2026, 7, 19, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 19, 17, 0, 0, TimeSpan.Zero),
            Location: Explore.Application.Contracts.LocationPrivacy.EventLocationDisclosureContract.PrivateHomePublicLabel);

        _mediator.Send(
                Arg.Is<GetEventCalendarExportRequest>(request => request.EventId == eventId),
                Arg.Any<CancellationToken>())
            .Returns(export);
        _publicUrlBuilder.GetEventUrl(eventId)
            .Returns("https://events.example.org/events/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        IActionResult result = await _controller.GetCalendar(eventId, CancellationToken.None);

        var file = result as FileContentResult;
        await Assert.That(file).IsNotNull();
        await Assert.That(file!.ContentType).IsEqualTo("text/calendar; charset=utf-8");
        await Assert.That(file.FileDownloadName).IsEqualTo("pinned-public-calendar.ics");

        string calendar = Encoding.UTF8.GetString(file.FileContents);
        await Assert.That(calendar).Contains("BEGIN:VCALENDAR");
        await Assert.That(calendar).Contains("END:VCALENDAR");
        await Assert.That(calendar).DoesNotContain(PrivateHomeCanary);
        await Assert.That(calendar).DoesNotContain(AddressCanary);
        await Assert.That(calendar).DoesNotContain(PostcodeCanary);
        await Assert.That(calendar).DoesNotContain(CoordinateCanary);
        await Assert.That(calendar).DoesNotContain(RoomCanary);
        await Assert.That(_controller.Response.Headers["X-Calendar-Retention-Warning"].ToString())
            .Contains("Third-party calendar providers may retain");
    }

    [Test]
    [Category("CalendarPrivacy")]
    public async Task PublicCalendarHandlerContract_RequiresPurposeLimitedLocationDisclosure()
    {
        Type[] constructorDependencies = typeof(
                Explore.Application.Features.Events.Handlers.Queries.GetEventCalendarExportRequestHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        await Assert.That(constructorDependencies.Contains(
                typeof(Explore.Application.Contracts.Services.IEventLocationDisclosureService)))
            .IsTrue();
    }

    [Test]
    [Category("CalendarPrivacy")]
    public async Task AttendeeCalendarContract_UsesSeparateAuthenticatedAction()
    {
        MethodInfo? action = typeof(EventCalendarController).GetMethod("GetAttendeeCalendar");

        await Assert.That(action).IsNotNull();
        await Assert.That(action!.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        HttpGetAttribute? route = action.GetCustomAttribute<HttpGetAttribute>();
        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo("{id:guid}/calendar/my-access");
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetAttendeeEventCalendar);
    }

    [Test]
    [Category("CalendarPrivacy")]
    public async Task GetAttendeeCalendar_ReturnsExactAuthorizedLocationOnSeparateContract()
    {
        var eventId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        var export = new AttendeeEventCalendarExportDto(
            eventId,
            "Attendee Calendar",
            "Registration-scoped description.",
            "attendee-calendar",
            new DateTimeOffset(2026, 7, 19, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 19, 17, 0, 0, TimeSpan.Zero),
            $"{PrivateHomeCanary}, {RoomCanary}, {AddressCanary}, {PostcodeCanary}, {CoordinateCanary}");
        _mediator.Send(
                Arg.Is<GetAttendeeEventCalendarExportRequest>(request => request.EventId == eventId),
                Arg.Any<CancellationToken>())
            .Returns(export);
        _publicUrlBuilder.GetEventUrl(eventId)
            .Returns("https://events.example.org/events/bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

        IActionResult result = await _controller.GetAttendeeCalendar(eventId, CancellationToken.None);

        var file = result as FileContentResult;
        await Assert.That(file).IsNotNull();
        await Assert.That(file!.ContentType).IsEqualTo("text/calendar; charset=utf-8");
        string calendar = Encoding.UTF8.GetString(file.FileContents);
        string unfoldedCalendar = calendar.Replace("\r\n ", string.Empty, StringComparison.Ordinal);
        await Assert.That(unfoldedCalendar).Contains(PrivateHomeCanary);
        await Assert.That(unfoldedCalendar).Contains(RoomCanary);
        await Assert.That(unfoldedCalendar).Contains(AddressCanary);
        await Assert.That(unfoldedCalendar).Contains(PostcodeCanary);
        await Assert.That(unfoldedCalendar).Contains(CoordinateCanary.Replace(",", "\\,", StringComparison.Ordinal));
        await Assert.That(_controller.Response.Headers["X-Calendar-Retention-Warning"].ToString())
            .Contains("Third-party calendar providers may retain");
    }

    [Test]
    public async Task GetCalendarForPublishedPublicEventReturnsTextCalendarAttachment()
    {
        var eventId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var export = new EventCalendarExportDto(
            eventId,
            "Community Iftar",
            "Bring a dish to share.",
            "Community Iftar 2026!",
            new DateTimeOffset(2026, 5, 1, 18, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 1, 20, 0, 0, TimeSpan.Zero),
            "Main Hall, Brussels");

        _mediator.Send(Arg.Is<GetEventCalendarExportRequest>(request => request.EventId == eventId), Arg.Any<CancellationToken>())
            .Returns(export);
        _publicUrlBuilder.GetEventUrl(eventId)
            .Returns("https://events.example.org/events/11111111-2222-3333-4444-555555555555");

        IActionResult result = await _controller.GetCalendar(eventId, CancellationToken.None);

        var file = result as FileContentResult;
        await Assert.That(file).IsNotNull();
        await Assert.That(file!.ContentType).IsEqualTo("text/calendar; charset=utf-8");
        await Assert.That(file.FileDownloadName).IsEqualTo("community-iftar-2026.ics");

        string calendar = Encoding.UTF8.GetString(file.FileContents);
        await Assert.That(calendar).Contains("BEGIN:VCALENDAR");
        await Assert.That(calendar).Contains("BEGIN:VEVENT");
        await Assert.That(calendar).Contains($"UID:{eventId:D}");
        await Assert.That(calendar).Contains("SUMMARY:Community Iftar");
        await Assert.That(calendar).Contains("DTSTART:20260501T183000Z");
        await Assert.That(calendar).Contains("DTEND:20260501T200000Z");
        await Assert.That(calendar).Contains("LOCATION:Main Hall\\, Brussels");
        await Assert.That(calendar).Contains("URL:https://events.example.org/events/11111111-2222-3333-4444-555555555555");
    }

    [Test]
    public async Task GetCalendarWhenExportIsUnavailableReturnsNotFound()
    {
        var eventId = Guid.Parse("22222222-3333-4444-5555-666666666666");
        _mediator.Send(Arg.Is<GetEventCalendarExportRequest>(request => request.EventId == eventId), Arg.Any<CancellationToken>())
            .Returns((EventCalendarExportDto?)null);

        IActionResult result = await _controller.GetCalendar(eventId, CancellationToken.None);

        await Assert.That(result).IsTypeOf<ObjectResult>();
        var objectResult = (ObjectResult)result;
        await Assert.That(objectResult.StatusCode).IsEqualTo(404);
    }
}
