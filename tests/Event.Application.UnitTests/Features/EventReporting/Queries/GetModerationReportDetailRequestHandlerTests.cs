using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventReporting.Handlers.Queries;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventReporting.Queries;

public sealed class GetModerationReportDetailRequestHandlerTests
{
    private readonly IEventReportRepository _eventReportRepository = Substitute.For<IEventReportRepository>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IEventReportEvidenceProtector _evidenceProtector = Substitute.For<IEventReportEvidenceProtector>();

    [Test]
    public async Task Handle_WhenReportMatchesEvent_ReturnsDetailWithDecryptedEvidenceAndLogs()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var moderatorUserId = Guid.CreateVersion7();
        var report = CreateDetailedReport(tenantId, eventId, moderatorUserId);
        _tenantContext.TenantId.Returns(tenantId);
        _eventReportRepository.GetByIdWithEvidenceAsync(tenantId, report.Id, Arg.Any<CancellationToken>())
            .Returns(report);
        _evidenceProtector.Unprotect("protected-text").Returns("plain reporter evidence");

        var result = await CreateHandler().Handle(new GetModerationReportDetailRequest
        {
            EventId = eventId,
            ReportId = report.Id
        }, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(report.Id);
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.StatusCode).IsEqualTo("under_review");
        await Assert.That(result.PriorityCode).IsEqualTo("urgent");
        await Assert.That(result.ReporterKindCode).IsEqualTo("authenticated_user");
        await Assert.That(result.SourceKindCode).IsEqualTo("user_report");
        await Assert.That(result.ReasonName).IsEqualTo("Spam");
        await Assert.That(result.ReportCaseUpdatesConsent).IsFalse();
        await Assert.That(result.ReportFollowUpContactConsent).IsTrue();
        await Assert.That(result.CurrentCase).IsNotNull();
        await Assert.That(result.CurrentCase!.StatusCode).IsEqualTo("decision_ready");
        await Assert.That(result.Targets).Count().IsEqualTo(1);
        await Assert.That(result.EvidenceItems).Count().IsEqualTo(1);
        await Assert.That(result.EvidenceItems[0].TextBody).IsEqualTo("plain reporter evidence");
        await Assert.That(result.EvidenceItems[0].IsTextUnavailable).IsFalse();
        await Assert.That(result.Decisions).Count().IsEqualTo(1);
        await Assert.That(result.Decisions[0].DecisionKindCode).IsEqualTo("light_moderate");
        await Assert.That(result.Signals).Count().IsEqualTo(1);
        await Assert.That(result.Signals[0].RecommendedActionCode).IsEqualTo("light_moderate");
        await Assert.That(result.ExternalLinks).Count().IsEqualTo(1);
        await Assert.That(result.ExternalLinks[0].SyncStateCode).IsEqualTo("synced");

