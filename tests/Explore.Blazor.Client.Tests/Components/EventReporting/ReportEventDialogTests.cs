using System.Reflection;
using Explore.Blazor.Client.Components.EventReporting;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.EventReporting;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.EventReporting;

public sealed class ReportEventDialogTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private IRenderedComponent<MudDialogProvider>? _dialogProvider;

    [Test]
    public async Task Render_WhenDetailsEmpty_DisablesSubmitUntilDetailsAreProvided()
    {
        var eventId = Guid.NewGuid();
        var service = RegisterReportingService(eventId);

        var cut = RenderDialog(eventId);
        cut.WaitForState(() => !GetPrivateField<bool>(cut.Instance, "_isLoadingOptions"), TimeSpan.FromSeconds(3));

        await Assert.That(GetPrivateProperty<bool>(cut.Instance, "CanSubmit")).IsFalse();

        SetPrivateField(cut.Instance, "_reporterText", "This event appears to be spam.");

        await Assert.That(GetPrivateProperty<bool>(cut.Instance, "CanSubmit")).IsTrue();
        await service.DidNotReceive().SubmitAsync(Arg.Any<SubmitEventReportDto>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SubmitAsync_WhenDetailsEmpty_DoesNotCallSubmissionService()
    {
        var eventId = Guid.NewGuid();
        var service = RegisterReportingService(eventId);

        var cut = RenderDialog(eventId);
        cut.WaitForState(() => !GetPrivateField<bool>(cut.Instance, "_isLoadingOptions"), TimeSpan.FromSeconds(3));

        await InvokePrivateTaskAsync(cut.Instance, "SubmitAsync");

        await service.DidNotReceive().SubmitAsync(Arg.Any<SubmitEventReportDto>(), Arg.Any<CancellationToken>());
        await Assert.That(GetPrivateField<string?>(cut.Instance, "_errorMessage")).IsEqualTo("Add details before submitting.");
    }

    [Test]
    public async Task Render_CommunicationChoicesAreIndependentUncheckedAndDescribed()
    {
        var eventId = Guid.NewGuid();
        RegisterReportingService(eventId);

        var cut = RenderDialog(eventId);
        cut.WaitForState(() => !GetPrivateField<bool>(cut.Instance, "_isLoadingOptions"), TimeSpan.FromSeconds(3));

        var choices = _dialogProvider!.FindAll("input[type='checkbox']");
        await Assert.That(choices.Count).IsEqualTo(2);
        await Assert.That(choices.All(choice => !choice.HasAttribute("checked"))).IsTrue();
        await Assert.That(cut.Markup).Contains("Email preferences");
        await Assert.That(cut.Markup).Contains("Case updates");
        await Assert.That(cut.Markup).Contains("acknowledgement, status updates, and the final outcome");
        await Assert.That(cut.Markup).Contains("Follow-up contact");
        await Assert.That(cut.Markup).Contains("clarification or additional evidence");

        var describedInputs = cut.FindAll("input[type='checkbox'][aria-describedby]");
        await Assert.That(describedInputs.Count).IsEqualTo(2);
        foreach (var input in describedInputs)
        {
            var descriptionId = input.GetAttribute("aria-describedby");
            await Assert.That(descriptionId).IsNotNull();
            await Assert.That(cut.FindAll($"#{descriptionId}").Count).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task SubmitAsync_MapsCommunicationChoicesIndependently(
        bool caseUpdatesConsent,
        bool followUpContactConsent)
    {
        var eventId = Guid.NewGuid();
        var service = RegisterReportingService(eventId);
        SubmitEventReportDto? capturedRequest = null;
        service.SubmitAsync(Arg.Do<SubmitEventReportDto>(request => capturedRequest = request), Arg.Any<CancellationToken>())
            .Returns(EventReportSubmissionResult.Failed("Keep the dialog open for inspection."));
        var cut = RenderDialog(eventId);
        cut.WaitForState(() => !GetPrivateField<bool>(cut.Instance, "_isLoadingOptions"), TimeSpan.FromSeconds(3));
        SetPrivateField(cut.Instance, "_reporterText", "This event appears to be spam.");
        SetPrivateField(cut.Instance, "_reportCaseUpdatesConsent", caseUpdatesConsent);
        SetPrivateField(cut.Instance, "_reportFollowUpContactConsent", followUpContactConsent);

        await InvokePrivateTaskAsync(cut.Instance, "SubmitAsync");

        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.ReportCaseUpdatesConsent).IsEqualTo(caseUpdatesConsent);
        await Assert.That(capturedRequest.ReportFollowUpContactConsent).IsEqualTo(followUpContactConsent);
    }

    [Test]
    public async Task SubmitAsync_WhenValidationErrorRepeats_RendersOneAssertiveAlertPath()
    {
        var eventId = Guid.NewGuid();
        RegisterReportingService(eventId);
        var announcer = _ctx.Services.GetRequiredService<IAccessibilityAnnouncerService>();
        var cut = RenderDialog(eventId);
        cut.WaitForState(() => !GetPrivateField<bool>(cut.Instance, "_isLoadingOptions"), TimeSpan.FromSeconds(3));

        await InvokePrivateTaskAsync(cut.Instance, "SubmitAsync");
        await InvokePrivateTaskAsync(cut.Instance, "SubmitAsync");
        cut.Render();

        await Assert.That(_dialogProvider!.FindAll("[role='alert']").Count).IsEqualTo(1);
        await announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
    }

    [Test]
    [Arguments("event_correction_suggestion", "Suggest a correction")]
    [Arguments("unsafe_external_link", "Report unsafe link")]
    public async Task Submit_WithFixedIntent_UsesStableReasonAndSubcategory(string subcategoryCode, string reasonLabel)
    {
        var eventId = Guid.NewGuid();
        var service = RegisterReportingService(eventId, "other", reasonLabel);
        SubmitEventReportDto? capturedRequest = null;
        service.SubmitAsync(Arg.Do<SubmitEventReportDto>(request => capturedRequest = request), Arg.Any<CancellationToken>())
            .Returns(EventReportSubmissionResult.Failed("Keep the dialog open for inspection."));

        var cut = RenderDialog(eventId, subcategoryCode);
        cut.WaitForState(() => !GetPrivateField<bool>(cut.Instance, "_isLoadingOptions"), TimeSpan.FromSeconds(3));
        SetPrivateField(cut.Instance, "_reporterText", "Correct this community listing.");

        await InvokePrivateTaskAsync(cut.Instance, "SubmitAsync");

        await Assert.That(cut.Markup).Contains(reasonLabel);
        await Assert.That(cut.FindAll("select")).IsEmpty();
        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.ReasonCode).IsEqualTo("other");
        await Assert.That(capturedRequest.SubcategoryCode).IsEqualTo(subcategoryCode);
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<ReportEventDialog> RenderDialog(Guid eventId, string? fixedReasonCode = null)
    {
        _dialogProvider = _ctx.Render<MudDialogProvider>();
        var parameters = new DialogParameters<ReportEventDialog>
        {
            { component => component.EventId, eventId },
            { component => component.EventTitle, "Community Program" },
            { component => component.FixedSubcategoryCode, fixedReasonCode }
        };
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        _ = dialogService.ShowAsync<ReportEventDialog>("Report event", parameters);
        _dialogProvider.WaitForState(
            () => _dialogProvider.FindComponents<ReportEventDialog>().Count == 1,
            TimeSpan.FromSeconds(3));
        return _dialogProvider.FindComponent<ReportEventDialog>();
    }

    private IEventReportingService RegisterReportingService(
        Guid eventId,
        string reasonCode = "spam",
        string reasonName = "Spam")
    {
        var service = Substitute.For<IEventReportingService>();
        service.GetOptionsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfEventReportOptionsDto
            {
                EventId = eventId,
                IsReportable = true,
                MaxReporterTextLength = 4000,
                ReasonOptions =
                [
                    new ReasonOptions2
                    {
                        ReasonId = 1,
                        ReasonCode = reasonCode,
                        ReasonName = reasonName,
                        Description = "Misleading, repetitive, or promotional content."
                    }
                ]
            });

        _ctx.Services.AddSingleton(service);
        return service;
    }

    private static T GetPrivateProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property {propertyName} was not found.");

        return (T?)property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property {propertyName} returned null.");
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");

        return (T?)field.GetValue(instance)
            ?? throw new InvalidOperationException($"Field {fieldName} returned null.");
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");

        field.SetValue(instance, value);
    }

    private static async Task InvokePrivateTaskAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");

        var task = method.Invoke(instance, null) as Task
            ?? throw new InvalidOperationException($"Method {methodName} did not return a task.");
        await task;
    }
}
