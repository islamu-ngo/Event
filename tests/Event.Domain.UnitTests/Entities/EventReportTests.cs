using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public class EventReportTests
{
    [Test]
    public async Task EventReportingEntities_ImplementExpectedDomainInterfaces()
    {
        await Assert.That(Implements<EventReport>(typeof(ITenantEntity), typeof(IAuditableEntity), typeof(ISoftDeletable), typeof(IConcurrencyAware))).IsTrue();
        await Assert.That(Implements<EventReportTarget>(typeof(ITenantEntity))).IsTrue();
        await Assert.That(Implements<EventReportEvidence>(typeof(ITenantEntity), typeof(IAuditableEntity))).IsTrue();
        await Assert.That(Implements<EventReportCase>(typeof(ITenantEntity), typeof(IAuditableEntity), typeof(IConcurrencyAware))).IsTrue();
        await Assert.That(Implements<EventReportSignal>(typeof(ITenantEntity), typeof(IAuditableEntity))).IsTrue();
        await Assert.That(Implements<EventReportDecision>(typeof(ITenantEntity), typeof(IAuditableEntity))).IsTrue();
        await Assert.That(Implements<EventReportExternalLink>(typeof(ITenantEntity), typeof(IAuditableEntity))).IsTrue();
    }

    [Test]
    public async Task Create_WithRequiredFields_InitializesSubmittedReportWithoutReporterText()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var reporterUserId = Guid.CreateVersion7();
        var reporterActorId = Guid.CreateVersion7();

        var report = EventReport.Create(
            tenantId,
            eventId,
            reporterUserId,
            reporterActorId,
            EventReporterKind.AuthenticatedUser,
            EventReportSourceKind.UserReport,
            "spam",
            subcategoryCode: "duplicate_listing",
            EventReportPriority.Normal,
            EventReportSeverityHint.Medium,
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: true,
            reporterLocale: "en-US",
            reporterIpHash: new string('a', 64),
            reporterUserAgentHash: new string('b', 64));

        await Assert.That(report.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(report.TenantId).IsEqualTo(tenantId);
        await Assert.That(report.EventId).IsEqualTo(eventId);
        await Assert.That(report.ReporterUserId).IsEqualTo(reporterUserId);
        await Assert.That(report.ReporterActorId).IsEqualTo(reporterActorId);
        await Assert.That(report.ReporterKind).IsEqualTo(EventReporterKind.AuthenticatedUser);
        await Assert.That(report.SourceKind).IsEqualTo(EventReportSourceKind.UserReport);
        await Assert.That(report.ReasonCode).IsEqualTo("spam");
        await Assert.That(report.SubcategoryCode).IsEqualTo("duplicate_listing");
        await Assert.That(report.Status).IsEqualTo(EventReportStatus.Submitted);
        await Assert.That(report.Priority).IsEqualTo(EventReportPriority.Normal);
        await Assert.That(report.SeverityHint).IsEqualTo(EventReportSeverityHint.Medium);
        await Assert.That(report.ReportCaseUpdatesConsent).IsFalse();
        await Assert.That(report.ReportFollowUpContactConsent).IsTrue();
        await Assert.That(report.ReporterLocale).IsEqualTo("en-US");
        await Assert.That(ReportMetadataProperties()).DoesNotContain("TextBodyEncrypted");
    }

    [Test]
    public async Task ChangeReporterCommunicationConsent_WhenValuesChange_UpdatesBothPurposesAndAuditTime()
    {
        var report = CreateReport();
        var now = new DateTime(2026, 7, 19, 18, 0, 0, DateTimeKind.Utc);

        report.ChangeReporterCommunicationConsent(
            reportCaseUpdatesConsent: true,
            reportFollowUpContactConsent: true,
            now);

        await Assert.That(report.ReportCaseUpdatesConsent).IsTrue();
        await Assert.That(report.ReportFollowUpContactConsent).IsTrue();
        await Assert.That(report.UpdatedAt).IsEqualTo(now);

        var withdrawnAt = now.AddMinutes(5);
        report.ChangeReporterCommunicationConsent(
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: false,
            withdrawnAt);

        await Assert.That(report.ReportCaseUpdatesConsent).IsFalse();
        await Assert.That(report.ReportFollowUpContactConsent).IsFalse();
        await Assert.That(report.UpdatedAt).IsEqualTo(withdrawnAt);
    }

    [Test]
    public async Task ChangeReporterCommunicationConsent_WhenValuesAreUnchanged_DoesNotRewriteAuditTime()
    {
        var report = CreateReport();

        report.ChangeReporterCommunicationConsent(
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: false,
            new DateTime(2026, 7, 19, 18, 0, 0, DateTimeKind.Utc));

        await Assert.That(report.UpdatedAt).IsNull();
    }

    [Test]
    public async Task Create_WhenReasonCodeBlank_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            _ = EventReport.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                null,
                EventReporterKind.AuthenticatedUser,
                EventReportSourceKind.UserReport,
                " ",
                subcategoryCode: null,
                EventReportPriority.Normal,
                severityHint: null,
                reportCaseUpdatesConsent: false,
                reportFollowUpContactConsent: false,
                reporterLocale: null,
                reporterIpHash: null,
                reporterUserAgentHash: null);

            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task UpdateStatus_FromSubmittedToTriaged_SetsStatusAndUpdatedAt()
    {
        var report = CreateReport();
        var now = new DateTime(2026, 7, 2, 9, 30, 0, DateTimeKind.Utc);

        report.UpdateStatus(EventReportStatus.Triaged, now);

        await Assert.That(report.Status).IsEqualTo(EventReportStatus.Triaged);
        await Assert.That(report.UpdatedAt).IsEqualTo(now);
        await Assert.That(report.ClosedAt).IsNull();
    }

    [Test]
    public async Task UpdateStatus_WhenClosed_RejectsFurtherTransitions()
    {
        var report = CreateReport();
        var now = DomainTestClock.UtcNow;
        report.UpdateStatus(EventReportStatus.Dismissed, now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            report.UpdateStatus(EventReportStatus.UnderReview, now.AddMinutes(1));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task CreateReporterText_StoresEncryptedEvidenceAwayFromReportMetadata()
    {
        var tenantId = Guid.CreateVersion7();
        var reportId = Guid.CreateVersion7();
        var createdByUserId = Guid.CreateVersion7();
        var retentionUntil = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

        var evidence = EventReportEvidence.CreateReporterText(
            tenantId,
            reportId,
            "ciphertext:v1:abc123",
            EventReportEvidenceClassification.Sensitive,
            retentionUntil,
            createdByUserId);

        await Assert.That(evidence.EvidenceKind).IsEqualTo(EventReportEvidenceKind.ReporterText);
        await Assert.That(evidence.TextBodyEncrypted).IsEqualTo("ciphertext:v1:abc123");
        await Assert.That(evidence.Classification).IsEqualTo(EventReportEvidenceClassification.Sensitive);
        await Assert.That(evidence.RetentionUntil).IsEqualTo(retentionUntil);
        await Assert.That(evidence.CreatedByUserId).IsEqualTo(createdByUserId);
        await Assert.That(ReportMetadataProperties()).DoesNotContain(nameof(EventReportEvidence.TextBodyEncrypted));
    }

    [Test]
    public async Task EventReportCase_AssignAndClose_TransitionPredictably()
    {
        var caseItem = EventReportCase.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "default",
            EventReportPriority.High,
            DomainTestClock.UtcNow.AddDays(1));
        var moderatorUserId = Guid.CreateVersion7();
        var now = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc);

        caseItem.Assign(moderatorUserId, now);
        caseItem.Close(now.AddMinutes(15));

        await Assert.That(caseItem.Status).IsEqualTo(EventReportCaseStatus.Closed);
        await Assert.That(caseItem.AssignedModeratorUserId).IsEqualTo(moderatorUserId);
        await Assert.That(caseItem.UpdatedAt).IsEqualTo(now.AddMinutes(15));
    }

    [Test]
    public async Task EventReportCase_Triage_UpdatesQueueAndPriority()
    {
        var caseItem = EventReportCase.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "default",
            EventReportPriority.Normal,
            DomainTestClock.UtcNow.AddDays(1));
        var now = new DateTime(2026, 7, 2, 10, 30, 0, DateTimeKind.Utc);

        caseItem.Triage("urgent-safety", EventReportPriority.Urgent, now);

        await Assert.That(caseItem.QueueCode).IsEqualTo("urgent-safety");
        await Assert.That(caseItem.Priority).IsEqualTo(EventReportPriority.Urgent);
        await Assert.That(caseItem.Status).IsEqualTo(EventReportCaseStatus.Open);
        await Assert.That(caseItem.UpdatedAt).IsEqualTo(now);
    }

    [Test]
    public async Task EventReportDecision_Create_WithLocalModeratorRequiresModeratorId()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            _ = EventReportDecision.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                EventReportDecisionSource.LocalModerator,
                EventReportDecisionKind.LightModerate,
                "policy_violation",
                safeNote: "Safe operator note",
                moderatorUserId: null,
                externalDecisionId: null);

            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task EventReportExternalLink_CreatePending_StoresProviderTargetIdentity()
    {
        var tenantId = Guid.CreateVersion7();
        var targetId = tenantId.ToString("N");

        var link = EventReportExternalLink.CreatePending(
            tenantId,
            Guid.CreateVersion7(),
            caseId: null,
            EventReportExternalProvider.Osprey,
            "sync-1",
            providerTargetScope: EventReportProviderTargetScope.Tenant,
            providerTargetId: $" {targetId} ");

        await Assert.That(link.ProviderTargetScope).IsEqualTo(EventReportProviderTargetScope.Tenant);
        await Assert.That(link.ProviderTargetId).IsEqualTo(targetId);
    }

    [Test]
    public async Task EventReportExternalLink_CreatePending_WhenProviderTargetIdBlank_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            _ = EventReportExternalLink.CreatePending(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                caseId: null,
                EventReportExternalProvider.Coop,
                "sync-2",
                providerTargetScope: EventReportProviderTargetScope.Tenant,
                providerTargetId: " ");

            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task EventReportSignal_Create_StoresProviderTargetIdentity()
    {
        var tenantId = Guid.CreateVersion7();
        var targetId = tenantId.ToString("N");

        var signal = EventReportSignal.Create(
            tenantId,
            reportId: Guid.CreateVersion7(),
            eventId: Guid.CreateVersion7(),
            EventReportSignalProvider.Osprey,
            "policy_match",
            "event.spam",
            0.75m,
            EventReportSignalVerdict.NeedsReview,
            recommendedAction: null,
            safeSummary: "Needs moderator review.",
            externalSignalId: "signal-tenant-1",
            correlationId: "correlation-tenant-1",
            providerTargetScope: EventReportProviderTargetScope.Tenant,
            providerTargetId: targetId);

        await Assert.That(signal.ProviderTargetScope).IsEqualTo(EventReportProviderTargetScope.Tenant);
        await Assert.That(signal.ProviderTargetId).IsEqualTo(targetId);
    }

    [Test]
    public async Task EventReportDecision_Create_StoresProviderTargetIdentity()
    {
        var tenantId = Guid.CreateVersion7();
        var targetId = tenantId.ToString("N");

        var decision = EventReportDecision.Create(
            tenantId,
            caseId: Guid.CreateVersion7(),
            reportId: Guid.CreateVersion7(),
            EventReportDecisionSource.CoopReviewer,
            EventReportDecisionKind.LightModerate,
            "policy_violation",
            safeNote: "External reviewer confirmed the case.",
            moderatorUserId: null,
            externalDecisionId: "coop-decision-1",
            providerTargetScope: EventReportProviderTargetScope.Tenant,
            providerTargetId: targetId);

        await Assert.That(decision.ProviderTargetScope).IsEqualTo(EventReportProviderTargetScope.Tenant);
        await Assert.That(decision.ProviderTargetId).IsEqualTo(targetId);
    }

    private static EventReport CreateReport()
    {
        return EventReport.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            EventReporterKind.AuthenticatedUser,
            EventReportSourceKind.UserReport,
            "spam",
            subcategoryCode: null,
            EventReportPriority.Normal,
            severityHint: null,
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: false,
            reporterLocale: null,
            reporterIpHash: null,
            reporterUserAgentHash: null);
    }

    private static bool Implements<T>(params Type[] expectedInterfaces)
    {
        var actualInterfaces = typeof(T).GetInterfaces();
        return expectedInterfaces.All(actualInterfaces.Contains);
    }

    private static List<string> ReportMetadataProperties()
    {
        return typeof(EventReport).GetProperties().Select(property => property.Name).ToList();
    }
}