        var serialized = JsonSerializer.Serialize(result);
        await Assert.That(serialized).DoesNotContain(report.ReporterUserId.ToString());
        await Assert.That(serialized).DoesNotContain(report.ReporterActorId.ToString());
        await Assert.That(serialized).DoesNotContain(moderatorUserId.ToString());
        await Assert.That(serialized).DoesNotContain("protected-text");
        await Assert.That(serialized).DoesNotContain("ip-hash");
        await Assert.That(serialized).DoesNotContain("ua-hash");
        await Assert.That(serialized).DoesNotContain("coop-case-1");
        await Assert.That(serialized).DoesNotContain("coop-signal-1");
        await Assert.That(serialized).DoesNotContain("https://coop.example/cases/1");
        await Assert.That(serialized).DoesNotContain("signal-corr");
        await Assert.That(serialized).DoesNotContain("ReporterUserId");
        await Assert.That(serialized).DoesNotContain("ReporterActorId");
        await Assert.That(serialized).DoesNotContain("CreatedByUserId");
        await Assert.That(serialized).DoesNotContain("ModeratorUserId");
        await Assert.That(serialized).DoesNotContain("ProviderCaseId");
        await Assert.That(serialized).DoesNotContain("ProviderSignalId");
        await Assert.That(serialized).DoesNotContain("ProviderUrl");
        await Assert.That(serialized).DoesNotContain("CorrelationId");
    }

    [Test]
    public async Task Handle_WhenReportBelongsToDifferentEvent_ReturnsNull()
    {
        var tenantId = Guid.CreateVersion7();
        var report = CreateDetailedReport(tenantId, Guid.CreateVersion7(), Guid.CreateVersion7());
        _tenantContext.TenantId.Returns(tenantId);
        _eventReportRepository.GetByIdWithEvidenceAsync(tenantId, report.Id, Arg.Any<CancellationToken>())
            .Returns(report);

        var result = await CreateHandler().Handle(new GetModerationReportDetailRequest
        {
            EventId = Guid.CreateVersion7(),
            ReportId = report.Id
        }, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Handle_WhenEvidenceCannotBeUnprotected_MarksTextUnavailable()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var report = CreateDetailedReport(tenantId, eventId, Guid.CreateVersion7());
        _tenantContext.TenantId.Returns(tenantId);
        _eventReportRepository.GetByIdWithEvidenceAsync(tenantId, report.Id, Arg.Any<CancellationToken>())
            .Returns(report);
        _evidenceProtector.Unprotect("protected-text")
            .Returns(_ => throw new CryptographicException("invalid payload"));

        var result = await CreateHandler().Handle(new GetModerationReportDetailRequest
        {
            EventId = eventId,
            ReportId = report.Id
        }, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.EvidenceItems[0].TextBody).IsNull();
        await Assert.That(result.EvidenceItems[0].HasTextBody).IsTrue();
        await Assert.That(result.EvidenceItems[0].IsTextUnavailable).IsTrue();
    }

    private GetModerationReportDetailRequestHandler CreateHandler() => new(
        _eventReportRepository,
        _tenantContext,
        _evidenceProtector);

    private static EventReport CreateDetailedReport(Guid tenantId, Guid eventId, Guid moderatorUserId)
    {
        var report = EventReport.Create(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            EventReporterKind.AuthenticatedUser,
            EventReportSourceKind.UserReport,
            "spam",
            "organizer",
            EventReportPriority.Urgent,
            EventReportSeverityHint.Critical,
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: true,
            reporterLocale: "en",
            reporterIpHash: "ip-hash",
            reporterUserAgentHash: "ua-hash");
        report.UpdateStatus(EventReportStatus.UnderReview, DateTime.UtcNow);

        var target = EventReportTarget.CreateEventTarget(tenantId, report.Id, eventId);
        var evidence = EventReportEvidence.CreateReporterText(
            tenantId,
            report.Id,
            "protected-text",
            EventReportEvidenceClassification.Sensitive,
            DateTime.UtcNow.AddDays(30),
            report.ReporterUserId,
            DateTime.UtcNow);
        var reportCase = EventReportCase.Create(
            tenantId,
            report.Id,
            "safety",
            EventReportPriority.Urgent,
            DateTime.UtcNow.AddHours(4));
        reportCase.Assign(moderatorUserId, DateTime.UtcNow);
        reportCase.MarkDecisionReady(DateTime.UtcNow);
        var decision = EventReportDecision.Create(
            tenantId,
            reportCase.Id,
            report.Id,
            EventReportDecisionSource.LocalModerator,
            EventReportDecisionKind.LightModerate,
            "spam",
            "safe note",
            moderatorUserId,
            externalDecisionId: null);
        var signal = EventReportSignal.Create(
            tenantId,
            report.Id,
            eventId,
            EventReportSignalProvider.Local,
            "keyword",
            "spam-policy",
            0.86m,
            EventReportSignalVerdict.NeedsReview,
            EventReportRecommendedAction.LightModerate,
            "safe signal summary",
            externalSignalId: null,
            "signal-corr");
        var externalLink = EventReportExternalLink.CreatePending(
            tenantId,
            report.Id,
            reportCase.Id,
            EventReportExternalProvider.Coop,
            "link-corr");
        externalLink.MarkSynced("coop-case-1", "coop-signal-1", "https://coop.example/cases/1", DateTime.UtcNow);

        report.Targets.Add(target);
        report.EvidenceItems.Add(evidence);
        report.Cases.Add(reportCase);
        report.Decisions.Add(decision);
        report.Signals.Add(signal);
        report.ExternalLinks.Add(externalLink);
        return report;
    }
}
