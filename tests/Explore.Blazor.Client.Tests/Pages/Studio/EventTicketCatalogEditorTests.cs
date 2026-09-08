using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Studio;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class EventTicketCatalogEditorTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventTicketingService _ticketingService;
    private readonly IEventDayService _eventDayService;
    private readonly Explore.Blazor.Client.Contracts.Services.IEventSessionService _eventSessionService;
    private readonly IDialogService _dialogService;
    private readonly IAccessibilityAnnouncerService _announcer;
    private readonly IAccessibilityFocusService _focusService;

    public EventTicketCatalogEditorTests()
    {
        _ticketingService = _ctx.AddMockService<IEventTicketingService>();
        _eventDayService = _ctx.AddMockService<IEventDayService>();
        _eventSessionService = _ctx.AddMockService<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        _dialogService = Substitute.For<IDialogService>();
        _announcer = _ctx.AddMockService<IAccessibilityAnnouncerService>();
        _focusService = _ctx.AddMockService<IAccessibilityFocusService>();
        _ctx.Services.AddSingleton(_dialogService);
        _eventDayService.GetDaysByEventAsync(Arg.Any<Guid>(), true, Arg.Any<CancellationToken>()).Returns([]);
        _eventSessionService.GetSessionsByEventAsync(Arg.Any<Guid>(), true, Arg.Any<CancellationToken>()).Returns([]);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task RenderWithoutMutationLinksKeepsCatalogReadOnly()
    {
        var eventId = Guid.CreateVersion7();
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateCatalog(eventId));

        var cut = _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.CanManageTicketTypes, true)
            .Add(component => component.CanManageCapacityPools, true));

        cut.WaitForElement("[data-testid='event-ticket-catalog-editor']");
        cut.WaitForAssertion(() => cut.Markup.Contains("No ticket types yet.", StringComparison.Ordinal));
        await Assert.That(cut.FindAll("[data-testid='add-ticket-type']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='add-capacity-pool']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='publish-ticket-catalog']")).IsEmpty();
    }

    [Test]
    public async Task RenderShowsOnlyControlsBackedByExactCatalogLinks()
    {
        var eventId = Guid.CreateVersion7();
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateCatalog(eventId, "create-type", "publish"));

        var cut = _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.CanManageTicketTypes, true));

        cut.WaitForElement("[data-testid='add-ticket-type']");
        await Assert.That(cut.FindAll("[data-testid='publish-ticket-catalog']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='add-capacity-pool']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='create-ticket-catalog-draft']")).IsEmpty();
    }

    [Test]
    public async Task PaidCatalogShowsServerBlockersAndUsesBoundedPaymentReadinessContract()
    {
        var eventId = Guid.CreateVersion7();
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(
            CreatePaidCatalog(eventId, ready: false, "preflight", "commercial-disclosures", "payment-connection", "start-onboarding"));
        _ticketingService.GetPaymentConnectionAsync(eventId, Arg.Any<CancellationToken>()).Returns(
            new HalResourceOfEventOrganizerPaymentConnectionManagementDto
            {
                EventId = eventId,
                Connection = new Connection2
                {
                    StatusId = 2,
                    MerchantCountryCode = "US",
                    ChargeCapabilityStateId = 2,
                    RequirementsStateId = 2,
                    SupportedCurrencyCodes = ["USD"]
                }
            });

        var cut = Render(eventId);

        cut.WaitForElement("[data-testid='payment-connection-present']");
        await Assert.That(cut.Markup).Contains("Paid publication requires merchant, refund, and support disclosures.");
        await Assert.That(cut.FindAll("[data-testid='save-commercial-disclosures']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='start-payment-onboarding']").Count).IsEqualTo(1);
        string[] exposedProperties = typeof(Connection2).GetProperties().Select(property => property.Name).ToArray();
        await Assert.That(exposedProperties).DoesNotContain("ProviderCode");
        await Assert.That(exposedProperties).DoesNotContain("ConnectPlatformId");
        await Assert.That(exposedProperties).DoesNotContain("ExternalAccountId");
    }

    [Test]
    public async Task MissingPaymentRelationDoesNotInferConnectionStateOrCallService()
    {
        var eventId = Guid.CreateVersion7();
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(
            CreatePaidCatalog(eventId, ready: false, "preflight"));

        var cut = Render(eventId);

        cut.WaitForElement("[data-testid='payment-connection-not-advertised']");
        await Assert.That(cut.FindAll("[data-testid='payment-connection-missing']")).IsEmpty();
        await _ticketingService.DidNotReceive().GetPaymentConnectionAsync(eventId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CommercialDisclosureAffordanceSavesGeneratedRequestAndReloads()
    {
        var eventId = Guid.CreateVersion7();
        EventTicketCatalogState catalog = CreatePaidCatalog(eventId, ready: false, "commercial-disclosures");
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(catalog, catalog);
        _ticketingService.UpdateCommercialDisclosuresAsync(
                eventId,
                Arg.Any<UpdateEventTicketCatalogCommercialDisclosuresCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = Render(eventId);
        cut.WaitForElement("[data-testid='save-commercial-disclosures']").Click();

        cut.WaitForElement("[data-testid='ticket-catalog-status']");
        await _ticketingService.Received(1).UpdateCommercialDisclosuresAsync(
            eventId,
            Arg.Is<UpdateEventTicketCatalogCommercialDisclosuresCommand>(request =>
                request.EventId == eventId
                && request.MerchantDisclosureText == "Merchant disclosure"
                && request.RefundPolicyDisclosureText == "Refund policy"
                && request.SupportContactDisclosureText == "Support contact"),
            Arg.Any<CancellationToken>());
        await _ticketingService.Received(2).GetCatalogAsync(eventId, Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("Commercial disclosures saved.");
    }

    [Test]
    public async Task ReadinessRefreshUsesPreflightHalPublishAffordance()
    {
        var eventId = Guid.CreateVersion7();
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(
            CreatePaidCatalog(eventId, ready: false, "preflight"));
        _ticketingService.GetPaidPublicationPreflightAsync(eventId, Arg.Any<CancellationToken>()).Returns(
            new HalResourceOfPaidEventPublicationPreflightDto
            {
                EventId = eventId,
                IsPaidCatalog = true,
                IsReady = true,
                Blockers = [],
                _links = new Dictionary<string, HalLink>
                {
                    ["publish"] = new() { Href = $"/api/events/{eventId}/ticketing/publish", Method = "POST" }
                }
            });

        var cut = Render(eventId);
        cut.WaitForElement("[data-testid='refresh-paid-publication-readiness']").Click();

        cut.WaitForElement("[data-testid='paid-publication-ready']");
        await Assert.That(cut.FindAll("[data-testid='publish-ticket-catalog']").Count).IsEqualTo(1);
        await _ticketingService.Received(1).GetPaidPublicationPreflightAsync(eventId, Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("Paid publication readiness refreshed.");
    }

    [Test]
    public async Task ExactOnboardingAffordanceNavigatesToServerUrl()
    {
        var eventId = Guid.CreateVersion7();
        var onboardingUrl = new Uri("https://payments.example.test/onboarding");
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(
            CreatePaidCatalog(eventId, ready: false, "start-onboarding"));
        _ticketingService.StartPaymentOnboardingAsync(eventId, Arg.Any<CancellationToken>()).Returns(
            new BaseCommandResponseOfOrganizerPaymentOnboardingLinkResult
            {
                Success = true,
                Id = new OrganizerPaymentOnboardingLinkResult
                {
                    OnboardingUrl = onboardingUrl
                }
            });

        var cut = Render(eventId);
        cut.WaitForElement("[data-testid='start-payment-onboarding']").Click();

        cut.WaitForState(() => _ctx.Services.GetRequiredService<NavigationManager>().Uri == onboardingUrl.ToString());
        await _ticketingService.Received(1).StartPaymentOnboardingAsync(eventId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateDraftUsesSelectedCurrencyAndReloadsCatalog()
    {
        var eventId = Guid.CreateVersion7();
        var initial = CreateCatalog(eventId, "create-draft");
        var created = CreateCatalog(eventId, "create-type", "create-pool", "publish");
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(initial, created);
        _ticketingService.CreateDraftAsync(eventId, Arg.Any<CreateEventTicketCatalogDraftCommand>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.CanManageCapacityPools, true));

        cut.WaitForElement("[data-testid='create-ticket-catalog-draft']").Click();
        cut.WaitForElement("[data-testid='ticket-catalog-status']");

        await _ticketingService.Received(1).CreateDraftAsync(
            eventId,
            Arg.Is<CreateEventTicketCatalogDraftCommand>(request => request.EventId == eventId && request.CurrencyCode == "USD"),
            Arg.Any<CancellationToken>());
        await Assert.That(cut.FindAll("[data-testid='add-capacity-pool']").Count).IsEqualTo(1);
        await _announcer.Received(1).AnnouncePoliteAsync("Draft created.");
    }

    [Test]
    [Arguments(true, false, 1, 0)]
    [Arguments(false, true, 0, 1)]
    [Arguments(false, false, 0, 0)]
    public async Task EventCapabilitiesIndependentlyGateTypeAndPoolControls(
        bool canManageTypes,
        bool canManagePools,
        int expectedTypeControls,
        int expectedPoolControls)
    {
        var eventId = Guid.CreateVersion7();
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateCatalog(eventId, "create-type", "create-pool"));

        var cut = _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.CanManageTicketTypes, canManageTypes)
            .Add(component => component.CanManageCapacityPools, canManagePools));

        cut.WaitForElement("[data-testid='event-ticket-catalog-editor']");
        cut.WaitForAssertion(() => cut.Markup.Contains("No ticket types yet.", StringComparison.Ordinal));
        await Assert.That(cut.FindAll("[data-testid='add-ticket-type']").Count).IsEqualTo(expectedTypeControls);
        await Assert.That(cut.FindAll("[data-testid='add-capacity-pool']").Count).IsEqualTo(expectedPoolControls);
    }

    [Test]
    public async Task LoadAnnouncesCatalogCountsAndProvidesManagedTargetsToInlineEditor()
    {
        var eventId = Guid.CreateVersion7();
        var dayId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateCatalog(eventId, "create-type"));
        _eventDayService.GetDaysByEventAsync(eventId, true, Arg.Any<CancellationToken>()).Returns([new EventDayListDto { Id = dayId, Label = "Friday" }]);
        _eventSessionService.GetSessionsByEventAsync(eventId, true, Arg.Any<CancellationToken>()).Returns([new EventSessionListDto { Id = sessionId, EventTitle = "Event", Title = "Opening" }]);

        var cut = _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.CanManageTicketTypes, true));

        cut.WaitForElement("[data-testid='add-ticket-type']").Click();
        var inlineEditor = cut.FindComponent<EventTicketTypeEditor>();
        await Assert.That(inlineEditor.Instance.EventDays.Single().Id).IsEqualTo(dayId);
        await Assert.That(inlineEditor.Instance.EventSessions.Single().Id).IsEqualTo(sessionId);
        await _announcer.Received(1).AnnouncePoliteAsync("Ticket catalog loaded: 0 ticket types and 0 capacity pools.");
    }

    [Test]
    public async Task Dispose_DuringPendingLoad_CancelsRequestWithoutShowingError()
    {
        var eventId = Guid.CreateVersion7();
        var completion = new TaskCompletionSource<EventTicketCatalogState?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = default;
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(call =>
        {
            observedToken = call.ArgAt<CancellationToken>(1);
            return completion.Task;
        });
        var cut = _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, eventId));
        cut.WaitForState(() => observedToken.CanBeCanceled);
        await Assert.That(observedToken.CanBeCanceled).IsTrue();
        await Assert.That(cut.FindAll("[role='alert']")).IsEmpty();

        cut.Instance.Dispose();
        cut.Dispose();

        await Assert.That(observedToken.IsCancellationRequested).IsTrue();
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
        completion.TrySetCanceled(observedToken);
    }

    [Test]
    public async Task EventIdChange_CancelsPendingRequestsAndIgnoresStaleResults()
    {
        var firstEventId = Guid.CreateVersion7();
        var secondEventId = Guid.CreateVersion7();
        var catalogCompletion = new TaskCompletionSource<EventTicketCatalogState?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var daysCompletion = new TaskCompletionSource<ICollection<EventDayListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionsCompletion = new TaskCompletionSource<ICollection<EventSessionListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken catalogToken = default;
        CancellationToken daysToken = default;
        CancellationToken sessionsToken = default;
        _ticketingService.GetCatalogAsync(firstEventId, Arg.Any<CancellationToken>()).Returns(call =>
        {
            catalogToken = call.ArgAt<CancellationToken>(1);
            return catalogCompletion.Task;
        });
        _eventDayService.GetDaysByEventAsync(firstEventId, true, Arg.Any<CancellationToken>()).Returns(call =>
        {
            daysToken = call.ArgAt<CancellationToken>(2);
            return daysCompletion.Task;
        });
        _eventSessionService.GetSessionsByEventAsync(firstEventId, true, Arg.Any<CancellationToken>()).Returns(call =>
        {
            sessionsToken = call.ArgAt<CancellationToken>(2);
            return sessionsCompletion.Task;
        });
        _ticketingService.GetCatalogAsync(secondEventId, Arg.Any<CancellationToken>()).Returns(CreateCatalog(secondEventId, "publish"));

        var cut = _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, firstEventId));
        cut.WaitForState(() => catalogToken.CanBeCanceled && daysToken.CanBeCanceled && sessionsToken.CanBeCanceled);

        cut.Render(parameters => parameters.Add(component => component.EventId, secondEventId));
        cut.WaitForElement("[data-testid='publish-ticket-catalog']");
        await Assert.That(catalogToken.IsCancellationRequested).IsTrue();
        await Assert.That(daysToken.IsCancellationRequested).IsTrue();
        await Assert.That(sessionsToken.IsCancellationRequested).IsTrue();

        catalogCompletion.SetResult(CreateCatalog(firstEventId, "create-type"));
        daysCompletion.SetResult([new EventDayListDto { Id = Guid.CreateVersion7(), Label = "Stale day" }]);
        sessionsCompletion.SetResult([new EventSessionListDto { Id = Guid.CreateVersion7(), EventTitle = "Stale event", Title = "Stale session" }]);
        await cut.InvokeAsync(() => Task.CompletedTask);

        await Assert.That(cut.FindAll("[data-testid='publish-ticket-catalog']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='add-ticket-type']")).IsEmpty();
        await _announcer.Received(1).AnnouncePoliteAsync("Ticket catalog loaded: 0 ticket types and 0 capacity pools.");
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
    }

    [Test]
    public async Task PublishCancellationRestoresFocusAndPerformsNoMutation()
    {
        var eventId = Guid.CreateVersion7();
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateCatalog(eventId, "publish"));
        _dialogService.ShowMessageBoxAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DialogOptions>())
            .Returns(false);

        var cut = _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='publish-ticket-catalog']").Click();
        await _focusService.Received(1).SaveFocusAsync();
        await _focusService.Received(1).RestoreFocusAsync();
        await _ticketingService.DidNotReceive().PublishAsync(eventId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MutationFailureIsAnnouncedAssertively()
    {
        var eventId = Guid.CreateVersion7();
        _ticketingService.GetCatalogAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateCatalog(eventId, "create-draft"));
        _ticketingService.CreateDraftAsync(eventId, Arg.Any<CreateEventTicketCatalogDraftCommand>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = false, Message = "Catalog is locked." });

        var cut = _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='create-ticket-catalog-draft']").Click();
        cut.WaitForElement("[data-testid='ticket-catalog-error']");
        await _announcer.Received(1).AnnounceAssertiveAsync("Catalog is locked.");
    }

    [Test]
    public async Task MissingSupportDataUsesBoundedGuidanceWithoutRawIdentifierInput()
    {
        var model = TicketTypeEditModel.Create();
        model.Entitlements.Single().SetScope(2);

        var cut = _ctx.RenderMudComponent<EventTicketTypeEditor>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.SupportDataUnavailable, true));

        cut.WaitForElement("[data-testid='ticket-entitlement-support-guidance']");
        await Assert.That(cut.Markup).DoesNotContain("Event day ID");
        await Assert.That(cut.Markup).Contains("whole-event admission");
    }

    private static EventTicketCatalogState CreateCatalog(Guid eventId, params string[] relations) => new(
        eventId,
        Guid.CreateVersion7(),
        1,
        "USD",
        1,
        "DRAFT",
        "Draft",
        [],
        [],
        relations.ToDictionary(
            relation => relation,
            relation => new HalLink { Href = $"/api/events/{eventId}/ticketing/{relation}", Method = "POST" },
            StringComparer.Ordinal));

    private IRenderedComponent<EventTicketCatalogEditor> Render(Guid eventId) =>
        _ctx.RenderMudComponent<EventTicketCatalogEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.CanManageTicketTypes, true)
            .Add(component => component.CanManageCapacityPools, true));

    private static EventTicketCatalogState CreatePaidCatalog(Guid eventId, bool ready, params string[] relations) =>
        CreateCatalog(eventId, relations) with
        {
            MerchantDisclosureText = "Merchant disclosure",
            RefundPolicyDisclosureText = "Refund policy",
            SupportContactDisclosureText = "Support contact",
            PublicationPreflight = new EventTicketCatalogPaidPreflightState(
                true,
                ready,
                ready
                    ? []
                    : [new EventTicketCatalogPaidPreflightBlockerState("commercial_disclosures_missing", "Paid publication requires merchant, refund, and support disclosures.")])
        };
}

