using System.Reflection;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Admin;
using Explore.Blazor.Client.Pages.Events.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class EventLocationReviewQueueTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Render_ListsEveryFlaggedAssociation()
    {
        var eventId = Guid.NewGuid();
        RegisterQueue(eventId, Row(needsReview: true, canRemediate: true), Row(needsReview: true, canRemediate: false));

        var cut = Render(eventId);

        await Assert.That(cut.FindAll("[data-testid='event-location-needs-review']").Count).IsEqualTo(2);
        await Assert.That(cut.Markup).Contains("2 location(s) awaiting review");
    }

    [Test]
    public async Task Render_GatesRemediationStrictlyByTheRemediateLocationRelation()
    {
        var eventId = Guid.NewGuid();
        RegisterQueue(eventId, Row(needsReview: true, canRemediate: true), Row(needsReview: true, canRemediate: false));

        var cut = Render(eventId);

        // Two flagged rows, but only the row the server authorized offers the action.
        await Assert.That(cut.FindAll("[data-testid='event-location-remediate']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Render_WithNoRemediationRelation_OffersNoRemediationAnywhere()
    {
        var eventId = Guid.NewGuid();
        RegisterQueue(eventId, Row(needsReview: true, canRemediate: false));

        var cut = Render(eventId);

        await Assert.That(cut.FindAll("[data-testid='event-location-remediate']")).IsEmpty();
    }

    [Test]
    public async Task Render_WithEmptyQueue_ShowsTheClearedState()
    {
        var eventId = Guid.NewGuid();
        RegisterQueue(eventId);

        var cut = Render(eventId);

        await Assert.That(cut.Markup).Contains("Nothing is waiting for privacy review");
        await Assert.That(cut.FindAll("[data-testid='event-location-remediate']")).IsEmpty();
    }

    [Test]
    public async Task Render_ReadsOnlyTheReviewQueueSurface()
    {
        var eventId = Guid.NewGuid();
        var service = RegisterQueue(eventId, Row(needsReview: true, canRemediate: true));

        Render(eventId);

        await service.Received(1).GetReviewQueueAsync(eventId, Arg.Any<CancellationToken>());
        await service.DidNotReceive().GetManagementListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await service.DidNotReceive().GetPublicAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OpenRemediationAsync_PostsTheConfirmationWithTheRowConcurrencyTokens()
    {
        var eventId = Guid.NewGuid();
        var row = Row(needsReview: true, canRemediate: true);
        row.PolicyVersion = 5;
        row.ConcurrencyStamp = Guid.NewGuid();
        var service = RegisterQueue(eventId, row);
        ConfirmEventLocationRemediationDto? captured = null;
        service.ConfirmRemediationAsync(
                eventId,
                row.EventLocationId!.Value,
                Arg.Do<ConfirmEventLocationRemediationDto>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var dialogProvider = _ctx.Render<MudDialogProvider>();
        var cut = Render(eventId);

        // The page awaits the dialog result, so the open call only completes once the dialog closes.
        Task opening = cut.InvokeAsync(() => InvokePrivateTaskAsync(cut.Instance, "OpenRemediationAsync", row));
        dialogProvider.WaitForState(
            () => dialogProvider.FindComponents<ConfirmRemediationDialog>().Count == 1,
            TimeSpan.FromSeconds(3));
        var dialog = dialogProvider.FindComponent<ConfirmRemediationDialog>();

        await dialog.InvokeAsync(() => InvokePrivateTaskAsync(dialog.Instance, "ConfirmAsync"));
        await opening.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.ExpectedPolicyVersion).IsEqualTo(5);
        await Assert.That(captured.ExpectedConcurrencyStamp).IsEqualTo(row.ConcurrencyStamp);
    }

    [Test]
    public async Task ConfirmRemediationDialog_WithoutTheRelation_NeverReachesTheApi()
    {
        var eventId = Guid.NewGuid();
        var row = Row(needsReview: true, canRemediate: false);
        var service = RegisterQueue(eventId, row);

        var dialogProvider = _ctx.Render<MudDialogProvider>();
        var parameters = new DialogParameters<ConfirmRemediationDialog>
        {
            { component => component.EventId, eventId },
            { component => component.Resource, row }
        };
        _ = _ctx.Services.GetRequiredService<IDialogService>()
            .ShowAsync<ConfirmRemediationDialog>("Confirm", parameters);
        dialogProvider.WaitForState(
            () => dialogProvider.FindComponents<ConfirmRemediationDialog>().Count == 1,
            TimeSpan.FromSeconds(3));
        var dialog = dialogProvider.FindComponent<ConfirmRemediationDialog>();

        await dialog.InvokeAsync(() => InvokePrivateTaskAsync(dialog.Instance, "ConfirmAsync"));

        await service.DidNotReceive().ConfirmRemediationAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<ConfirmEventLocationRemediationDto>(),
            Arg.Any<CancellationToken>());
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<EventLocationReviewQueue> Render(Guid eventId)
    {
        var cut = _ctx.Render<EventLocationReviewQueue>(parameters =>
            parameters.Add(component => component.EventId, eventId));
        cut.WaitForState(
            () => !GetPrivateField<bool>(cut.Instance, "_loading"),
            TimeSpan.FromSeconds(3));
        return cut;
    }

    private IEventLocationService RegisterQueue(
        Guid eventId,
        params HalResourceOfEventLocationManagementDto[] rows)
    {
        var service = Substitute.For<IEventLocationService>();
        service.GetReviewQueueAsync(eventId, Arg.Any<CancellationToken>()).Returns(rows);
        _ctx.Services.AddSingleton(service);
        return service;
    }

    private static HalResourceOfEventLocationManagementDto Row(bool needsReview, bool canRemediate)
    {
        var links = new Dictionary<string, HalLink>
        {
            ["self"] = new() { Href = "/api/events/e/locations/l/management" }
        };
        if (canRemediate)
        {
            links["remediate-location"] = new HalLink { Href = "/api/events/e/locations/l/remediation/confirm" };
        }

        return new HalResourceOfEventLocationManagementDto
        {
            EventLocationId = Guid.NewGuid(),
            State = EventLocationDisclosureState.Needs_privacy_review,
            NeedsPrivacyReview = needsReview,
            PolicyVersion = 1,
            ConcurrencyStamp = Guid.NewGuid(),
            Policy = new EventLocationDisclosurePolicyDto { FullDetailsAudienceId = 3 },
            _links = links
        };
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        return (T)field.GetValue(instance)!;
    }

    private static Task InvokePrivateTaskAsync(object instance, string methodName, params object[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        return (Task)method.Invoke(instance, arguments)!;
    }
}
