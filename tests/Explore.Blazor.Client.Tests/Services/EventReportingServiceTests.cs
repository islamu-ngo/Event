using Explore.Blazor.Client.Contracts.Services.EventReporting;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class EventReportingServiceTests
{
    private readonly IEventReportsClient _apiClient = Substitute.For<IEventReportsClient>();
    private readonly ILogger<EventReportingService> _logger = Substitute.For<ILogger<EventReportingService>>();

    [Test]
    public async Task GetMyReportsAsync_WhenApiSucceeds_ReturnsPagedHalReportResources()
    {
        var report = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        _apiClient.GetMyEventReportsAsync(
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfMyEventReportDto
            {
                PageNumber = 2,
                PageSize = 10,
                TotalCount = 21,
                TotalPages = 3,
                HasPrevious = true,
                HasNext = true,
                _embedded = new HalCollectionEmbeddedOfMyEventReportDto
                {
                    Items = [report]
                }
            });

        var result = await CreateService().GetMyReportsAsync(2, 10, CancellationToken.None);

        await Assert.That(result.PageNumber).IsEqualTo(2);
        await Assert.That(result.PageSize).IsEqualTo(10);
        await Assert.That(result.TotalCount).IsEqualTo(21);
        await Assert.That(result.TotalPages).IsEqualTo(3);
        await Assert.That(result.HasPrevious).IsTrue();
        await Assert.That(result.HasNext).IsTrue();
        await Assert.That(result.Reports.Count).IsEqualTo(1);
        await Assert.That(result.Reports[0].Id).IsEqualTo(report.Id);
        await Assert.That(result.Reports[0].ReasonCode).IsEqualTo("spam");

        await _apiClient.Received(1).GetMyEventReportsAsync(
            2,
            10,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetMyReportsAsync_WhenApiThrows_ReturnsNormalizedEmptyPage()
    {
        _apiClient.GetMyEventReportsAsync(
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        var result = await CreateService().GetMyReportsAsync(3, 15, CancellationToken.None);

        await Assert.That(result.Reports).IsEmpty();
        await Assert.That(result.PageNumber).IsEqualTo(3);
        await Assert.That(result.PageSize).IsEqualTo(15);
        await Assert.That(result.TotalCount).IsEqualTo(0);
        await Assert.That(result.HasPrevious).IsFalse();
        await Assert.That(result.HasNext).IsFalse();
    }

    [Test]
    public async Task GetMyReportAsync_WhenReportNotFound_ReturnsNull()
    {
        var reportId = Guid.NewGuid();
        _apiClient.GetMyEventReportAsync(
                reportId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));

        var result = await CreateService().GetMyReportAsync(reportId, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task UpdateCommunicationConsentAsync_WhenAffordanceMatches_SendsExplicitChoicesAndReturnsResource()
    {
        var report = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        var reportId = report.Id!.Value;
        HalLinkTestFactory.WithLinks(
            report,
            new HalLinkTestLink(
                "update-communication-consent",
                $"/api/event-reports/my/{reportId}/communication-consent",
                "PATCH"));
        UpdateMyReportCommunicationConsentDto? capturedRequest = null;
        var updated = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        updated.Id = reportId;
        updated.ReportCaseUpdatesConsent = false;
        updated.ReportFollowUpContactConsent = true;
        HalLinkTestFactory.WithLinks(
            updated,
            new HalLinkTestLink(
                "update-communication-consent",
                $"/api/event-reports/my/{reportId}/communication-consent",
                "PATCH"));
        _apiClient.UpdateMyEventReportCommunicationConsentAsync(
                reportId,
                Arg.Do<UpdateMyReportCommunicationConsentDto>(request => capturedRequest = request),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(updated);

        var result = await CreateService().UpdateCommunicationConsentAsync(
            report,
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: true,
            CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Report).IsSameReferenceAs(updated);
        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.Consent!.ReportCaseUpdatesConsent).IsFalse();
        await Assert.That(capturedRequest.Consent.ReportFollowUpContactConsent).IsTrue();
        await Assert.That(result.Report!.HasLink("update-communication-consent")).IsTrue();
    }

    [Test]
    public async Task UpdateCommunicationConsentAsync_WhenAffordanceTargetsAnotherReport_FailsClosed()
    {
        var report = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        HalLinkTestFactory.WithLinks(
            report,
            new HalLinkTestLink(
                "update-communication-consent",
                $"/api/event-reports/my/{Guid.NewGuid()}/communication-consent",
                "PATCH"));

        var result = await CreateService().UpdateCommunicationConsentAsync(
            report,
            reportCaseUpdatesConsent: true,
            reportFollowUpContactConsent: true,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await _apiClient.DidNotReceive().UpdateMyEventReportCommunicationConsentAsync(
            Arg.Any<Guid>(),
            Arg.Any<UpdateMyReportCommunicationConsentDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments(false, "PATCH")]
    [Arguments(true, "POST")]
    [Arguments(true, "put")]
    [Arguments(true, "Put")]
    public async Task UpdateCommunicationConsentAsync_WhenRelationOrPatchMethodIsMissing_FailsClosed(
        bool includeRelation,
        string method)
    {
        var report = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        if (includeRelation)
        {
            HalLinkTestFactory.WithLinks(
                report,
                new HalLinkTestLink(
                    "update-communication-consent",
                    $"/api/event-reports/my/{report.Id}/communication-consent",
                    method));
        }

        var result = await CreateService().UpdateCommunicationConsentAsync(
            report,
            reportCaseUpdatesConsent: true,
            reportFollowUpContactConsent: true,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await _apiClient.DidNotReceive().UpdateMyEventReportCommunicationConsentAsync(
            Arg.Any<Guid>(),
            Arg.Any<UpdateMyReportCommunicationConsentDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateCommunicationConsentAsync_WhenExpectedIdAppearsInAnExtraSegment_FailsClosed()
    {
        var report = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        var reportId = report.Id!.Value;
        HalLinkTestFactory.WithLinks(
            report,
            new HalLinkTestLink(
                "update-communication-consent",
                $"/api/{reportId}/event-reports/my/{Guid.NewGuid()}/communication-consent",
                "PATCH"));

        var result = await CreateService().UpdateCommunicationConsentAsync(
            report,
            reportCaseUpdatesConsent: true,
            reportFollowUpContactConsent: true,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await _apiClient.DidNotReceive().UpdateMyEventReportCommunicationConsentAsync(
            Arg.Any<Guid>(),
            Arg.Any<UpdateMyReportCommunicationConsentDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateCommunicationConsentAsync_WhenHrefIsMalformed_FailsClosed()
    {
        var report = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        HalLinkTestFactory.WithLinks(
            report,
            new HalLinkTestLink(
                "update-communication-consent",
                "https://[invalid/api/event-reports/my/report/communication-consent",
                "PATCH"));

        var result = await CreateService().UpdateCommunicationConsentAsync(
            report,
            reportCaseUpdatesConsent: true,
            reportFollowUpContactConsent: true,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await _apiClient.DidNotReceive().UpdateMyEventReportCommunicationConsentAsync(
            Arg.Any<Guid>(),
            Arg.Any<UpdateMyReportCommunicationConsentDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateCommunicationConsentAsync_WhenCanonicalHrefHasSafeSuffixes_Dispatches()
    {
        var reportId = Guid.NewGuid();
        var hrefs = new[]
        {
            $"/api/event-reports/my/{reportId}/communication-consent?source=my-reports",
            $"/api/event-reports/my/{reportId}/communication-consent/#preferences",
            $"https://event.example/api/event-reports/my/{reportId}/communication-consent?source=my-reports#preferences"
        };
        var updated = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        updated.Id = reportId;
        _apiClient.UpdateMyEventReportCommunicationConsentAsync(
                reportId,
                Arg.Any<UpdateMyReportCommunicationConsentDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(updated);

        foreach (var href in hrefs)
        {
            var report = CreateReportResource("submitted", "Submitted", "spam", "Spam");
            report.Id = reportId;
            HalLinkTestFactory.WithLinks(
                report,
                new HalLinkTestLink("update-communication-consent", href, "PATCH"));

            var result = await CreateService().UpdateCommunicationConsentAsync(
                report,
                reportCaseUpdatesConsent: true,
                reportFollowUpContactConsent: false,
                CancellationToken.None);

            await Assert.That(result.Success).IsTrue();
        }

        await _apiClient.Received(hrefs.Length).UpdateMyEventReportCommunicationConsentAsync(
            reportId,
            Arg.Any<UpdateMyReportCommunicationConsentDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateCommunicationConsentAsync_WhenReturnedResourceIdDiffers_Fails()
    {
        var report = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        var reportId = report.Id!.Value;
        HalLinkTestFactory.WithLinks(
            report,
            new HalLinkTestLink(
                "update-communication-consent",
                $"/api/event-reports/my/{reportId}/communication-consent",
                "PATCH"));
        var mismatched = CreateReportResource("submitted", "Submitted", "spam", "Spam");
        _apiClient.UpdateMyEventReportCommunicationConsentAsync(
                reportId,
                Arg.Any<UpdateMyReportCommunicationConsentDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(mismatched);

        var result = await CreateService().UpdateCommunicationConsentAsync(
            report,
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: false,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Report).IsNull();
    }

    private EventReportingService CreateService() => new(_apiClient, _logger);

    private static HalResourceOfMyEventReportDto CreateReportResource(
        string statusCode,
        string statusName,
        string reasonCode,
        string reasonName)
    {
        return HalLinkTestFactory.WithLinks(new HalResourceOfMyEventReportDto
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            StatusId = 1,
            StatusCode = statusCode,
            StatusName = statusName,
            ReasonId = 1,
            ReasonCode = reasonCode,
            ReasonName = reasonName,
            SubmittedAtUtc = TestTime.UtcNow
        }, new HalLinkTestLink("self", "/api/event-reports/my/report", "GET"));
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
