using System.Reflection;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Events.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Event;

public sealed class EventLocationEditorDialogTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private IRenderedComponent<MudDialogProvider>? _dialogProvider;

    [Test]
    public async Task Render_WithEditLink_OffersTheSaveAffordance()
    {
        RegisterService();
        var cut = RenderDialog(CreateResource(withEditLink: true));

        await Assert.That(_dialogProvider!.FindAll("[data-testid='event-location-editor-save']").Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task Render_WithoutEditLink_HidesSaveAndExplainsWhy()
    {
        RegisterService();
        var cut = RenderDialog(CreateResource(withEditLink: false));

        await Assert.That(_dialogProvider!.FindAll("[data-testid='event-location-editor-save']")).IsEmpty();
        await Assert.That(cut.Markup).Contains("no longer allowed");
        await Assert.That(_dialogProvider.FindAll("[role='alert']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Render_WithEditLink_BindsAllSevenFieldFlags()
    {
        RegisterService();
        var cut = RenderDialog(CreateResource(withEditLink: true));

        // MudSwitch renders one checkbox input per flag; all seven policy fields must be operable.
        await Assert.That(_dialogProvider!.FindAll("input[type='checkbox']").Count).IsEqualTo(7);
    }

    [Test]
    public async Task SaveAsync_SendsEveryFlagAudienceAndConcurrencyToken()
    {
        var eventId = Guid.NewGuid();
        var eventLocationId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var service = RegisterService();
        UpdateEventLocationDisclosureDto? captured = null;
        service.UpdateDisclosureAsync(
                eventId,
                eventLocationId,
                Arg.Do<UpdateEventLocationDisclosureDto>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = RenderDialog(
            CreateResource(withEditLink: true, eventLocationId: eventLocationId, stamp: stamp, policyVersion: 9),
            eventId,
            eventLocationId);

        SetPrivateField(cut.Instance, "_showCity", true);
        SetPrivateField(cut.Instance, "_showStreetAddress", false);
        SetPrivateField(cut.Instance, "_fullDetailsAudienceId", 2);
        SetPrivateField(cut.Instance, "_revealDate", (DateTime?)new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        SetPrivateField(cut.Instance, "_revealTime", (TimeSpan?)new TimeSpan(18, 30, 0));

        await cut.InvokeAsync(() => InvokePrivateTaskAsync(cut.Instance, "SaveAsync"));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.ExpectedConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(captured.ExpectedPolicyVersion).IsEqualTo(9);
        await Assert.That(captured.Fields!.ShowCity).IsEqualTo(true);
        await Assert.That(captured.Fields.ShowStreetAddress).IsEqualTo(false);
        await Assert.That(captured.Audience!.FullDetailsAudienceId).IsEqualTo(2);
        await Assert.That(captured.Audience.RevealFullDetailsFromUtc!.HasValue).IsEqualTo(true);
        await Assert.That(captured.Audience.RevealFullDetailsFromUtc.Value!.Value.UtcDateTime)
            .IsEqualTo(new DateTime(2026, 9, 1, 18, 30, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task SaveAsync_ClearsTheRevealInstant_WhenNoDateIsChosen()
    {
        var service = RegisterService();
        UpdateEventLocationDisclosureDto? captured = null;
        service.UpdateDisclosureAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Do<UpdateEventLocationDisclosureDto>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = RenderDialog(CreateResource(withEditLink: true));
        SetPrivateField(cut.Instance, "_revealDate", (DateTime?)null);

        await cut.InvokeAsync(() => InvokePrivateTaskAsync(cut.Instance, "SaveAsync"));

        await Assert.That(captured!.Audience!.RevealFullDetailsFromUtc!.HasValue).IsEqualTo(true);
        await Assert.That(captured.Audience.RevealFullDetailsFromUtc.Value).IsNull();
    }

    [Test]
    public async Task SaveAsync_WithoutEditLink_NeverReachesTheApi()
    {
        var service = RegisterService();
        var cut = RenderDialog(CreateResource(withEditLink: false));

        await cut.InvokeAsync(() => InvokePrivateTaskAsync(cut.Instance, "SaveAsync"));

        await service.DidNotReceive().UpdateDisclosureAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<UpdateEventLocationDisclosureDto>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveAsync_OnConflict_KeepsTheDialogOpenAndReportsTheServerMessage()
    {
        var service = RegisterService();
        service.UpdateDisclosureAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<UpdateEventLocationDisclosureDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "The location policy changed. Reload and try again."
            });

        var cut = RenderDialog(CreateResource(withEditLink: true));

        await cut.InvokeAsync(() => InvokePrivateTaskAsync(cut.Instance, "SaveAsync"));
        cut.Render();

        await Assert.That(GetPrivateField<string?>(cut.Instance, "_error"))
            .IsEqualTo("The location policy changed. Reload and try again.");
        await Assert.That(_dialogProvider!.FindComponents<EventLocationEditorDialog>().Count).IsEqualTo(1);
    }

    [Test]
    public async Task Render_SeedsEveryControlFromTheServerPolicy()
    {
        RegisterService();
        var resource = CreateResource(withEditLink: true);
        resource.Policy = new EventLocationDisclosurePolicyDto
        {
            ShowVenueName = true,
            ShowCity = true,
            ShowCountry = true,
            ShowRoomName = false,
            ShowStreetAddress = false,
            ShowPostcode = false,
            ShowCoordinates = false,
            FullDetailsAudienceId = 2,
            RevealFullDetailsFromUtc = new DateTimeOffset(2026, 7, 4, 9, 15, 0, TimeSpan.Zero)
        };

        var cut = RenderDialog(resource);

        await Assert.That(GetPrivateField<bool>(cut.Instance, "_showVenueName")).IsTrue();
        await Assert.That(GetPrivateField<bool>(cut.Instance, "_showCoordinates")).IsFalse();
        await Assert.That(GetPrivateField<int>(cut.Instance, "_fullDetailsAudienceId")).IsEqualTo(2);
        await Assert.That(GetPrivateField<DateTime?>(cut.Instance, "_revealDate"))
            .IsEqualTo(new DateTime(2026, 7, 4));
        await Assert.That(GetPrivateField<TimeSpan?>(cut.Instance, "_revealTime"))
            .IsEqualTo(new TimeSpan(9, 15, 0));
    }

    public void Dispose() => _ctx.Dispose();

    private IEventLocationService RegisterService()
    {
        var service = Substitute.For<IEventLocationService>();
        _ctx.Services.AddSingleton(service);
        return service;
    }

    private static HalResourceOfEventLocationManagementDto CreateResource(
        bool withEditLink,
        Guid? eventLocationId = null,
        Guid? stamp = null,
        int policyVersion = 1)
    {
        var links = new Dictionary<string, HalLink>
        {
            ["self"] = new() { Href = "/api/events/e/locations/l/management" }
        };
        if (withEditLink)
        {
            links["edit"] = new HalLink { Href = "/api/events/e/locations/l/disclosure" };
        }

        return new HalResourceOfEventLocationManagementDto
        {
            EventLocationId = eventLocationId ?? Guid.NewGuid(),
            State = EventLocationDisclosureState.Private_venue,
            PolicyVersion = policyVersion,
            ConcurrencyStamp = stamp ?? Guid.NewGuid(),
            NeedsPrivacyReview = false,
            Policy = new EventLocationDisclosurePolicyDto { FullDetailsAudienceId = 3 },
            _links = links
        };
    }

    private IRenderedComponent<EventLocationEditorDialog> RenderDialog(
        HalResourceOfEventLocationManagementDto resource,
        Guid? eventId = null,
        Guid? eventLocationId = null)
    {
        _dialogProvider = _ctx.Render<MudDialogProvider>();
        var parameters = new DialogParameters<EventLocationEditorDialog>
        {
            { component => component.EventId, eventId ?? Guid.NewGuid() },
            { component => component.EventLocationId, eventLocationId ?? resource.EventLocationId!.Value },
            { component => component.Resource, resource }
        };
        var dialogService = _ctx.Services.GetRequiredService<IDialogService>();
        _ = dialogService.ShowAsync<EventLocationEditorDialog>("Location disclosure", parameters);
        _dialogProvider.WaitForState(
            () => _dialogProvider.FindComponents<EventLocationEditorDialog>().Count == 1,
            TimeSpan.FromSeconds(3));
        return _dialogProvider.FindComponent<EventLocationEditorDialog>();
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        return (T)field.GetValue(instance)!;
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        field.SetValue(instance, value);
    }

    private static Task InvokePrivateTaskAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        return (Task)method.Invoke(instance, null)!;
    }
}
