namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Hateoas;

public sealed class ModerationReportDetailLinkPolicy : ILinkPolicy<ModerationReportDetailDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ModerationReportDetailDto dto, ClaimsPrincipal? user)
    {
        foreach (var link in ModerationReportLinks.CreateReadLinks(dto.EventId, dto.Id))
        {
            yield return link;
        }

        foreach (var link in ModerationReportLinks.CreateActionLinks(
                     dto.EventId,
                     dto.Id,
                     dto.StatusCode,
                     dto.CurrentCase,
                     hasDecision: dto.Decisions.Count > 0))
        {
            yield return link;
        }
    }
}

public sealed class ModerationReportQueueCollectionLinkPolicy : ICollectionLinkPolicy<ModerationReportQueueItemDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ModerationReportQueueItemDto dto, ClaimsPrincipal? user)
    {
        foreach (var link in ModerationReportLinks.CreateReadLinks(dto.EventId, dto.Id))
        {
            yield return link;
        }

        foreach (var link in ModerationReportLinks.CreateActionLinks(
                     dto.EventId,
                     dto.Id,
                     dto.StatusCode,
                     dto.CurrentCase,
                     hasDecision: dto.DecisionCount > 0))
        {
            yield return link;
        }
    }
}

file static class ModerationReportLinks
{
    private const string SubmittedStatus = "submitted";
    private const string TriagedStatus = "triaged";
    private const string ActionedStatus = "actioned";
    private const string DismissedStatus = "dismissed";
    private const string DuplicateStatus = "duplicate";
    private const string ClosedStatus = "closed";
    private const string OpenCaseStatus = "open";
    private const string AssignedCaseStatus = "assigned";
    private const string DecisionReadyCaseStatus = "decision_ready";

    public static IEnumerable<LinkDefinition> CreateReadLinks(Guid eventId, Guid reportId)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetModerationReportDetail,
            new { eventId, reportId },
            "GET",
            "Moderation report detail",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ViewManagement, ResourceKinds.Event, eventId.ToString());

        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetModerationReportQueue,
            new { eventId },
            "GET",
            "Moderation report queue",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ViewManagement, ResourceKinds.Event, eventId.ToString());

        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventManagementDetails,
            new { id = eventId },
            "GET",
            "Event management detail",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ViewManagement, ResourceKinds.Event, eventId.ToString());
    }

    public static IEnumerable<LinkDefinition> CreateActionLinks(
        Guid eventId,
        Guid reportId,
        string reportStatusCode,
        ModerationReportCaseDto? currentCase,
        bool hasDecision)
    {
        if (currentCase is null)
        {
            yield break;
        }

        if (CanTriage(reportStatusCode, currentCase.StatusCode))
        {
            yield return CreateModerateLightAction(
                LinkRelations.TriageReport,
                RouteNames.TriageModerationReport,
                eventId,
                reportId,
                "Triage report");
        }

        if (CanAssign(reportStatusCode, currentCase.StatusCode))
        {
            yield return CreateModerateLightAction(
                LinkRelations.AssignReport,
                RouteNames.AssignModerationReport,
                eventId,
                reportId,
                "Assign report");
        }

        if (CanDecide(reportStatusCode, currentCase.StatusCode))
        {
            yield return CreateModerateLightAction(
                LinkRelations.DecideReport,
                RouteNames.DecideModerationReport,
                eventId,
                reportId,
                "Decide report");
        }

        if (CanExecute(currentCase.StatusCode, hasDecision))
        {
            yield return CreateModerateLightAction(
                LinkRelations.ExecuteReportDecision,
                RouteNames.ExecuteModerationReportDecision,
                eventId,
                reportId,
                "Execute report decision");
        }
    }

    private static LinkDefinition CreateModerateLightAction(
        string rel,
        string routeName,
        Guid eventId,
        Guid reportId,
        string title)
        => new LinkDefinition(
            rel,
            routeName,
            new { eventId, reportId },
            "POST",
            title,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ModerateLight, ResourceKinds.Event, eventId.ToString());

    private static bool CanTriage(string reportStatusCode, string caseStatusCode)
        => Is(caseStatusCode, OpenCaseStatus)
           && (Is(reportStatusCode, SubmittedStatus) || Is(reportStatusCode, TriagedStatus));

    private static bool CanAssign(string reportStatusCode, string caseStatusCode)
        => !IsTerminal(reportStatusCode)
           && (Is(caseStatusCode, OpenCaseStatus) || Is(caseStatusCode, AssignedCaseStatus));

    private static bool CanDecide(string reportStatusCode, string caseStatusCode)
        => !IsTerminal(reportStatusCode)
           && Is(caseStatusCode, AssignedCaseStatus);

    private static bool CanExecute(string caseStatusCode, bool hasDecision)
        => hasDecision && Is(caseStatusCode, DecisionReadyCaseStatus);

    private static bool IsTerminal(string reportStatusCode)
        => Is(reportStatusCode, ActionedStatus)
           || Is(reportStatusCode, DismissedStatus)
           || Is(reportStatusCode, DuplicateStatus)
           || Is(reportStatusCode, ClosedStatus);

    private static bool Is(string actual, string expected)
        => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
