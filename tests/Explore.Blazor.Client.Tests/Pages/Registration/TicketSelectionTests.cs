using System.Reflection;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Pages.Registration;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Registration;

public sealed class TicketSelectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IRegistrationOrderService _service;

    public TicketSelectionTests()
    {
        _service = _ctx.AddMockService<IRegistrationOrderService>();
        _ctx.JSInterop.SetupVoid("Blazor._internal.NavigationLock.enableNavigationPrompt", _ => true);
        _ctx.JSInterop.SetupVoid("Blazor._internal.NavigationLock.disableNavigationPrompt", _ => true);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task PaidCheckout_RendersStructuredDirectoryOperatorAndLegalLinks_WithoutProseDisclaimer()
    {
        PropertyInfo? directoryProperty = typeof(RegistrationCheckoutCompositionDto).GetProperty("DirectoryOperator");
        await Assert.That(directoryProperty).IsNotNull();
        if (directoryProperty is null) return;

        _ctx.SetAnonymousUser();
        Guid eventId = Guid.CreateVersion7();
        var composition = new RegistrationCheckoutCompositionDto
        {
            EventId = eventId,
            TicketCatalogVersionId = Guid.CreateVersion7(),
            CurrencyCode = "EUR",
            TicketTypes =
            [
                new RegistrationCheckoutTicketTypeDto
                {
                    Id = Guid.CreateVersion7(),
                    Name = "General admission",
                    TicketPricingModeCode = "FIXED",
                    FixedPriceMinor = 1200
                }
            ]
        };
        SetDirectoryOperator(composition, directoryProperty);
        _service.GetCheckoutAsync(eventId, Arg.Any<CancellationToken>()).Returns(composition);

        var cut = _ctx.RenderMudComponent<TicketSelection>(
            parameters => parameters.Add(component => component.EventId, eventId));

        IReadOnlyList<AngleSharp.Dom.IElement> disclosures = cut.FindAll("[data-testid='ticket-selection-directory-operator']");
        await Assert.That(disclosures.Count).IsEqualTo(1);
        if (disclosures.Count != 1) return;
        AngleSharp.Dom.IElement disclosure = disclosures[0];
        await Assert.That(disclosure.TextContent).Contains("Directory operator");
        await Assert.That(disclosure.TextContent).Contains("Community Directory Foundation");
        await Assert.That(disclosure.QuerySelector("a[href='https://directory.example/legal']")).IsNotNull();
        await Assert.That(disclosure.QuerySelector("a[href='https://directory.example/terms']")).IsNotNull();
        await Assert.That(disclosure.QuerySelector("a[href='https://directory.example/privacy']")).IsNotNull();
        await Assert.That(cut.FindAll("[data-testid='ticket-selection-paid-event-directory-disclaimer']").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).DoesNotContain("provides an event discovery and management directory only");
        await Assert.That(typeof(RegistrationCheckoutCompositionDto).GetProperty("PaidEventDirectoryDisclaimer"))
            .IsNull();
    }

    [Test]
    public async Task SubmitGuestSelection_UsesServerCatalogAndNavigatesToRecovery()
    {
        _ctx.SetAnonymousUser();
        var eventId = Guid.CreateVersion7();
        var catalogId = Guid.CreateVersion7();
        var ticketId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        _service.GetCheckoutAsync(eventId, Arg.Any<CancellationToken>()).Returns(new RegistrationCheckoutCompositionDto
        {
            EventId = eventId,
            TicketCatalogVersionId = catalogId,
            CurrencyCode = "EUR",
            DirectoryOperator = new TenantDirectoryOperatorPublicDto(),
            TicketTypes = [new RegistrationCheckoutTicketTypeDto { Id = ticketId, Name = "General admission", TicketPricingModeCode = "FIXED", FixedPriceMinor = 1200 }]
        });
        _service.StartGuestAsync(eventId, Arg.Any<StartRegistrationOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GuestRegistrationOrderStartDto { Id = orderId, Success = true });

        var cut = _ctx.RenderMudComponent<TicketSelection>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.Find("input").Change("1");
        await cut.Find("[data-testid='ticket-reservation-action']").ClickAsync(new());

        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri)
            .EndsWith($"/registration/guest/events/{eventId}/orders/{orderId}");
        await _service.Received(1).StartGuestAsync(
            eventId,
            Arg.Is<StartRegistrationOrderRequest>(request =>
                request.TicketCatalogVersionId == catalogId
                && request.Lines!.Single().TicketTypeId == ticketId
                && request.Lines.Single().Quantity == 1),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SlidingScaleSelection_SubmitsBuyerPriceFromLinkedServerOption()
    {
        _ctx.SetAnonymousUser();
        var eventId = Guid.CreateVersion7();
        var ticketId = Guid.CreateVersion7();
        _service.GetCheckoutAsync(eventId, Arg.Any<CancellationToken>()).Returns(new RegistrationCheckoutCompositionDto
        {
            EventId = eventId,
            TicketCatalogVersionId = Guid.CreateVersion7(),
            CurrencyCode = "EUR",
            DirectoryOperator = new TenantDirectoryOperatorPublicDto(),
            TicketTypes = [new RegistrationCheckoutTicketTypeDto
            {
                Id = ticketId,
                Name = "Community rate",
                TicketPricingModeCode = "SLIDING_SCALE",
                SlidingScaleOptions =
                [
                    new RegistrationCheckoutSlidingScaleOptionDto { BuyerPriceMinor = 500, OrganizerEarningsMinor = 450 },
                    new RegistrationCheckoutSlidingScaleOptionDto { BuyerPriceMinor = 1000, OrganizerEarningsMinor = 900 }
                ]
            }]
        });
        _service.StartGuestAsync(eventId, Arg.Any<StartRegistrationOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GuestRegistrationOrderStartDto { Id = Guid.CreateVersion7(), Success = true });

        var cut = _ctx.RenderMudComponent<TicketSelection>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.FindAll("input").Single(input => input.GetAttribute("type") != "range").Change("1");
        cut.Find("input[type='range']").Input("0");
        await cut.FindAll("button").Single(button => button.TextContent.Contains("Reserve selected tickets", StringComparison.Ordinal)).ClickAsync(new());

        await _service.Received(1).StartGuestAsync(
            eventId,
            Arg.Is<StartRegistrationOrderRequest>(request => request.Lines!.Single().ChosenUnitPriceMinor == 500),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SlidingScaleSelection_RendersBothServerAuthoredValues()
    {
        _ctx.SetAnonymousUser();
        var eventId = Guid.CreateVersion7();
        var ticketId = Guid.CreateVersion7();
        _service.GetCheckoutAsync(eventId, Arg.Any<CancellationToken>()).Returns(new RegistrationCheckoutCompositionDto
        {
            EventId = eventId,
            TicketCatalogVersionId = Guid.CreateVersion7(),
            CurrencyCode = "EUR",
            DirectoryOperator = new TenantDirectoryOperatorPublicDto(),
            TicketTypes =
            [
                new RegistrationCheckoutTicketTypeDto
                {
                    Id = ticketId,
                    Name = "Community rate",
                    TicketPricingModeCode = "SLIDING_SCALE",
                    SlidingScaleOptions =
                    [
                        new RegistrationCheckoutSlidingScaleOptionDto { BuyerPriceMinor = 500, OrganizerEarningsMinor = 450 }
                    ]
                }
            ]
        });

        var cut = _ctx.RenderMudComponent<TicketSelection>(parameters => parameters.Add(component => component.EventId, eventId));

        await Assert.That(cut.Find("output")).IsNotNull();
        await Assert.That(cut.Markup).Contains("500 EUR minor units");
        await Assert.That(cut.Markup).Contains("Organizer earns");
        await Assert.That(cut.Markup).Contains("450 EUR minor units");
    }

    [Test]
    public async Task MissingDirectoryOperator_BlocksAllPaidModes_ButFreeRemainsAvailable()
    {
        foreach (string mode in new[] { "FIXED", "DONATION", "PAY_WHAT_YOU_CAN", "SLIDING_SCALE" })
        {
            using var context = new BlazorTestContext();
            context.SetAnonymousUser();
            IRegistrationOrderService service = context.AddMockService<IRegistrationOrderService>();
            Guid eventId = Guid.CreateVersion7();
            service.GetCheckoutAsync(eventId, Arg.Any<CancellationToken>()).Returns(Composition(eventId, mode));
            var cut = context.RenderMudComponent<TicketSelection>(p => p.Add(c => c.EventId, eventId));
            await Assert.That(cut.FindAll("[data-testid='paid-checkout-identity-unavailable']").Count).IsEqualTo(1);
            await Assert.That(cut.FindAll("button").Any(b => b.TextContent.Contains("Reserve selected tickets"))).IsFalse();
        }

        _ctx.SetAnonymousUser();
        Guid freeId = Guid.CreateVersion7();
        _service.GetCheckoutAsync(freeId, Arg.Any<CancellationToken>()).Returns(Composition(freeId, "FREE"));
        var free = _ctx.RenderMudComponent<TicketSelection>(p => p.Add(c => c.EventId, freeId));
        await Assert.That(free.FindAll("[data-testid='ticket-reservation-action']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task SlidingScale_UsesValueTextWithoutDuplicateLiveAnnouncement()
    {
        _ctx.SetAnonymousUser();
        Guid eventId = Guid.CreateVersion7();
        RegistrationCheckoutCompositionDto composition = Composition(eventId, "SLIDING_SCALE");
        SetDirectoryOperator(composition, typeof(RegistrationCheckoutCompositionDto).GetProperty("DirectoryOperator")!);
        _service.GetCheckoutAsync(eventId, Arg.Any<CancellationToken>()).Returns(composition);
        var cut = _ctx.RenderMudComponent<TicketSelection>(p => p.Add(c => c.EventId, eventId));
        var slider = cut.Find("input[type='range']");
        string outputId = slider.GetAttribute("aria-describedby")!;
        await Assert.That(slider.GetAttribute("aria-valuetext")).Contains("You pay");
        var output = cut.Find($"#{outputId}");
        await Assert.That(output.TextContent).Contains("Organizer earns");
        await Assert.That(output.HasAttribute("role")).IsFalse();
        await Assert.That(output.GetAttribute("aria-live")).IsEqualTo("off");
    }

    [Test]
    public async Task InFlightReservation_AnnouncesBusyStateAndLabel()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<GuestRegistrationOrderStartDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ctx.SetAnonymousUser();
        Guid eventId = Guid.CreateVersion7();
        _service.GetCheckoutAsync(eventId, Arg.Any<CancellationToken>()).Returns(Composition(eventId, "FREE"));
        _service.StartGuestAsync(eventId, Arg.Any<StartRegistrationOrderRequest>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            entered.TrySetResult();
            return await completion.Task;
        });
        var cut = _ctx.RenderMudComponent<TicketSelection>(p => p.Add(c => c.EventId, eventId));
        cut.Find("input").Change("1");
        Task click = cut.Find("[data-testid='ticket-reservation-action']").ClickAsync(new());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(cut.Find("[data-testid='ticket-reservation-action']").GetAttribute("aria-busy")).IsEqualTo("true");
        await Assert.That(cut.Markup).Contains("Reserving selected tickets");
        completion.SetResult(null);
        await click;
    }

    private static RegistrationCheckoutCompositionDto Composition(Guid eventId, string mode) => new()
    {
        EventId = eventId,
        TicketCatalogVersionId = Guid.CreateVersion7(),
        CurrencyCode = "EUR",
        TicketTypes = [new RegistrationCheckoutTicketTypeDto
        {
            Id = Guid.CreateVersion7(), Name = "Admission", TicketPricingModeCode = mode,
            FixedPriceMinor = mode == "FIXED" ? 1200 : null,
            MinimumPriceMinor = mode == "FREE" ? null : 500,
            SlidingScaleOptions = mode == "SLIDING_SCALE"
                ? [new RegistrationCheckoutSlidingScaleOptionDto { BuyerPriceMinor = 500, OrganizerEarningsMinor = 450 }]
                : null
        }]
    };

    private static void SetDirectoryOperator(
        RegistrationCheckoutCompositionDto composition,
        PropertyInfo directoryProperty)
    {
        Type operatorType = Nullable.GetUnderlyingType(directoryProperty.PropertyType) ?? directoryProperty.PropertyType;
        object operatorValue = Activator.CreateInstance(operatorType)
            ?? throw new InvalidOperationException($"Could not create {operatorType.Name}.");
        var values = new Dictionary<string, object?>
        {
            ["DocumentRevision"] = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            ["PublicName"] = "Community Directory",
            ["LegalName"] = "Community Directory Foundation",
            ["OperatorKindCode"] = "NONPROFIT",
            ["JurisdictionCountryCode"] = "DE",
            ["RegistrationIdentifier"] = "VR 12345",
            ["PublicContactEmail"] = "support@directory.example",
            ["LegalNoticeUrl"] = "https://directory.example/legal",
            ["TermsUrl"] = "https://directory.example/terms",
            ["PrivacyUrl"] = "https://directory.example/privacy"
        };
        foreach ((string name, object? value) in values)
        {
            PropertyInfo property = operatorType.GetProperty(name)
                ?? throw new InvalidOperationException($"{operatorType.Name} does not expose {name}.");
            property.SetValue(operatorValue, value);
        }
        directoryProperty.SetValue(composition, operatorValue);
    }

    [Test]
    public async Task NullPerOrderLimit_AllowsQuantitiesAboveNinetyNine()
    {
        _ctx.SetAnonymousUser();
        var eventId = Guid.CreateVersion7();
        _service.GetCheckoutAsync(eventId, Arg.Any<CancellationToken>()).Returns(new RegistrationCheckoutCompositionDto
        {
            EventId = eventId,
            TicketCatalogVersionId = Guid.CreateVersion7(),
            CurrencyCode = "EUR",
            DirectoryOperator = new TenantDirectoryOperatorPublicDto(),
            TicketTypes = [new RegistrationCheckoutTicketTypeDto
            {
                Id = Guid.CreateVersion7(),
                Name = "General admission",
                TicketPricingModeCode = "FIXED",
                FixedPriceMinor = 1200,
                PerOrderLimit = null
            }]
        });

        var cut = _ctx.RenderMudComponent<TicketSelection>(parameters => parameters.Add(component => component.EventId, eventId));

        var quantityField = cut.FindComponents<MudNumericField<int>>().Single();
        await Assert.That(quantityField.Instance.Max).IsEqualTo(int.MaxValue);

        cut.Find("input").Change("123");
        await cut.FindAll("button").Single(button => button.TextContent.Contains("Reserve selected tickets", StringComparison.Ordinal)).ClickAsync(new());

        await _service.Received(1).StartGuestAsync(
            eventId,
            Arg.Is<StartRegistrationOrderRequest>(request => request.Lines!.Single().Quantity == 123),
            Arg.Any<CancellationToken>());
    }
}
