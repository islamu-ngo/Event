using System.Diagnostics.Metrics;
using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationReportControllerTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IResourceAssembler<ModerationReportDetailDto, ModerationReportQueueItemDto> _resourceAssembler =
        Substitute.For<IResourceAssembler<ModerationReportDetailDto, ModerationReportQueueItemDto>>();

    public ModerationReportControllerTests()
    {
        _tenantContext.TenantId.Returns(Guid.CreateVersion7());
    }

    [Test]
    public async Task Routes_UseStableNamesAuthorizationAndRateLimitPolicies()
    {
        var controllerType = typeof(ModerationReportController);
        var getQueue = controllerType.GetMethod(nameof(ModerationReportController.GetQueue))!;
        var getDetail = controllerType.GetMethod(nameof(ModerationReportController.GetDetail))!;
        var triage = controllerType.GetMethod(nameof(ModerationReportController.Triage))!;
        var assign = controllerType.GetMethod(nameof(ModerationReportController.Assign))!;
        var decide = controllerType.GetMethod(nameof(ModerationReportController.Decide))!;
        var executeDecision = controllerType.GetMethod(nameof(ModerationReportController.ExecuteDecision))!;

        await Assert.That(controllerType.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controllerType.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(controllerType.GetCustomAttribute<RouteAttribute>()?.Template).IsEqualTo("api/events/{eventId:guid}/moderation/reports");

        await AssertRoute(getQueue, typeof(HttpGetAttribute), null, RouteNames.GetModerationReportQueue);
        await Assert.That(GetRateLimitPolicy(getQueue)).IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);

        await AssertRoute(getDetail, typeof(HttpGetAttribute), "{reportId:guid}", RouteNames.GetModerationReportDetail);
        await Assert.That(GetRateLimitPolicy(getDetail)).IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);

        await AssertRoute(triage, typeof(HttpPostAttribute), "{reportId:guid}/triage", RouteNames.TriageModerationReport);
        await Assert.That(GetRateLimitPolicy(triage)).IsEqualTo(RateLimitingExtensions.WritePolicy);

        await AssertRoute(assign, typeof(HttpPostAttribute), "{reportId:guid}/assign", RouteNames.AssignModerationReport);
        await Assert.That(GetRateLimitPolicy(assign)).IsEqualTo(RateLimitingExtensions.WritePolicy);

        await AssertRoute(decide, typeof(HttpPostAttribute), "{reportId:guid}/decision", RouteNames.DecideModerationReport);
        await Assert.That(GetRateLimitPolicy(decide)).IsEqualTo(RateLimitingExtensions.WritePolicy);

        await AssertRoute(executeDecision, typeof(HttpPostAttribute), "{reportId:guid}/decision/execute", RouteNames.ExecuteModerationReportDecision);
        await Assert.That(GetRateLimitPolicy(executeDecision)).IsEqualTo(RateLimitingExtensions.WritePolicy);
    }

    [Test]
    public async Task GetQueue_DispatchesEventScopedQueryAndAssemblesHalCollection()
    {
        var eventId = Guid.CreateVersion7();
        var result = PaginatedResult<ModerationReportQueueItemDto>.Create([], 0, 2, 10);
        var halCollection = new HalCollectionResource<ModerationReportQueueItemDto>();
        _mediator.Send(Arg.Any<GetModerationReportQueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(result);
        _resourceAssembler.ToCollectionResource(
                result,
                RouteNames.GetModerationReportQueue,
                Arg.Any<object>(),
                Arg.Any<HttpContext>())
            .Returns(halCollection);
        var controller = CreateController();

        var response = await controller.GetQueue(
            eventId,
            new ModerationReportQueueQueryRequest
            {
                PageNumber = 2,
                PageSize = 10,
                Statuses = ["submitted,under_review"],
                CaseStatuses = ["open", "assigned"],
                Priority = "urgent",
                QueueCode = "policy",
                AssignedModeratorUserId = Guid.CreateVersion7(),
                SortBy = "updatedAt"
            },
            CancellationToken.None);

        var ok = response.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsSameReferenceAs(halCollection);
        await _mediator.Received(1).Send(
            Arg.Is<GetModerationReportQueueRequest>(request =>
                request.EventId == eventId &&
                request.PageNumber == 2 &&
                request.PageSize == 10 &&
                request.Statuses.Contains(EventReportStatus.Submitted) &&
                request.Statuses.Contains(EventReportStatus.UnderReview) &&
                request.CaseStatuses.Contains(EventReportCaseStatus.Open) &&
                request.CaseStatuses.Contains(EventReportCaseStatus.Assigned) &&
                request.Priority == EventReportPriority.Urgent &&
                request.QueueCode == "policy" &&
                request.SortBy == "updated_at"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WriteActions_MapRouteAndBodyToSecuredCommands()
    {
        var eventId = Guid.CreateVersion7();
        var reportId = Guid.CreateVersion7();
        var caseId = Guid.CreateVersion7();
        var stamp = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();
        var decisionId = Guid.CreateVersion7();
        var duplicateGroupId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<TriageEventReportCommand>(), Arg.Any<CancellationToken>()).Returns(Success(reportId));
        _mediator.Send(Arg.Any<AssignEventReportCommand>(), Arg.Any<CancellationToken>()).Returns(Success(reportId));
        _mediator.Send(Arg.Any<DecideEventReportCommand>(), Arg.Any<CancellationToken>()).Returns(Success(decisionId));
        _mediator.Send(Arg.Any<ExecuteReportDecisionCommand>(), Arg.Any<CancellationToken>()).Returns(Success(decisionId));
        var controller = CreateController();

        await controller.Triage(eventId, reportId, new TriageModerationReportRequestDto
        {
            CaseId = caseId,
            ExpectedCaseConcurrencyStamp = stamp,
            QueueCode = "policy",
            Priority = EventReportPriority.High
        }, CancellationToken.None);
        await controller.Assign(eventId, reportId, new AssignModerationReportRequestDto
        {
            CaseId = caseId,
            ExpectedCaseConcurrencyStamp = stamp,
            AssigneeUserId = assigneeId
        }, CancellationToken.None);
        await controller.Decide(eventId, reportId, new DecideModerationReportRequestDto
        {
            CaseId = caseId,
            ExpectedCaseConcurrencyStamp = stamp,
            DecisionKind = EventReportDecisionKind.Duplicate,
            ReasonCode = "duplicate",
            SafeNote = "Duplicate report.",
            DuplicateGroupId = duplicateGroupId
        }, CancellationToken.None);
        await controller.ExecuteDecision(eventId, reportId, new ExecuteModerationReportDecisionRequestDto
        {
            CaseId = caseId,
            DecisionId = decisionId,
            ExpectedCaseConcurrencyStamp = stamp,
            CorrelationId = "corr-report-decision"
        }, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<TriageEventReportCommand>(command =>
                command.EventId == eventId &&
                command.ReportId == reportId &&
                command.CaseId == caseId &&
                command.ExpectedCaseConcurrencyStamp == stamp &&
                command.QueueCode == "policy" &&
                command.Priority == EventReportPriority.High),
            Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<AssignEventReportCommand>(command =>
                command.EventId == eventId &&
                command.ReportId == reportId &&
                command.CaseId == caseId &&
                command.ExpectedCaseConcurrencyStamp == stamp &&
                command.AssigneeUserId == assigneeId),
            Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<DecideEventReportCommand>(command =>
                command.EventId == eventId &&
                command.ReportId == reportId &&
                command.CaseId == caseId &&
                command.ExpectedCaseConcurrencyStamp == stamp &&
                command.DecisionKind == EventReportDecisionKind.Duplicate &&
                command.ReasonCode == "duplicate" &&
                command.SafeNote == "Duplicate report." &&
                command.DuplicateGroupId == duplicateGroupId),
            Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<ExecuteReportDecisionCommand>(command =>
                command.EventId == eventId &&
                command.ReportId == reportId &&
                command.CaseId == caseId &&
                command.DecisionId == decisionId &&
                command.ExpectedCaseConcurrencyStamp == stamp &&
                command.CorrelationId == "corr-report-decision"),
            Arg.Any<CancellationToken>());
    }

    private ModerationReportController CreateController()
        => new(
            _mediator,
            _resourceAssembler,
            _tenantContext,
            CreateMetrics(),
            NullLogger<ModerationReportController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static BaseCommandResponse<Guid> Success(Guid id) => BaseCommandResponse.Success(id, "ok");

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new BusinessMetrics(meterFactory);
    }

    private static async Task AssertRoute(MethodInfo method, Type attributeType, string? template, string routeName)
    {
        var attribute = method.GetCustomAttributes().Single(value => value.GetType() == attributeType) as HttpMethodAttribute;
        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Template).IsEqualTo(template);
        await Assert.That(attribute.Name).IsEqualTo(routeName);
    }

    private static string? GetRateLimitPolicy(MethodInfo method)
        => method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;
}
