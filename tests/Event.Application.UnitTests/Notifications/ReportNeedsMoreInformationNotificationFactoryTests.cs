using Explore.Application.Contracts.Notifications;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Notifications;

public sealed class ReportNeedsMoreInformationNotificationFactoryTests
{
    [Test]
    public async Task Create_WithCurrentAuthority_BuildsRequiredInAppAndOptionalFollowUpEmail()
    {
        (EventReport report, EventReportDecision decision) = CreateSource(followUpConsent: true);

        RecipientNotificationMaterialization materialization = CreateMaterialization(report, decision);

        await Assert.That(materialization.Intent.TemplateKey)
            .IsEqualTo(ReportNeedsMoreInformationNotificationFactory.TemplateKey);
        await Assert.That(materialization.Intent.ReportDecisionId).IsEqualTo(decision.Id);
        await Assert.That(materialization.DeliveryPolicy)
            .IsEqualTo(NotificationDeliveryPolicyEnum.ReportFollowUpContact);
        await Assert.That(materialization.ConsentPurpose)
            .IsEqualTo(ReportEmailConsentPurposeCodes.FollowUpContact);
        await Assert.That(materialization.InApp!.IsRequired).IsTrue();
        await Assert.That(materialization.InApp.NotificationEntityTypeId).IsNull();
        await Assert.That(materialization.InApp.EntityId).IsNull();
        await Assert.That(materialization.LinkAllowed).IsFalse();
        await Assert.That(materialization.Email!.Kind).IsEqualTo(EmailDispatchKind.ReportNeedsMoreInformation);
        await Assert.That(materialization.Email.SourceId).IsEqualTo(decision.Id);

        string recipientContent = string.Join(
            ' ',
            materialization.InApp.Title,
            materialization.InApp.Body,
            materialization.Email.Subject,
            materialization.Email.PlainTextBody,
            materialization.Email.HtmlBody,
            materialization.Intent.SafePayloadReference);
        await Assert.That(recipientContent).Contains("not a final decision");
        await Assert.That(recipientContent).DoesNotContain(decision.ReasonCode);
        await Assert.That(recipientContent).DoesNotContain(decision.SafeNote!);
        await Assert.That(recipientContent).DoesNotContain(report.EventId.ToString("D"));
        await Assert.That(recipientContent).DoesNotContain(report.Id.ToString("D"));
    }

    [Test]
    public async Task Create_WithoutFollowUpConsent_PreservesRequiredInAppAndTypesSkippedEmail()
    {
        (EventReport report, EventReportDecision decision) = CreateSource(followUpConsent: false);

        RecipientNotificationMaterialization materialization = CreateMaterialization(report, decision);

        await Assert.That(materialization.InApp!.IsRequired).IsTrue();
        await Assert.That(materialization.IncludeEmailChannel).IsTrue();
        await Assert.That(materialization.EmailRequired).IsFalse();
        await Assert.That(materialization.Email).IsNull();
        await Assert.That(materialization.EmailSkipReason)
            .IsEqualTo(ReportNeedsMoreInformationNotificationFactory.ConsentNotGrantedSkipReason);
    }

    [Test]
    public async Task Create_ForDistinctDecisions_UsesDistinctOccurrenceAndDeduplicationIdentity()
    {
        (EventReport report, EventReportDecision firstDecision) = CreateSource(followUpConsent: true);
        EventReportDecision secondDecision = CreateDecision(report);

        RecipientNotificationMaterialization first = CreateMaterialization(report, firstDecision);
        RecipientNotificationMaterialization second = CreateMaterialization(report, secondDecision);

        await Assert.That(first.Intent.DeduplicationKey).IsNotEqualTo(second.Intent.DeduplicationKey);
        await Assert.That(first.Intent.ReportDecisionId).IsNotEqualTo(second.Intent.ReportDecisionId);
        await Assert.That(first.Email!.SourceId).IsNotEqualTo(second.Email!.SourceId);
    }

    private static RecipientNotificationMaterialization CreateMaterialization(
        EventReport report,
        EventReportDecision decision) => new ReportNeedsMoreInformationNotificationFactory().Create(
        report,
        decision,
        new RecipientEmailAddressResolution("reporter@example.test", null),
        emailPreferenceEnabled: true,
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        DateTime.UtcNow);

    private static (EventReport Report, EventReportDecision Decision) CreateSource(bool followUpConsent)
    {
        Guid tenantId = Guid.CreateVersion7();
        EventReport report = EventReport.Create(
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            EventReporterKind.AuthenticatedUser,
            EventReportSourceKind.UserReport,
            "unsafe_reason_code_sentinel",
            null,
            EventReportPriority.Normal,
            null,
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: followUpConsent,
            null,
            null,
            null);
        return (report, CreateDecision(report));
    }

    private static EventReportDecision CreateDecision(EventReport report) => EventReportDecision.Create(
        report.TenantId,
        Guid.CreateVersion7(),
        report.Id,
        EventReportDecisionSource.LocalModerator,
        EventReportDecisionKind.NeedsMoreInfo,
        "unsafe_decision_reason_sentinel",
        "unsafe_internal_note_sentinel",
        Guid.CreateVersion7(),
        null);
}
