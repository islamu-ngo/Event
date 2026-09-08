using Explore.Blazor.Client.Components.EventReporting;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.EventReporting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Tests.Components.EventReporting;

public sealed class MyReportsPageTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventReportingService _service = Substitute.For<IEventReportingService>();
    private readonly IAccessibilityAnnouncerService _announcer = Substitute.For<IAccessibilityAnnouncerService>();
    private readonly IAccessibilityFocusService _focus = Substitute.For<IAccessibilityFocusService>();

    public MyReportsPageTests()
    {
        _ctx.Services.RemoveAll<IEventReportingService>();
        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.RemoveAll<IAccessibilityFocusService>();
        _ctx.Services.AddSingleton(_service);
        _ctx.Services.AddSingleton(_announcer);
        _ctx.Services.AddSingleton(_focus);
    }

    [Test]
    public async Task Render_AlwaysShowsAuthoritativeValues_AndGatesEditingByReportLink()
    {
        var editable = CreateReport(caseUpdates: true, followUp: false, canEdit: true);
        var readOnly = CreateReport(caseUpdates: false, followUp: true, canEdit: false);
        SetReports(editable, readOnly);

        var cut = RenderPage();

        await Assert.That(cut.FindAll(".my-reports-page__consent-summary").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("button").Count(button => button.TextContent.Trim() == "Edit email preferences")).IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("Case updates");
        await Assert.That(cut.Markup).Contains("Follow-up contact");
        await Assert.That(cut.Markup).Contains("Enabled");
        await Assert.That(cut.Markup).Contains("Disabled");
    }

    [Test]
    public async Task Edit_InitializesBothControls_AndProvidesUniqueLabelsAndDescriptions()
    {
        var report = CreateReport(caseUpdates: true, followUp: false, canEdit: true);
        SetReports(report);
        var cut = RenderPage();

        await cut.InvokeAsync(() => FindButton(cut, "Edit email preferences").Click());

        var inputs = cut.FindAll("input[type='checkbox']");
        await Assert.That(inputs.Count).IsEqualTo(2);
        await Assert.That(inputs[0].HasAttribute("checked")).IsTrue();
        await Assert.That(inputs[1].HasAttribute("checked")).IsFalse();
        await Assert.That(cut.Markup).Contains("Change email preferences");
        await Assert.That(FindButton(cut, "Save email preferences").HasAttribute("disabled")).IsTrue();

        foreach (var input in inputs)
        {
            var descriptionId = input.GetAttribute("aria-describedby");
            await Assert.That(descriptionId).IsNotNull();
            await Assert.That(cut.FindAll($"#{descriptionId}").Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Save_SendsExplicitValues_AndReplacesTheAuthoritativeResource()
    {
        var report = CreateReport(caseUpdates: true, followUp: false, canEdit: true);
        var updated = CreateReport(caseUpdates: false, followUp: true, canEdit: true, report.Id);
        bool? capturedCaseUpdates = null;
        bool? capturedFollowUp = null;
        _service.UpdateCommunicationConsentAsync(
                report,
                Arg.Do<bool>(value => capturedCaseUpdates = value),
                Arg.Do<bool>(value => capturedFollowUp = value),
                Arg.Any<CancellationToken>())
            .Returns(EventReportConsentUpdateResult.Successful(updated));
        SetReports(report);
        var cut = RenderPage();
        _announcer.ClearReceivedCalls();

        await cut.InvokeAsync(() => FindButton(cut, "Edit email preferences").Click());
        var inputs = cut.FindAll("input[type='checkbox']");
        inputs[0].Change(false);
        inputs[1].Change(true);
        await cut.InvokeAsync(() => FindButton(cut, "Save email preferences").Click());

        await Assert.That(capturedCaseUpdates).IsFalse();
        await Assert.That(capturedFollowUp).IsTrue();
        var summary = cut.FindAll(".my-reports-page__consent-summary-item");
        await Assert.That(summary[0].TextContent).Contains("Disabled");
        await Assert.That(summary[1].TextContent).Contains("Enabled");
        await Assert.That(cut.FindAll("fieldset")).IsEmpty();
        await Assert.That(FindButton(cut, "Edit email preferences")).IsNotNull();
        await _announcer.Received(1).AnnouncePoliteAsync("Email preferences saved.");
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
        await _focus.Received(1).FocusByIdAsync($"report-consent-summary-{report.Id:N}", true);
    }

    [Test]
    public async Task FailedSave_PreservesAuthoritativeValues_AndLeavesOneRetryableErrorPath()
    {
        var report = CreateReport(caseUpdates: true, followUp: false, canEdit: true);
        _service.UpdateCommunicationConsentAsync(
                report,
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(EventReportConsentUpdateResult.Failed());
        SetReports(report);
        var cut = RenderPage();
        _announcer.ClearReceivedCalls();

        await cut.InvokeAsync(() => FindButton(cut, "Edit email preferences").Click());
        cut.FindAll("input[type='checkbox']")[0].Change(false);
        await cut.InvokeAsync(() => FindButton(cut, "Save email preferences").Click());

        var summary = cut.FindAll(".my-reports-page__consent-summary-item");
        await Assert.That(summary[0].TextContent).Contains("Enabled");
        await Assert.That(summary[1].TextContent).Contains("Disabled");
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
        await Assert.That(FindButton(cut, "Save email preferences").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.FindAll("input[type='checkbox']")[0].HasAttribute("checked")).IsFalse();
        await _announcer.DidNotReceive().AnnouncePoliteAsync(Arg.Any<string>());
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
    }

    [Test]
    public async Task Cancel_RestoresAuthoritativeValues_WithoutAServiceCall()
    {
        var report = CreateReport(caseUpdates: false, followUp: false, canEdit: true);
        SetReports(report);
        var cut = RenderPage();

        await cut.InvokeAsync(() => FindButton(cut, "Edit email preferences").Click());
        cut.FindAll("input[type='checkbox']")[0].Change(true);
        await cut.InvokeAsync(() => FindButton(cut, "Cancel").Click());
        await cut.InvokeAsync(() => FindButton(cut, "Edit email preferences").Click());

        await Assert.That(cut.FindAll("input[type='checkbox']").All(input => !input.HasAttribute("checked"))).IsTrue();
        await _service.DidNotReceive().UpdateCommunicationConsentAsync(
            Arg.Any<HalResourceOfMyEventReportDto>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        await _focus.Received(1).FocusByIdAsync($"report-consent-edit-{report.Id:N}", true);
    }

    [Test]
    public async Task Save_WhenReturnedAffordanceIsRemoved_FocusesAlwaysPresentSummary()
    {
        var report = CreateReport(caseUpdates: false, followUp: false, canEdit: true);
        var updated = CreateReport(caseUpdates: true, followUp: false, canEdit: false, report.Id);
        _service.UpdateCommunicationConsentAsync(
                report,
                true,
                false,
                Arg.Any<CancellationToken>())
            .Returns(EventReportConsentUpdateResult.Successful(updated));
        SetReports(report);
        var cut = RenderPage();

        await cut.InvokeAsync(() => FindButton(cut, "Edit email preferences").Click());
        cut.FindAll("input[type='checkbox']")[0].Change(true);
        await cut.InvokeAsync(() => FindButton(cut, "Save email preferences").Click());

        await Assert.That(cut.FindAll("button").Any(
            button => button.TextContent.Trim() == "Edit email preferences")).IsFalse();
        var summary = cut.Find($"#report-consent-summary-{report.Id:N}");
        await Assert.That(summary.GetAttribute("tabindex")).IsEqualTo("-1");
        await _focus.Received(1).FocusByIdAsync($"report-consent-summary-{report.Id:N}", true);
    }

    [Test]
    public async Task Save_WhilePending_KeepsOtherReportControlsIndependent()
    {
        var first = CreateReport(caseUpdates: false, followUp: false, canEdit: true);
        var second = CreateReport(caseUpdates: false, followUp: false, canEdit: true);
        var completion = new TaskCompletionSource<EventReportConsentUpdateResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _service.UpdateCommunicationConsentAsync(
                first,
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(completion.Task);
        SetReports(first, second);
        var cut = RenderPage();

        await cut.InvokeAsync(() => cut.FindAll("button")
            .First(button => button.TextContent.Trim() == "Edit email preferences")
            .Click());
        cut.FindAll("input[type='checkbox']")[0].Change(true);
        var saveTask = cut.InvokeAsync(() => FindButton(cut, "Save email preferences").Click());
        cut.WaitForState(() => cut.Markup.Contains("Saving email preferences...", StringComparison.Ordinal));

        var remainingEdit = FindButton(cut, "Edit email preferences");
        await Assert.That(remainingEdit.HasAttribute("disabled")).IsFalse();
        await cut.InvokeAsync(() => remainingEdit.Click());
        await Assert.That(cut.FindAll("fieldset").Count).IsEqualTo(2);

        completion.SetResult(EventReportConsentUpdateResult.Successful(
            CreateReport(caseUpdates: true, followUp: false, canEdit: true, first.Id)));
        await saveTask;
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<MyReportsPage> RenderPage()
    {
        var cut = _ctx.RenderMudComponent<MyReportsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading reports...", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        return cut;
    }

    private void SetReports(params HalResourceOfMyEventReportDto[] reports)
    {
        _service.GetMyReportsAsync(1, 10, Arg.Any<CancellationToken>())
            .Returns(new EventReportPageResult(reports, 1, 10, reports.Length, 1, false, false));
    }

    private static HalResourceOfMyEventReportDto CreateReport(
        bool caseUpdates,
        bool followUp,
        bool canEdit,
        Guid? reportId = null)
    {
        var id = reportId ?? Guid.NewGuid();
        var report = new HalResourceOfMyEventReportDto
        {
            Id = id,
            EventId = Guid.NewGuid(),
            StatusId = 1,
            StatusCode = "submitted",
            StatusName = "Submitted",
            ReasonId = 1,
            ReasonCode = "spam",
            ReasonName = "Spam",
            SubmittedAtUtc = TestTime.UtcNow,
            ReportCaseUpdatesConsent = caseUpdates,
            ReportFollowUpContactConsent = followUp
        };

        return canEdit
            ? HalLinkTestFactory.WithLinks(
                report,
                new HalLinkTestLink(
                    "update-communication-consent",
                    $"/api/event-reports/my/{id}/communication-consent",
                    "PUT"))
            : report;
    }

    private static AngleSharp.Dom.IElement FindButton(
        IRenderedComponent<MyReportsPage> cut,
        string text)
        => cut.FindAll("button").Single(button => button.TextContent.Trim() == text);
}
