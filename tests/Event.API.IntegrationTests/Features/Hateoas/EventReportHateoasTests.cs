using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class EventReportHateoasTests
{
    [Test]
    public async Task ReportableOptions_ExposeSelfEventAndAuthenticatedSubmitLinks()
    {
        var eventId = Guid.CreateVersion7();
        var dto = new EventReportOptionsDto
        {
            EventId = eventId,
            IsReportable = true,
            MaxReporterTextLength = 4000
        };

        var links = new EventReportOptionsDetailLinkPolicy()
            .GetLinks(dto, user: null)
            .ToList();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetEventReportOptions);
        await Assert.That(new RouteValueDictionary(self.RouteValues)["eventId"]).IsEqualTo(eventId);

        var @event = links.Single(link => link.Rel == LinkRelations.Event);
        await Assert.That(@event.RouteName).IsEqualTo(RouteNames.GetEventById);
        await Assert.That(new RouteValueDictionary(@event.RouteValues)["id"]).IsEqualTo(eventId);

        var submit = links.Single(link => link.Rel == LinkRelations.ReportEvent);
        await Assert.That(submit.RouteName).IsEqualTo(RouteNames.SubmitEventReport);
        await Assert.That(submit.Method).IsEqualTo("POST");
        await Assert.That(submit.RequiresAuth).IsTrue();
        await Assert.That(submit.AdvertiseWhenAnonymous).IsTrue();
    }

    [Test]
    public async Task NonReportableOptions_DoNotExposeSubmitLink()
    {
        var dto = new EventReportOptionsDto
        {
            EventId = Guid.CreateVersion7(),
            IsReportable = false,
            MaxReporterTextLength = 4000,
            UnavailableReasonCode = "event_not_reportable"
        };

        var links = new EventReportOptionsDetailLinkPolicy()
            .GetLinks(dto, user: null)
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.ReportEvent)).IsFalse();
    }

    [Test]
    public async Task MyReportStatus_ExposesAuthenticatedSelfAndPublicEventLinks()
    {
        var reportId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.UserId.Returns(userId);
        var dto = new MyEventReportDto
        {
            Id = reportId,
            EventId = eventId,
            StatusId = 1,
            StatusCode = "submitted",
            StatusName = "Submitted",
            ReasonCode = "spam",
            ReasonName = "Spam",
            SubmittedAtUtc = DateTime.UtcNow,
            ReportCaseUpdatesConsent = true,
            ReportFollowUpContactConsent = false
        };

        var links = new MyEventReportDetailLinkPolicy(currentUserService)
            .GetLinks(dto, user: null)
            .ToList();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetMyEventReport);
        await Assert.That(new RouteValueDictionary(self.RouteValues)["reportId"]).IsEqualTo(reportId);
        await Assert.That(self.RequiresAuth).IsTrue();

        var @event = links.Single(link => link.Rel == LinkRelations.Event);
        await Assert.That(@event.RouteName).IsEqualTo(RouteNames.GetEventById);
        await Assert.That(new RouteValueDictionary(@event.RouteValues)["id"]).IsEqualTo(eventId);
        await Assert.That(@event.RequiresAuth).IsFalse();

        var updateConsent = links.Single(link => link.Rel == LinkRelations.UpdateCommunicationConsent);
        await Assert.That(updateConsent.RouteName).IsEqualTo(RouteNames.UpdateMyEventReportCommunicationConsent);
        await Assert.That(new RouteValueDictionary(updateConsent.RouteValues)["reportId"]).IsEqualTo(reportId);
        await Assert.That(updateConsent.Method).IsEqualTo("PATCH");
        await Assert.That(updateConsent.RequiresAuth).IsTrue();
        await Assert.That(updateConsent.PermissionResourceKind).IsEqualTo(ResourceKinds.User);
        await Assert.That(updateConsent.PermissionAction).IsEqualTo(AuthorizationActions.Users.Update);
        await Assert.That(updateConsent.PermissionResourceId).IsEqualTo(userId.ToString());
    }

    [Test]
    public async Task MyReportCollectionItem_UsesTheSameOwnerConsentAffordance()
    {
        var reportId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.UserId.Returns(userId);
        var dto = CreateMyReport(reportId);

        var updateConsent = new MyEventReportCollectionLinkPolicy(currentUserService)
            .GetItemLinks(dto, user: null)
            .Single(link => link.Rel == LinkRelations.UpdateCommunicationConsent);

        await Assert.That(updateConsent.RouteName).IsEqualTo(RouteNames.UpdateMyEventReportCommunicationConsent);
        await Assert.That(new RouteValueDictionary(updateConsent.RouteValues)["reportId"]).IsEqualTo(reportId);
        await Assert.That(updateConsent.Method).IsEqualTo("PATCH");
        await Assert.That(updateConsent.PermissionResourceKind).IsEqualTo(ResourceKinds.User);
        await Assert.That(updateConsent.PermissionResourceId).IsEqualTo(userId.ToString());
    }

    [Test]
    public async Task MyReportStatus_WhenCurrentUserCannotBeResolved_OmitsConsentAffordance()
    {
        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.UserId.Returns((Guid?)null);

        var detailLinks = new MyEventReportDetailLinkPolicy(currentUserService)
            .GetLinks(CreateMyReport(Guid.CreateVersion7()), user: null)
            .ToList();
        var collectionLinks = new MyEventReportCollectionLinkPolicy(currentUserService)
            .GetItemLinks(CreateMyReport(Guid.CreateVersion7()), user: null)
            .ToList();

        await Assert.That(detailLinks.Any(link => link.Rel == LinkRelations.UpdateCommunicationConsent)).IsFalse();
        await Assert.That(collectionLinks.Any(link => link.Rel == LinkRelations.UpdateCommunicationConsent)).IsFalse();
    }

    [Test]
    public async Task ModerationQueueItem_OpenSubmittedReport_ExposesTriageAndAssignLinks()
    {
        var reportId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var dto = CreateQueueItem(reportId, eventId, "submitted", "open", decisionCount: 0);

        var links = new ModerationReportQueueCollectionLinkPolicy()
            .GetItemLinks(dto, user: null)
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.TriageReport)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.AssignReport)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.DecideReport)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.ExecuteReportDecision)).IsFalse();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetModerationReportDetail);
        await Assert.That(new RouteValueDictionary(self.RouteValues)["eventId"]).IsEqualTo(eventId);
        await Assert.That(new RouteValueDictionary(self.RouteValues)["reportId"]).IsEqualTo(reportId);
        await Assert.That(self.RequiresAuth).IsTrue();
    }

    [Test]
    public async Task ModerationDetail_AssignedReport_ExposesDecisionLinkOnlyForWorkflowActions()
    {
        var dto = CreateDetail(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "under_review",
            "assigned",
            decisions: []);

        var links = new ModerationReportDetailLinkPolicy()
            .GetLinks(dto, user: null)
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.TriageReport)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.AssignReport)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.DecideReport)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.ExecuteReportDecision)).IsFalse();
    }

    [Test]
    public async Task ModerationDetail_DecisionReadyReport_ExposesExecuteDecisionLink()
    {
        var decision = new ModerationReportDecisionDto
        {
            Id = Guid.CreateVersion7(),
            CaseId = Guid.CreateVersion7(),
            ReportId = Guid.CreateVersion7(),
            DecisionSourceId = 1,
            DecisionSourceCode = "local_moderator",
            DecisionSourceName = "LocalModerator",
            DecisionKindId = 5,
            DecisionKindCode = "light_moderate",
            DecisionKindName = "LightModerate",
            ReasonCode = "spam",
            CreatedAtUtc = DateTime.UtcNow
        };
        var dto = CreateDetail(
            decision.ReportId,
            Guid.CreateVersion7(),
            "under_review",
            "decision_ready",
            [decision]);

        var links = new ModerationReportDetailLinkPolicy()
            .GetLinks(dto, user: null)
            .ToList();

        var execute = links.Single(link => link.Rel == LinkRelations.ExecuteReportDecision);
        await Assert.That(execute.RouteName).IsEqualTo(RouteNames.ExecuteModerationReportDecision);
        await Assert.That(execute.Method).IsEqualTo("POST");
        await Assert.That(execute.RequiresAuth).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.DecideReport)).IsFalse();
    }

    [Test]
    public async Task ModerationQueueItem_TerminalReport_HidesMutatingLinks()
    {
        var dto = CreateQueueItem(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "actioned",
            "assigned",
            decisionCount: 1);

        var links = new ModerationReportQueueCollectionLinkPolicy()
            .GetItemLinks(dto, user: null)
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.TriageReport)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.AssignReport)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.DecideReport)).IsFalse();
    }

    private static ModerationReportQueueItemDto CreateQueueItem(
        Guid reportId,
        Guid eventId,
        string statusCode,
        string caseStatusCode,
        int decisionCount)
        => new()
        {
            Id = reportId,
            EventId = eventId,
            ReporterKindId = 1,
            ReporterKindCode = "user",
            ReporterKindName = "User",
            SourceKindId = 1,
            SourceKindCode = "local",
            SourceKindName = "Local",
            StatusId = 1,
            StatusCode = statusCode,
            StatusName = statusCode,
            PriorityId = 2,
            PriorityCode = "normal",
            PriorityName = "Normal",
            ReasonCode = "spam",
            ReasonName = "Spam",
            ReportCaseUpdatesConsent = true,
            ReportFollowUpContactConsent = false,
            SubmittedAtUtc = DateTime.UtcNow,
            CurrentCase = CreateCase(reportId, caseStatusCode),
            DecisionCount = decisionCount
        };

    private static MyEventReportDto CreateMyReport(Guid reportId) => new()
    {
        Id = reportId,
        EventId = Guid.CreateVersion7(),
        StatusId = 1,
        StatusCode = "submitted",
        StatusName = "Submitted",
        ReasonCode = "spam",
        ReasonName = "Spam",
        SubmittedAtUtc = DateTime.UtcNow,
        ReportCaseUpdatesConsent = true,
        ReportFollowUpContactConsent = false
    };

    private static ModerationReportDetailDto CreateDetail(
        Guid reportId,
        Guid eventId,
        string statusCode,
        string caseStatusCode,
        IReadOnlyList<ModerationReportDecisionDto> decisions)
        => new()
        {
            Id = reportId,
            EventId = eventId,
            ReporterKindId = 1,
            ReporterKindCode = "user",
            ReporterKindName = "User",
            SourceKindId = 1,
            SourceKindCode = "local",
            SourceKindName = "Local",
            StatusId = 1,
            StatusCode = statusCode,
            StatusName = statusCode,
            PriorityId = 2,
            PriorityCode = "normal",
            PriorityName = "Normal",
            ReasonCode = "spam",
            ReasonName = "Spam",
            ReportCaseUpdatesConsent = true,
            ReportFollowUpContactConsent = false,
            SubmittedAtUtc = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CurrentCase = CreateCase(reportId, caseStatusCode),
            Decisions = decisions
        };

    private static ModerationReportCaseDto CreateCase(Guid reportId, string statusCode)
        => new()
        {
            Id = Guid.CreateVersion7(),
            ReportId = reportId,
            QueueCode = "policy",
            StatusId = 1,
            StatusCode = statusCode,
            StatusName = statusCode,
            PriorityId = 2,
            PriorityCode = "normal",
            PriorityName = "Normal",
            CreatedAtUtc = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
}
