namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Hateoas;

public sealed class EventReportOptionsDetailLinkPolicy : ILinkPolicy<EventReportOptionsDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventReportOptionsDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventReportOptions,
            new { eventId = dto.EventId },
            "GET",
            "Event report options");

        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            "Event details");

        if (dto.IsReportable)
        {
            yield return new LinkDefinition(
                LinkRelations.ReportEvent,
                RouteNames.SubmitEventReport,
                null,
                "POST",
                "Report event",
                RequiresAuth: true)
                .AdvertisedWhenAnonymous();
        }
    }
}

public sealed class EventReportOptionsCollectionLinkPolicy : ICollectionLinkPolicy<EventReportOptionsDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventReportOptionsDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventReportOptions,
            new { eventId = dto.EventId },
            "GET",
            "Event report options");

        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            "Event details");

        if (dto.IsReportable)
        {
            yield return new LinkDefinition(
                LinkRelations.ReportEvent,
                RouteNames.SubmitEventReport,
                null,
                "POST",
                "Report event",
                RequiresAuth: true)
                .AdvertisedWhenAnonymous();
        }
    }
}

public sealed class MyEventReportDetailLinkPolicy(ICurrentUserService currentUserService)
    : ILinkPolicy<MyEventReportDto>
{
    public IEnumerable<LinkDefinition> GetLinks(MyEventReportDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetMyEventReport,
            new { reportId = dto.Id },
            "GET",
            "My event report",
            RequiresAuth: true);

        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            "Event details");

        if (currentUserService.UserId is not { } userId)
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.UpdateCommunicationConsent,
            RouteNames.UpdateMyEventReportCommunicationConsent,
            new { reportId = dto.Id },
            "PATCH",
            "Update communication consent",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Users.Update, ResourceKinds.User, userId.ToString());
    }
}

public sealed class MyEventReportCollectionLinkPolicy(ICurrentUserService currentUserService)
    : ICollectionLinkPolicy<MyEventReportDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(MyEventReportDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetMyEventReport,
            new { reportId = dto.Id },
            "GET",
            "My event report",
            RequiresAuth: true);

        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            "Event details");

        if (currentUserService.UserId is not { } userId)
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.UpdateCommunicationConsent,
            RouteNames.UpdateMyEventReportCommunicationConsent,
            new { reportId = dto.Id },
            "PATCH",
            "Update communication consent",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Users.Update, ResourceKinds.User, userId.ToString());
    }
}
