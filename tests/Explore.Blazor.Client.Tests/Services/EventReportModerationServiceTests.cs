using Explore.Blazor.Client.Contracts.Services.EventReporting;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class EventReportModerationServiceTests
{
    private readonly IModerationReportClient _apiClient = Substitute.For<IModerationReportClient>();
    private readonly ILogger<EventReportModerationService> _logger = Substitute.For<ILogger<EventReportModerationService>>();

    [Test]
    public async Task GetQueueAsync_WhenApiSucceeds_ReturnsPagedHalQueueResources()
    {
        var eventId = Guid.NewGuid();
        var assignedModeratorId = Guid.NewGuid();
        var report = CreateQueueResource(eventId, "submitted", "Submitted", "urgent", "Urgent");
        _apiClient.GetModerationReportQueueAsync(
                Arg.Any<Guid>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfModerationReportQueueItemDto
            {
                PageNumber = 2,
                PageSize = 20,
                TotalCount = 41,
                TotalPages = 3,
                HasPrevious = true,
                HasNext = true,
                _embedded = new HalCollectionEmbeddedOfModerationReportQueueItemDto
                {
                    Items = [report]
                }
            });

        var result = await CreateService().GetQueueAsync(
            eventId,
            new ModerationReportQueueQueryState
            {
                StatusCode = "submitted",
                CaseStatusCode = "open",
                PriorityCode = "urgent",
                QueueCode = "safety",
                AssignedModeratorUserId = assignedModeratorId,
                UnassignedOnly = true,
                OpenOnly = true,
                ReasonCode = "spam",
                SortBy = "priority",
                SortDescending = false,
                PageNumber = 2,
                PageSize = 20
            },
            CancellationToken.None);

        await Assert.That(result.PageNumber).IsEqualTo(2);
        await Assert.That(result.PageSize).IsEqualTo(20);
        await Assert.That(result.TotalCount).IsEqualTo(41);
        await Assert.That(result.TotalPages).IsEqualTo(3);
        await Assert.That(result.HasPrevious).IsTrue();
        await Assert.That(result.HasNext).IsTrue();
        await Assert.That(result.Reports.Count).IsEqualTo(1);
        await Assert.That(result.Reports[0].Id).IsEqualTo(report.Id);

        await _apiClient.Received(1).GetModerationReportQueueAsync(
            eventId,
            Arg.Is<IEnumerable<string>?>(values => values != null && values.SequenceEqual(new[] { "submitted" })),
            Arg.Is<IEnumerable<string>?>(values => values != null && values.SequenceEqual(new[] { "open" })),
            "urgent",
            "safety",
            assignedModeratorId,
            true,
            true,
            "spam",
            "priority",
            false,
            2,
            20,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetQueueAsync_WhenApiThrows_ReturnsNormalizedEmptyPage()
    {
        var eventId = Guid.NewGuid();
        _apiClient.GetModerationReportQueueAsync(
                Arg.Any<Guid>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        var result = await CreateService().GetQueueAsync(
            eventId,
            new ModerationReportQueueQueryState { PageNumber = 3, PageSize = 15 },
            CancellationToken.None);

        await Assert.That(result.Reports).IsEmpty();
        await Assert.That(result.PageNumber).IsEqualTo(3);
        await Assert.That(result.PageSize).IsEqualTo(15);
        await Assert.That(result.TotalCount).IsEqualTo(0);
        await Assert.That(result.HasPrevious).IsFalse();
        await Assert.That(result.HasNext).IsFalse();
    }

    [Test]
    public async Task GetDetailAsync_WhenReportNotFound_ReturnsNull()
    {
        var eventId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        _apiClient.GetModerationReportDetailAsync(
                eventId,
                reportId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));

        var result = await CreateService().GetDetailAsync(eventId, reportId, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TriageAsync_WhenApiSucceeds_ForwardsTriageRequest()
    {
        var eventId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var expectedStamp = Guid.NewGuid();
        _apiClient.TriageModerationReportAsync(
                eventId,
                reportId,
                Arg.Any<TriageModerationReportRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = reportId, Message = "Triaged" });

        var result = await CreateService().TriageAsync(
            eventId,
            reportId,
            new TriageModerationReportRequestDto
            {
                CaseId = caseId,
                ExpectedCaseConcurrencyStamp = expectedStamp,
                QueueCode = "safety",
                Priority = EventReportPriority.Urgent
            },
            CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(reportId);
        await _apiClient.Received(1).TriageModerationReportAsync(
            eventId,
            reportId,
            Arg.Is<TriageModerationReportRequestDto>(request =>
                request.CaseId == caseId &&
                request.ExpectedCaseConcurrencyStamp == expectedStamp &&
                request.QueueCode == "safety" &&
                request.Priority == EventReportPriority.Urgent),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DecideAsync_WhenApiReturnsCommandFailure_ReturnsFailureResult()
    {
        var eventId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var rejectedResponse = new BaseCommandResponseOfGuid
        {
            Success = false,
            Message = "Refresh the report and try again.",
            FailureCode = "report_case_concurrency_conflict",
            Errors = ["The report case changed."]
        };
        _apiClient.DecideModerationReportAsync(
                eventId,
                reportId,
                Arg.Any<DecideModerationReportRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException<BaseCommandResponseOfGuid>(
                "Conflict",
                409,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                rejectedResponse,
                null));

        var result = await CreateService().DecideAsync(
            eventId,
            reportId,
            new DecideModerationReportRequestDto
            {
                CaseId = Guid.NewGuid(),
                ExpectedCaseConcurrencyStamp = Guid.NewGuid(),
                DecisionKind = EventReportDecisionKind.NoViolation,
                ReasonCode = "spam",
                SafeNote = "safe moderator note"
            },
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).IsEqualTo("Refresh the report and try again.");
        await Assert.That(result.FailureCode).IsEqualTo("report_case_concurrency_conflict");
        await Assert.That(result.Errors).Contains("The report case changed.");
    }

    private EventReportModerationService CreateService() => new(_apiClient, _logger);

    private static HalResourceOfModerationReportQueueItemDto CreateQueueResource(
        Guid eventId,
        string statusCode,
        string statusName,
        string priorityCode,
        string priorityName)
    {
        var reportId = Guid.NewGuid();
        return HalLinkTestFactory.WithLinks(new HalResourceOfModerationReportQueueItemDto
        {
            Id = reportId,
            EventId = eventId,
            ReporterKindId = 1,
            ReporterKindCode = "user",
            ReporterKindName = "User",
            SourceKindId = 1,
            SourceKindCode = "web",
            SourceKindName = "Web",
            StatusId = 1,
            StatusCode = statusCode,
            StatusName = statusName,
            PriorityId = 4,
            PriorityCode = priorityCode,
            PriorityName = priorityName,
            ReasonId = 1,
            ReasonCode = "spam",
            ReasonName = "Spam",
            ReportCaseUpdatesConsent = true,
            ReportFollowUpContactConsent = false,
            SubmittedAtUtc = TestTime.UtcNow,
            CurrentCase = new CurrentCase2
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                QueueCode = "safety",
                StatusId = 1,
                StatusCode = "open",
                StatusName = "Open",
                PriorityId = 4,
                PriorityCode = priorityCode,
                PriorityName = priorityName,
                CreatedAtUtc = TestTime.UtcNow,
                ConcurrencyStamp = Guid.NewGuid()
            },
            DecisionCount = 1,
            SignalCount = 2,
            ExternalLinkCount = 1
        }, new HalLinkTestLink("self", "/api/events/event/moderation/reports/report", "GET"));
    }

    private static ApiException CreateApiException(string message, int statusCode)
    {
        return new ApiException(
            message,
            statusCode,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            new InvalidOperationException(message));
    }
}