public sealed class TicketCatalogEditModelsTests
{
    [Test]
    public async Task TicketTypePricingModesRejectIncompatibleAmounts()
    {
        var model = TicketTypeEditModel.Create();
        model.Name = "General admission";

        model.SetPricingMode(1);
        await Assert.That(model.IsValid).IsFalse();
        model.FixedPriceMinor = 2500;
        await Assert.That(model.IsValid).IsTrue();

        model.SetPricingMode(2);
        await Assert.That(model.FixedPriceMinor).IsNull();
        await Assert.That(model.IsValid).IsTrue();

        model.SetPricingMode(5);
        model.MinimumPriceMinor = 1000;
        model.SuggestedPriceMinor = 500;
        await Assert.That(model.IsValid).IsFalse();
    }

    [Test]
    public async Task EntitlementScopeClearsForeignTargetIdentifiers()
    {
        var model = TicketEntitlementEditModel.Create();
        model.SetScope(3);
        model.EventSessionId = Guid.CreateVersion7();
        await Assert.That(model.IsValid).IsTrue();

        model.SetScope(1);
        await Assert.That(model.EventSessionId).IsNull();
        await Assert.That(model.SelectionRuleId).IsEqualTo(1);
        await Assert.That(model.IsValid).IsTrue();
    }
}
