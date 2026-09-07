using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Registration.FormRenderer;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Registration;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Registration;

public sealed class OrderRecoveryTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IRegistrationOrderService _service;
    private readonly IRegistrationPaymentService _paymentService;
    private readonly INativeRegistrationFormService _nativeForms;
    private readonly IGuestRegistrationOrderCapabilityStore _capabilityStore;
    private readonly IAccessibilityAnnouncerService _announcer;
    private readonly IAccessibilityFocusService _focus;
    private readonly IBrowserActionInterop _browserActions;

    public OrderRecoveryTests()
    {
        _ctx.Services.AddSingleton<TimeProvider>(
            new FixedTimeProvider(TestTime.UtcNow));
        _service = _ctx.AddMockService<IRegistrationOrderService>();
        _paymentService = _ctx.AddMockService<IRegistrationPaymentService>();
        _nativeForms = _ctx.AddMockService<INativeRegistrationFormService>();
        _capabilityStore = _ctx.AddMockService<IGuestRegistrationOrderCapabilityStore>();
        _browserActions = _ctx.AddMockService<IBrowserActionInterop>();
        _announcer = _ctx.Services.GetRequiredService<IAccessibilityAnnouncerService>();
        _focus = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task AuthenticatedRecovery_RendersServerStatusAndHoldCountdownWithoutCancelRelation()
    {
        var order = CreateOrder("EXPIRED", "Expired");
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(order));

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => cut.Find("h2").TextContent.Contains("Expired", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("This reservation has expired.");
        await Assert.That(cut.Markup).DoesNotContain("Cancel registration order");
    }

    [Test]
    public async Task AuthenticatedRecovery_RendersCancelOnlyWhenOrderHalContainsCancel()
    {
        var order = CreateOrder("AWAITING_APPROVAL", "Awaiting approval", "cancel");
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(order));

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => cut.Markup.Contains("Cancel registration order", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("This registration is waiting for organizer approval.");
    }

    [Test]
    public async Task AuthenticatedRecovery_RendersNativeJourneyOnlyFromRequirementProgressRelation()
    {
        var order = CreateOrder("AWAITING_REQUIREMENTS", "Awaiting requirements", "requirement-progress");
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);
        _nativeForms.GetRequirementsAsync(order.EventId.Value, order.Id.Value, null, Arg.Any<CancellationToken>())
            .Returns(new NativeRegistrationRequirementCollectionView(
                [], new Dictionary<string, HalLink> { ["launch-attempt"] = new() { Href = "/launch", Method = "POST" } }));

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("All registration details are complete."));
        await _nativeForms.Received(1).GetRequirementsAsync(
            order.EventId.Value, order.Id.Value, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AuthenticatedRecovery_RendersLifecycleActionsAndServerContributionOptionsFromHal()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "continue", "finalize");
        order.PlatformContribution = new PlatformContribution2
        {
            SelectedBasisPoints = 0,
            Options =
            [
                new Options12 { ContributionBasisPoints = 0, AmountMinor = 0, IsDefault = true },
                new Options12 { ContributionBasisPoints = 500, AmountMinor = 625 }
            ]
        };
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(order));

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup).Contains("Continue registration");
            Assert.That(cut.Markup).Contains("Finalize registration");
        });
        await Assert.That(cut.Markup).Contains("Platform contribution");
        await Assert.That(cut.FindComponents<MudSelectItem<int?>>().Select(item => item.Instance.Value))
            .Contains(500);
    }

    [Test]
    public async Task AuthenticatedRecovery_ReadyForCheckout_RequiresExactLifecycleRelations()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout");
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Review your order and complete checkout."));
        await Assert.That(cut.Markup).DoesNotContain("Continue registration");
        await Assert.That(cut.Markup).DoesNotContain("Finalize registration");
    }

    [Test]
    public async Task AuthenticatedRecovery_InvokesContinueOnlyWhenContinueRelationExists()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "continue");
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);
        _service.ContinueCurrentAsync(order.EventId.Value, order.Id.Value, Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Continue registration"));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(button =>
            button.TextContent.Contains("Continue registration", StringComparison.Ordinal)).Click());

        await _service.Received(1).ContinueCurrentAsync(order.EventId.Value, order.Id.Value, null, Arg.Any<CancellationToken>());
        await _service.DidNotReceive().FinalizeCurrentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AuthenticatedRecovery_InvokesFinalizeOnlyWhenFinalizeRelationExists()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "finalize");
        order.TotalDueMinor = 0;
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);
        _service.FinalizeCurrentAsync(order.EventId.Value, order.Id.Value, Arg.Any<CancellationToken>()).Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Finalize registration"));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(button =>
            button.TextContent.Contains("Finalize registration", StringComparison.Ordinal)).Click());

        await _service.Received(1).FinalizeCurrentAsync(order.EventId.Value, order.Id.Value, Arg.Any<CancellationToken>());
        await _service.DidNotReceive().ContinueCurrentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AuthenticatedRecovery_ContinueSubmitsOnlySelectedContributionBasisPoints()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "continue");
        order.PlatformContribution = new PlatformContribution2
        {
            SelectedBasisPoints = 500,
            Options = [new Options12 { ContributionBasisPoints = 500, AmountMinor = 625 }]
        };
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);
        _service.ContinueCurrentAsync(order.EventId.Value, order.Id.Value, 500, Arg.Any<CancellationToken>()).Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Platform contribution"));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(button =>
            button.TextContent.Contains("Continue registration", StringComparison.Ordinal)).Click());

        await _service.Received(1).ContinueCurrentAsync(order.EventId.Value, order.Id.Value, 500, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AuthenticatedRecovery_ReadyForCheckout_RendersContributionValuesFromDto()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "continue");
        order.PlatformContribution = new PlatformContribution2
        {
            Heading = "Support this event",
            Body = "Choose an optional contribution.",
            SelectedBasisPoints = 500,
            Options =
            [
                new Options12 { ContributionBasisPoints = 0, AmountMinor = 0, IsDefault = true },
                new Options12 { ContributionBasisPoints = 500, AmountMinor = 625 }
            ]
        };
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>())
            .Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Support this event"));
        await Assert.That(cut.Markup).Contains("5%");
        await Assert.That(cut.Markup).Contains("625 EUR minor units");
    }

    [Test]
    public async Task AuthenticatedRecovery_RendersPromotionAndSeparatedTotalsFromOrderDto()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "remove-promotion", "finalize");
        order.PreDiscountOrganizerDirectedTotalMinor = 2_000;
        order.PromotionDiscountTotalMinor = 500;
        order.PostDiscountOrganizerDirectedTotalMinor = 1_500;
        order.PlatformFeeTotalMinor = 150;
        order.PlatformContributionTotalMinor = 75;
        order.TotalDueMinor = 1_575;
        order.AppliedPromotionDisplayLabel = "Promotion ending in 10";
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Promotion ending in 10"));
        await Assert.That(cut.Markup).Contains("Organizer amount before promotion");
        await Assert.That(cut.Markup).Contains("Promotion discount");
        await Assert.That(cut.Markup).Contains("Organizer amount after promotion");
        await Assert.That(cut.Markup).Contains("Platform fee");
        await Assert.That(cut.Markup).Contains("Voluntary contribution");
        await Assert.That(cut.Markup).Contains("Final total");
        await Assert.That(cut.Markup).Contains("Remove promotion");
        await Assert.That(cut.Markup).DoesNotContain("Apply promotion code");
    }

    [Test]
    public async Task AuthenticatedRecovery_DifferentCodeFlowInstructsRemoveFirstWithGenericCopy()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "remove-promotion");
        order.AppliedPromotionDisplayLabel = "Promotion ending in 10";
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Remove this promotion before applying a different code."));
        await Assert.That(cut.Markup).DoesNotContain("SAVE10");
    }

    [Test]
    public async Task AuthenticatedRecovery_RemovePromotionUsesExactRelationAndReloadedOrder()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "remove-promotion");
        order.AppliedPromotionDisplayLabel = "Promotion ending in 10";
        var updated = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "apply-promotion");
        updated.EventId = order.EventId;
        updated.Id = order.Id;
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);
        _service.RemoveCurrentPromotionAsync(order.EventId.Value, order.Id.Value, order, Arg.Any<CancellationToken>()).Returns(updated);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Remove promotion"));
        await cut.InvokeAsync(() => cut.FindAll("button").Single(button =>
            button.TextContent.Contains("Remove promotion", StringComparison.Ordinal)).Click());

        await _service.Received(1).RemoveCurrentPromotionAsync(order.EventId.Value, order.Id.Value, order, Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).Contains("Promotion removed from your order.");
        await Assert.That(cut.Markup).DoesNotContain("Promotion ending in 10");
        await _announcer.Received(1).AnnouncePoliteAsync("Promotion removed from your order.");
    }

    [Test]
    public async Task AuthenticatedRecovery_InvalidPromotionShowsGenericLiveRegionMessage()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "apply-promotion");
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);
        _service.ApplyCurrentPromotionAsync(order.EventId.Value, order.Id.Value, order, "BADCODE", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(null));

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForElement("input[aria-label='Promotion code']").Input("BADCODE");
        await cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Apply promotion code", StringComparison.Ordinal))
            .ClickAsync(new()));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("We could not apply that promotion to this order."));
        await Assert.That(cut.Markup).DoesNotContain("BADCODE");
        await _announcer.Received(1).AnnounceAssertiveAsync("We could not apply that promotion to this order.");
        await _focus.Received(1).FocusAsync("#promotion-status", Arg.Any<bool>());
    }

    [Test]
    public async Task GuestRecovery_WithoutInMemoryCapability_FailsClosed()
    {
        var cut = _ctx.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, Guid.CreateVersion7())
            .Add(component => component.OrderId, Guid.CreateVersion7()));

        cut.WaitForElement("[role='alert']");
        await Assert.That(cut.Markup).Contains("same guest registration session");
        await _service.DidNotReceive().GetGuestAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<GuestRegistrationOrderCapability>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PaymentReturn_DoesNotInferSuccessFromCallbackNavigation()
    {
        var cut = _ctx.RenderMudComponent<PaymentRecoveryReturn>();

        cut.WaitForElement("[data-testid='payment-return-status']");
        await Assert.That(cut.Markup).Contains("does not confirm that payment succeeded");
        await Assert.That(cut.Markup).DoesNotContain("Payment confirmed");
        await Assert.That(cut.Markup).DoesNotContain("Registration confirmed");
        await _paymentService.DidNotReceiveWithAnyArgs().GetCurrentPaymentAsync(
            default, default, default!, default);
        await _paymentService.DidNotReceiveWithAnyArgs().GetGuestPaymentAsync(
            default, default, default!, default!, default);
    }

    [Test]
    public async Task PaymentReturn_ReturnToOriginalTabUsesBrowserInterop()
    {
        _browserActions.FocusOpenerAndCloseAsync(Arg.Any<CancellationToken>()).Returns(true);
        var cut = _ctx.RenderMudComponent<PaymentRecoveryReturn>();

        await cut.Find("[data-testid='return-to-registration-tab']")
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        await _browserActions.Received(1).FocusOpenerAndCloseAsync(Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).DoesNotContain("could not be focused");
    }

    [Test]
    public async Task PaymentReturn_MissingOpenerShowsSafeFallbackAndKeepsBrowseEvents()
    {
        _browserActions.FocusOpenerAndCloseAsync(Arg.Any<CancellationToken>()).Returns(false);
        var cut = _ctx.RenderMudComponent<PaymentRecoveryReturn>();

        await cut.Find("[data-testid='return-to-registration-tab']")
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForElement("[data-testid='return-tab-unavailable']");
        await Assert.That(cut.Markup).Contains("could not be focused");
        await Assert.That(cut.Markup).Contains("Browse events");
    }

    [Test]
    public async Task GuestRecovery_RendersActionsOnlyFromHalRelations()
    {
        var eventId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        _capabilityStore.TryGet(eventId, orderId, out Arg.Any<GuestRegistrationOrderCapability?>())
            .Returns(call =>
            {
                call[2] = capability;
                return true;
            });
        _service.GetGuestAsync(eventId, orderId, capability, Arg.Any<CancellationToken>()).Returns(
            new HalResourceOfGuestRegistrationOrderDto
            {
                Id = orderId,
                EventId = eventId,
                StatusCode = "READY_FOR_CHECKOUT",
                StatusName = "Ready for checkout",
                CurrencyCode = "EUR",
                _links = new Dictionary<string, HalLink>
                {
                    ["continue"] = new() { Href = "/continue", Method = "POST" }
                }
            });

        var cut = _ctx.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.OrderId, orderId));

        cut.WaitForAssertion(() => cut.Markup.Contains("Continue registration", StringComparison.Ordinal));
        await Assert.That(cut.Markup).DoesNotContain("Finalize registration");
        await Assert.That(cut.Markup).DoesNotContain("Cancel registration order");
    }

    [Test]
    public async Task AuthenticatedPayment_StartsOnlyFromExactOrderRelationAndRendersAuthoritativeFailureRetry()
    {
        var order = CreateOrder("AWAITING_PAYMENT", "Awaiting payment", "payment-acceptance", "start-payment");
        order.ExpiresAt = TestTime.UtcNow.AddMinutes(5);
        var failed = CreatePayment("Failed", "Failed", "payment-status", "retry-payment");
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);
        _paymentService.GetCurrentPaymentAcceptanceAsync(order.EventId.Value, order.Id.Value, order, Arg.Any<CancellationToken>())
            .Returns(Acceptance());
        _paymentService.StartCurrentPaymentAsync(order.EventId.Value, order.Id.Value, order, "revision", Arg.Any<CancellationToken>()).Returns(failed);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));
        cut.WaitForElement("[data-testid='payment-acceptance-acknowledgement']").Change(true);
        cut.WaitForElement("[data-testid='start-payment']").Click();

        cut.WaitForElement("[data-testid='retry-payment']");
        await Assert.That(cut.Markup).Contains("The payment failed");
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
        await _focus.Received(1).FocusAsync("#payment-actionable-status", Arg.Any<bool>());
    }

    [Test]
    public async Task AuthenticatedPayment_UnknownWithoutRetryRelationOffersNoBlindRetry()
    {
        var order = CreateOrder("AWAITING_PAYMENT", "Awaiting payment", "payment-status");
        order.ExpiresAt = TestTime.UtcNow.AddMinutes(5);
        var payment = CreatePayment("Unknown", "Unknown", "payment-status");
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);
        _paymentService.GetCurrentPaymentAsync(order.EventId.Value, order.Id.Value, order, Arg.Any<CancellationToken>()).Returns(payment);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForElement("[data-testid='payment-status']");
        await Assert.That(cut.Markup).Contains("outcome is not known yet");
        await Assert.That(cut.FindAll("[data-testid='retry-payment']")).IsEmpty();
    }

    [Test]
    public async Task AuthenticatedPayment_NoPaymentRelationsRendersNoPaymentAction()
    {
        var order = CreateOrder("AWAITING_PAYMENT", "Awaiting payment");
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Awaiting payment"));
        await Assert.That(cut.FindAll("[data-testid='start-payment']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='retry-payment']")).IsEmpty();
        await _paymentService.DidNotReceive().GetCurrentPaymentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<HalResourceOfRegistrationOrderDto>(), Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments("CONFIRMED", "Your registration is confirmed.")]
    [Arguments("DRAFT", "Choose your tickets to begin registration.")]
    [Arguments("AWAITING_IDENTITY", "Confirm your identity to continue registration.")]
    [Arguments("AWAITING_PARTICIPANT_DETAILS", "Add the required participant details to continue.")]
    [Arguments("AWAITING_REQUIREMENTS", "Complete the remaining registration requirements.")]
    [Arguments("READY_FOR_CHECKOUT", "Review your order and complete checkout.")]
    [Arguments("AWAITING_PAYMENT", "Payment is required")]
    [Arguments("AWAITING_APPROVAL", "waiting for organizer approval")]
    [Arguments("WAITLISTED", "waitlisted")]
    [Arguments("REJECTED", "not approved")]
    [Arguments("EXPIRED", "reservation expired")]
    [Arguments("CANCELLED", "was cancelled")]
    [Arguments("NEEDS_RECONCILIATION", "needs organizer review")]
    public async Task AuthenticatedRecovery_RendersStatusSpecificGuidance(string statusCode, string guidance)
    {
        var order = CreateOrder(statusCode, statusCode.Replace('_', ' '));
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => cut.Markup.Contains(guidance, StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains(guidance);
    }

    [Test]
    public async Task AuthenticatedRecovery_WithActiveHold_RendersCountdown()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout");
        order.ExpiresAt = TestTime.UtcNow.AddMinutes(5);
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));

        cut.WaitForAssertion(() => cut.Markup.Contains("Reservation expires in", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("Reservation expires in");
    }

    [Test]
    public async Task AuthenticatedRecovery_CountdownOverOneHour_RendersTotalRemainingMinutes()
    {
        var now = TestTime.UtcNow;
        await Assert.That(RegistrationOrderRecovery.Countdown(now.AddMinutes(61), now))
            .IsEqualTo("Reservation expires in 61:00.");
    }

    [Test]
    public async Task GuestRecovery_ContributionOptionsUseServerAmountsAndPreselectZero()
    {
        var eventId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        _capabilityStore.TryGet(eventId, orderId, out Arg.Any<GuestRegistrationOrderCapability?>())
            .Returns(call =>
            {
                call[2] = capability;
                return true;
            });
        _service.GetGuestAsync(eventId, orderId, capability, Arg.Any<CancellationToken>()).Returns(
            new HalResourceOfGuestRegistrationOrderDto
            {
                Id = orderId,
                EventId = eventId,
                StatusCode = "READY_FOR_CHECKOUT",
                StatusName = "Ready for checkout",
                CurrencyCode = "EUR",
                PlatformContribution = new PlatformContribution
                {
                    SelectedBasisPoints = 0,
                    Options =
                    [
                        new Options11 { ContributionBasisPoints = 0, AmountMinor = 0, IsDefault = true },
                        new Options11 { ContributionBasisPoints = 500, AmountMinor = 625 }
                    ]
                }
            });

        var cut = _ctx.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.OrderId, orderId));

        var select = cut.WaitForComponent<MudSelect<int?>>();
        await Assert.That(select.Instance.Value).IsEqualTo(0);
        var optionValues = cut.FindComponents<MudSelectItem<int?>>().Select(item => item.Instance.Value).ToArray();
        await Assert.That(optionValues).Contains(0);
        await Assert.That(optionValues).Contains(500);
    }

    [Test]
    public async Task GuestRecovery_AppliesPromotionThroughCapabilityBackedServiceOnlyWhenHalAllowsIt()
    {
        var eventId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        var order = new HalResourceOfGuestRegistrationOrderDto
        {
            Id = orderId,
            EventId = eventId,
            StatusCode = "READY_FOR_CHECKOUT",
            StatusName = "Ready for checkout",
            CurrencyCode = "EUR",
            _links = new Dictionary<string, HalLink> { ["apply-promotion"] = new() { Href = "/guest/promotion", Method = "POST" } }
        };
        _capabilityStore.TryGet(eventId, orderId, out Arg.Any<GuestRegistrationOrderCapability?>())
            .Returns(call =>
            {
                call[2] = capability;
                return true;
            });
        _service.GetGuestAsync(eventId, orderId, capability, Arg.Any<CancellationToken>()).Returns(order);
        _service.ApplyGuestPromotionAsync(eventId, orderId, capability, order, "GUEST10", Arg.Any<CancellationToken>()).Returns(order);

        var cut = _ctx.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.OrderId, orderId));

        cut.WaitForElement("input[aria-label='Promotion code']").Input("GUEST10");
        await cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Apply promotion code", StringComparison.Ordinal))
            .ClickAsync(new()));

        await _service.Received(1).ApplyGuestPromotionAsync(eventId, orderId, capability, order, "GUEST10", Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).DoesNotContain("opaque-capability");
    }

    [Test]
    public async Task AuthenticatedRecovery_SupersededPromotionMutation_CancelsAndIgnoresStaleCompletion()
    {
        var initialOrder = CreateOrder("READY_FOR_CHECKOUT", "Initial checkout", "apply-promotion");
        var currentOrder = CreateOrder("READY_FOR_CHECKOUT", "Current checkout", "apply-promotion");
        var staleOrder = CreateOrder("READY_FOR_CHECKOUT", "Stale checkout", "remove-promotion");
        var pending = new TaskCompletionSource<HalResourceOfRegistrationOrderDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedCancellation = default;
        _service.GetCurrentAsync(initialOrder.EventId!.Value, initialOrder.Id!.Value, Arg.Any<CancellationToken>()).Returns(initialOrder);
        _service.GetCurrentAsync(currentOrder.EventId!.Value, currentOrder.Id!.Value, Arg.Any<CancellationToken>()).Returns(currentOrder);
        _service.ApplyCurrentPromotionAsync(initialOrder.EventId.Value, initialOrder.Id.Value, initialOrder, "STALE10", Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedCancellation = call.ArgAt<CancellationToken>(4);
                return pending.Task;
            });

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, initialOrder.EventId.Value)
            .Add(component => component.OrderId, initialOrder.Id.Value));
        cut.WaitForElement("input[aria-label='Promotion code']").Input("STALE10");
        var mutation = cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Apply promotion code", StringComparison.Ordinal))
            .ClickAsync(new()));
        cut.WaitForAssertion(() =>
        {
            if (!capturedCancellation.CanBeCanceled)
            {
                throw new InvalidOperationException("Promotion mutation has not started.");
            }

            var currentSubmit = cut.FindAll("button")
                .Single(button => button.TextContent.Contains("Apply promotion code", StringComparison.Ordinal));
            if (!currentSubmit.HasAttribute("disabled"))
            {
                throw new InvalidOperationException("Promotion mutation has not reached its pending render.");
            }
        });

        cut.Render(parameters => parameters
            .Add(component => component.EventId, currentOrder.EventId.Value)
            .Add(component => component.OrderId, currentOrder.Id.Value));
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Current checkout", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Current route has not rendered.");
            }
        });
        pending.SetResult(staleOrder);
        await mutation;

        await Assert.That(capturedCancellation.IsCancellationRequested).IsTrue();
        await Assert.That(cut.Markup).DoesNotContain("Stale checkout");
        await _announcer.DidNotReceive().AnnouncePoliteAsync("Promotion applied.");
        await _focus.DidNotReceive().FocusAsync("#promotion-status", Arg.Any<bool>());
    }

    [Test]
    public async Task GuestRecovery_SupersededPromotionMutation_CancelsAndIgnoresStaleCompletion()
    {
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        var initialOrder = CreateGuestOrder("Initial guest checkout", "apply-promotion");
        var currentOrder = CreateGuestOrder("Current guest checkout", "apply-promotion");
        var staleOrder = CreateGuestOrder("Stale guest checkout", "remove-promotion");
        var pending = new TaskCompletionSource<HalResourceOfGuestRegistrationOrderDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedCancellation = default;
        _capabilityStore.TryGet(Arg.Any<Guid>(), Arg.Any<Guid>(), out Arg.Any<GuestRegistrationOrderCapability?>())
            .Returns(call =>
            {
                call[2] = capability;
                return true;
            });
        _service.GetGuestAsync(initialOrder.EventId!.Value, initialOrder.Id!.Value, capability, Arg.Any<CancellationToken>()).Returns(initialOrder);
        _service.GetGuestAsync(currentOrder.EventId!.Value, currentOrder.Id!.Value, capability, Arg.Any<CancellationToken>()).Returns(currentOrder);
        _service.ApplyGuestPromotionAsync(initialOrder.EventId.Value, initialOrder.Id.Value, capability, initialOrder, "STALE10", Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedCancellation = call.ArgAt<CancellationToken>(5);
                return pending.Task;
            });

        var cut = _ctx.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, initialOrder.EventId.Value)
            .Add(component => component.OrderId, initialOrder.Id.Value));
        cut.WaitForElement("input[aria-label='Promotion code']").Input("STALE10");
        var mutation = cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Apply promotion code", StringComparison.Ordinal))
            .ClickAsync(new()));
        cut.WaitForAssertion(() => Assert.That(capturedCancellation.CanBeCanceled).IsTrue());

        cut.Render(parameters => parameters
            .Add(component => component.EventId, currentOrder.EventId.Value)
            .Add(component => component.OrderId, currentOrder.Id.Value));
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Current guest checkout"));
        pending.SetResult(staleOrder);
        await mutation;

        await Assert.That(capturedCancellation.IsCancellationRequested).IsTrue();
        await Assert.That(cut.Markup).DoesNotContain("Stale guest checkout");
        await _announcer.DidNotReceive().AnnouncePoliteAsync("Promotion applied.");
        await _focus.DidNotReceive().FocusAsync("#guest-promotion-status", Arg.Any<bool>());
    }

    [Test]
    public async Task AuthenticatedRecovery_SupersededInitialLoad_IgnoresStaleCompletion()
    {
        var initialOrder = CreateOrder("READY_FOR_CHECKOUT", "Initial checkout", "apply-promotion");
        var currentOrder = CreateOrder("READY_FOR_CHECKOUT", "Current checkout", "apply-promotion");
        var pending = new TaskCompletionSource<HalResourceOfRegistrationOrderDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.GetCurrentAsync(initialOrder.EventId!.Value, initialOrder.Id!.Value, Arg.Any<CancellationToken>()).Returns(pending.Task);
        _service.GetCurrentAsync(currentOrder.EventId!.Value, currentOrder.Id!.Value, Arg.Any<CancellationToken>()).Returns(currentOrder);

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, initialOrder.EventId.Value)
            .Add(component => component.OrderId, initialOrder.Id.Value));
        cut.Render(parameters => parameters
            .Add(component => component.EventId, currentOrder.EventId.Value)
            .Add(component => component.OrderId, currentOrder.Id.Value));
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Current checkout"));

        pending.SetResult(initialOrder);
        await Task.Yield();

        await Assert.That(cut.Markup).Contains("Current checkout");
        await Assert.That(cut.Markup).DoesNotContain("Initial checkout");
    }

    [Test]
    public async Task GuestRecovery_SupersededInitialLoad_IgnoresStaleCompletion()
    {
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        var initialOrder = CreateGuestOrder("Initial guest checkout", "apply-promotion");
        var currentOrder = CreateGuestOrder("Current guest checkout", "apply-promotion");
        var pending = new TaskCompletionSource<HalResourceOfGuestRegistrationOrderDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _capabilityStore.TryGet(Arg.Any<Guid>(), Arg.Any<Guid>(), out Arg.Any<GuestRegistrationOrderCapability?>())
            .Returns(call =>
            {
                call[2] = capability;
                return true;
            });
        _service.GetGuestAsync(initialOrder.EventId!.Value, initialOrder.Id!.Value, capability, Arg.Any<CancellationToken>()).Returns(pending.Task);
        _service.GetGuestAsync(currentOrder.EventId!.Value, currentOrder.Id!.Value, capability, Arg.Any<CancellationToken>()).Returns(currentOrder);

        var cut = _ctx.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, initialOrder.EventId.Value)
            .Add(component => component.OrderId, initialOrder.Id.Value));
        cut.Render(parameters => parameters
            .Add(component => component.EventId, currentOrder.EventId.Value)
            .Add(component => component.OrderId, currentOrder.Id.Value));
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Current guest checkout"));

        pending.SetResult(initialOrder);
        await Task.Yield();

        await Assert.That(cut.Markup).Contains("Current guest checkout");
        await Assert.That(cut.Markup).DoesNotContain("Initial guest checkout");
    }

    [Test]
    public async Task GuestRecovery_MissingCapabilityRoute_InvalidatesPendingInitialLoad()
    {
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        var initialOrder = CreateGuestOrder("Initial guest checkout", "apply-promotion");
        var unavailableOrder = CreateGuestOrder("Unavailable guest checkout", "apply-promotion");
        var pending = new TaskCompletionSource<HalResourceOfGuestRegistrationOrderDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _capabilityStore.TryGet(Arg.Any<Guid>(), Arg.Any<Guid>(), out Arg.Any<GuestRegistrationOrderCapability?>())
            .Returns(call =>
            {
                var found = call.ArgAt<Guid>(1) == initialOrder.Id;
                call[2] = found ? capability : null;
                return found;
            });
        _service.GetGuestAsync(initialOrder.EventId!.Value, initialOrder.Id!.Value, capability, Arg.Any<CancellationToken>()).Returns(pending.Task);

        var cut = _ctx.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, initialOrder.EventId.Value)
            .Add(component => component.OrderId, initialOrder.Id.Value));
        cut.Render(parameters => parameters
            .Add(component => component.EventId, unavailableOrder.EventId!.Value)
            .Add(component => component.OrderId, unavailableOrder.Id!.Value));
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Open this order from the same guest registration session"));

        pending.SetResult(initialOrder);
        await Task.Yield();

        await Assert.That(cut.Markup).Contains("Open this order from the same guest registration session");
        await Assert.That(cut.Markup).DoesNotContain("Initial guest checkout");
    }

    [Test]
    public async Task GuestRecovery_MissingCapabilityRoute_ClearsPreviouslyLoadedOrder()
    {
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        var initialOrder = CreateGuestOrder("Initial guest checkout", "apply-promotion");
        var unavailableOrder = CreateGuestOrder("Unavailable guest checkout", "apply-promotion");
        _capabilityStore.TryGet(Arg.Any<Guid>(), Arg.Any<Guid>(), out Arg.Any<GuestRegistrationOrderCapability?>())
            .Returns(call =>
            {
                var found = call.ArgAt<Guid>(1) == initialOrder.Id;
                call[2] = found ? capability : null;
                return found;
            });
        _service.GetGuestAsync(initialOrder.EventId!.Value, initialOrder.Id!.Value, capability, Arg.Any<CancellationToken>()).Returns(initialOrder);

        var cut = _ctx.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, initialOrder.EventId.Value)
            .Add(component => component.OrderId, initialOrder.Id.Value));
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Initial guest checkout"));

        cut.Render(parameters => parameters
            .Add(component => component.EventId, unavailableOrder.EventId!.Value)
            .Add(component => component.OrderId, unavailableOrder.Id!.Value));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Open this order from the same guest registration session"));
        await Assert.That(cut.Markup).DoesNotContain("Initial guest checkout");
    }

    [Test]
    public async Task AuthenticatedRecovery_DisposeDuringPromotionMutation_CancelsAndIgnoresCompletion()
    {
        var order = CreateOrder("READY_FOR_CHECKOUT", "Checkout", "apply-promotion");
        var pending = new TaskCompletionSource<HalResourceOfRegistrationOrderDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedCancellation = default;
        _service.GetCurrentAsync(order.EventId!.Value, order.Id!.Value, Arg.Any<CancellationToken>()).Returns(order);
        _service.ApplyCurrentPromotionAsync(order.EventId.Value, order.Id.Value, order, "CANCEL10", Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedCancellation = call.ArgAt<CancellationToken>(4);
                return pending.Task;
            });

        var cut = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));
        cut.WaitForElement("input[aria-label='Promotion code']").Input("CANCEL10");
        var mutation = cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Apply promotion code", StringComparison.Ordinal))
            .ClickAsync(new()));
        cut.WaitForAssertion(() =>
        {
            if (!capturedCancellation.CanBeCanceled)
            {
                throw new InvalidOperationException("Promotion mutation has not started.");
            }

            var currentSubmit = cut.FindAll("button")
                .Single(button => button.TextContent.Contains("Apply promotion code", StringComparison.Ordinal));
            if (!currentSubmit.HasAttribute("disabled"))
            {
                throw new InvalidOperationException("Promotion mutation has not reached its pending render.");
            }
        });

        cut.Instance.Dispose();
        pending.SetResult(order);
        await mutation;

        await Assert.That(capturedCancellation.IsCancellationRequested).IsTrue();
        await _announcer.DidNotReceive().AnnouncePoliteAsync("Promotion applied.");
        await _focus.DidNotReceive().FocusAsync("#promotion-status", Arg.Any<bool>());
    }

    [Test]
    public async Task GuestRecovery_DisposeDuringPromotionMutation_CancelsAndIgnoresCompletion()
    {
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        var order = CreateGuestOrder("Guest checkout", "apply-promotion");
        var pending = new TaskCompletionSource<HalResourceOfGuestRegistrationOrderDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedCancellation = default;
        _capabilityStore.TryGet(order.EventId!.Value, order.Id!.Value, out Arg.Any<GuestRegistrationOrderCapability?>())
            .Returns(call =>
            {
                call[2] = capability;
                return true;
            });
        _service.GetGuestAsync(order.EventId.Value, order.Id.Value, capability, Arg.Any<CancellationToken>()).Returns(order);
        _service.ApplyGuestPromotionAsync(order.EventId.Value, order.Id.Value, capability, order, "CANCEL10", Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedCancellation = call.ArgAt<CancellationToken>(5);
                return pending.Task;
            });

        var cut = _ctx.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, order.EventId.Value)
            .Add(component => component.OrderId, order.Id.Value));
        cut.WaitForElement("input[aria-label='Promotion code']").Input("CANCEL10");
        var mutation = cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Apply promotion code", StringComparison.Ordinal))
            .ClickAsync(new()));
        cut.WaitForAssertion(() => Assert.That(capturedCancellation.CanBeCanceled).IsTrue());

        cut.Instance.Dispose();
        pending.SetResult(order);
        await mutation;

        await Assert.That(capturedCancellation.IsCancellationRequested).IsTrue();
        await _announcer.DidNotReceive().AnnouncePoliteAsync("Promotion applied.");
        await _focus.DidNotReceive().FocusAsync("#guest-promotion-status", Arg.Any<bool>());
    }

    [Test]
    public async Task ManualEvidence_CapturesSanitizedAuthenticatedAndGuestCheckoutStates()
    {
        var authenticatedOrder = CreateOrder("READY_FOR_CHECKOUT", "Ready for checkout", "apply-promotion", "finalize");
        authenticatedOrder.ExpiresAt = TestTime.UtcNow.AddMinutes(5);
        authenticatedOrder.PreDiscountOrganizerDirectedTotalMinor = 2_000;
        authenticatedOrder.PromotionDiscountTotalMinor = 500;
        authenticatedOrder.PostDiscountOrganizerDirectedTotalMinor = 1_500;
        authenticatedOrder.PlatformFeeTotalMinor = 150;
        authenticatedOrder.PlatformContributionTotalMinor = 75;
        authenticatedOrder.TotalDueMinor = 1_575;
        authenticatedOrder.PlatformContribution = new PlatformContribution2
        {
            Heading = "Support this event",
            Body = "Choose an optional contribution.",
            SelectedBasisPoints = 500,
            Options =
            [
                new Options12 { ContributionBasisPoints = 0, AmountMinor = 0, IsDefault = true },
                new Options12 { ContributionBasisPoints = 500, AmountMinor = 75 }
            ]
        };
        _service.GetCurrentAsync(authenticatedOrder.EventId!.Value, authenticatedOrder.Id!.Value, Arg.Any<CancellationToken>()).Returns(authenticatedOrder);
        _service.ApplyCurrentPromotionAsync(authenticatedOrder.EventId.Value, authenticatedOrder.Id.Value, authenticatedOrder, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(null));

        var authenticated = _ctx.RenderMudComponent<OrderRecovery>(parameters => parameters
            .Add(component => component.EventId, authenticatedOrder.EventId.Value)
            .Add(component => component.OrderId, authenticatedOrder.Id.Value));
        authenticated.WaitForElement("input[aria-label='Promotion code']").Input("NEVER-PERSIST");
        await authenticated.InvokeAsync(() => authenticated.FindAll("button")
            .Single(button => button.TextContent.Contains("Apply promotion code", StringComparison.Ordinal))
            .ClickAsync(new()));
        authenticated.WaitForElement("#promotion-status");
        await Assert.That(authenticated.Markup).DoesNotContain("This reservation has expired.");

        using var guestContext = new BlazorTestContext();
        var guestService = guestContext.AddMockService<IRegistrationOrderService>();
        guestContext.AddMockService<IRegistrationPaymentService>();
        var guestCapabilities = guestContext.AddMockService<IGuestRegistrationOrderCapabilityStore>();
        var guestCapability = new GuestRegistrationOrderCapability("opaque-capability");
        var guestOrder = new HalResourceOfGuestRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            StatusCode = "READY_FOR_CHECKOUT",
            StatusName = "Ready for checkout",
            CurrencyCode = "EUR",
            PreDiscountOrganizerDirectedTotalMinor = 2_000,
            PromotionDiscountTotalMinor = 500,
            PostDiscountOrganizerDirectedTotalMinor = 1_500,
            PlatformFeeTotalMinor = 150,
            PlatformContributionTotalMinor = 75,
            TotalDueMinor = 1_575,
            PlatformContribution = new PlatformContribution
            {
                Heading = "Guest support",
                Body = "Choose a guest contribution.",
                SelectedBasisPoints = 0,
                Options =
                [
                    new Options11 { ContributionBasisPoints = 0, AmountMinor = 0, IsDefault = true },
                    new Options11 { ContributionBasisPoints = 500, AmountMinor = 75 }
                ]
            },
            _links = new Dictionary<string, HalLink> { ["remove-promotion"] = new() { Href = "/guest/promotion", Method = "DELETE" } }
        };
        guestCapabilities.TryGet(guestOrder.EventId.Value, guestOrder.Id.Value, out Arg.Any<GuestRegistrationOrderCapability?>())
            .Returns(call =>
            {
                call[2] = guestCapability;
                return true;
            });
        guestService.GetGuestAsync(guestOrder.EventId.Value, guestOrder.Id.Value, guestCapability, Arg.Any<CancellationToken>()).Returns(guestOrder);

        var guest = guestContext.RenderMudComponent<GuestOrderRecovery>(parameters => parameters
            .Add(component => component.EventId, guestOrder.EventId.Value)
            .Add(component => component.OrderId, guestOrder.Id.Value));
        guest.WaitForAssertion(() => Assert.That(guest.Markup).Contains("Remove promotion"));

        var artifact = $"""
            <!doctype html>
            <html lang="en"><body>
            <h1>Phase 17 checkout bUnit manual QA</h1>
            <section data-viewport="narrow" style="inline-size:375px" aria-label="authenticated invalid-apply live transition">{authenticated.Markup}</section>
            <section data-viewport="tablet" dir="rtl" style="inline-size:768px" aria-label="guest remove-first state">{guest.Markup}</section>
            <section data-viewport="wide" style="inline-size:1280px" aria-label="authenticated finalization affordance">{authenticated.Markup}</section>
            </body></html>
            """;
        artifact = System.Text.RegularExpressions.Regex.Replace(
            artifact,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            "[redacted-guid]");

        await Assert.That(artifact).DoesNotContain("NEVER-PERSIST");
        await Assert.That(artifact).DoesNotContain(guestCapability.Value);
        await Assert.That(artifact).DoesNotContain(authenticatedOrder.Id.Value.ToString());
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(
            artifact,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")).IsFalse();
        await Assert.That(artifact).Contains("We could not apply that promotion to this order.");
        await Assert.That(artifact).Contains("Support this event");
        await Assert.That(artifact).Contains("Guest support");
        await Assert.That(artifact).Contains("dir=\"rtl\"");
        await Assert.That(artifact).DoesNotContain("This reservation has expired.");

        var evidenceDirectory = Environment.GetEnvironmentVariable("PHASE17_UI_EVIDENCE_DIR")
            ?? Path.Combine(Directory.GetCurrentDirectory(), ".omo", "evidence", "phase17-ui");
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "checkout.html"), artifact);
    }

    private static HalResourceOfRegistrationOrderDto CreateOrder(string statusCode, string statusName, params string[] relations)
    {
        var eventId = Guid.CreateVersion7();
        var resource = new HalResourceOfRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = eventId,
            StatusCode = statusCode,
            StatusName = statusName,
            CurrencyCode = "EUR",
            TotalDueMinor = 1250,
            ExpiresAt = TestTime.UtcNow.AddSeconds(-1),
            Lines = [new Lines2 { Quantity = 1, TicketTypeName = "General admission", SubtotalMinor = 1250, CurrencyCode = "EUR" }]
        };

        foreach (var relation in relations)
        {
            resource._links ??= new Dictionary<string, HalLink>();
            resource._links[relation] = new HalLink { Href = $"/api/orders/{resource.Id}/{relation}" };
        }

        return resource;
    }

    private static HalResourceOfGuestRegistrationOrderDto CreateGuestOrder(string statusName, params string[] relations)
    {
        var resource = new HalResourceOfGuestRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            StatusCode = "READY_FOR_CHECKOUT",
            StatusName = statusName,
            CurrencyCode = "EUR"
        };

        foreach (var relation in relations)
        {
            resource._links ??= new Dictionary<string, HalLink>();
            resource._links[relation] = new HalLink { Href = $"/api/orders/{resource.Id}/{relation}" };
        }

        return resource;
    }

    private static PaidOrderAcceptanceDisclosureDto Acceptance() => new()
    {
        DisclosureRevision = "revision",
        AcceptanceTemplateIdentifier = "paid-order-acceptance-v1",
        AcceptanceTemplateText = "I accept the identified merchant, directory, and platform roles.",
        OrganizerMerchant = new PaidOrderAcceptanceOrganizerMerchantDto
        {
            OrganizerActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000103"),
            MerchantDisclosureText = "Example Organizer, legal merchant",
            ProviderCode = "stripe",
            ProviderProfileCode = "OrganizerDirect",
            ProviderEnvironment = "test",
            ProviderCredentialOwner = "instance-operator",
            ChargeType = "direct-charge",
            StatementDescriptor = "EXAMPLE EVENT"
        },
        TenantDirectoryOperator = new PaidOrderAcceptanceTenantDirectoryOperatorDto
        {
            DocumentId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000104"),
            RevisionId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000105"),
            PublicName = "Community Directory",
            LegalName = "Community Directory Foundation",
            OperatorKindCode = "NONPROFIT",
            JurisdictionCountryCode = "BE",
            RegistrationIdentifier = "BE 0123.456.789",
            PublicContactEmail = "directory@example.test",
            LegalNoticeUrl = "https://directory.example.test/legal",
            TermsUrl = "https://directory.example.test/terms",
            PrivacyUrl = "https://directory.example.test/privacy"
        },
        InstanceOperator = new PaidOrderAcceptanceInstanceOperatorDto
        {
            OperatorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000106"),
            PublicName = "Independent Operator",
            LegalName = "Independent Operator ASBL",
            IsOfficialInstance = false,
            OfficialOrigin = "https://events.example.test",
            OperatorKindCode = "registered_organization",
            JurisdictionCountryCode = "BE",
            RegistrationIdentifier = "BE 0987.654.321",
            WebsiteUrl = "https://events.example.test",
            LegalNoticeUrl = "https://events.example.test/legal",
            TermsUrl = "https://events.example.test/terms",
            PrivacyUrl = "https://events.example.test/privacy"
        },
        PaymentOperations = new PaidOrderAcceptancePaymentOperationsDto
        {
            ComplaintContact = "complaints@example.test",
            ComplaintOwner = "Trust and Safety",
            RefundOwner = "Payments Operations",
            DisputeOwner = "Dispute Operations",
            ReconciliationOwner = "Payment Reconciliation",
            ActivationStatus = "approved"
        },
        DeliveryStartsAtUtc = DateTimeOffset.Parse("2026-09-10T17:00:00Z"),
        DeliveryEndsAtUtc = DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
        EventTimeZoneId = "Europe/Brussels",
        CurrencyCode = "EUR",
        CurrencyMinorUnitDigits = 2,
        OrganizerAmountMinor = 1_000,
        PlatformFeeMinor = 75,
        PlatformContributionMinor = 125,
        TotalMinor = 1_125,
        RefundPolicyVersion = 1,
        RefundPolicyText = "Refund policy",
        RefundPolicyLanguageTag = "en-GB",
        SupportContact = "support@example.test"
    };

    private static HalResourceOfRegistrationPaymentDto CreatePayment(
        string statusCode,
        string statusName,
        params string[] relations) => new()
        {
            Id = Guid.CreateVersion7(),
            RegistrationOrderId = Guid.CreateVersion7(),
            StatusCode = statusCode,
            StatusName = statusName,
            LastUpdatedAt = TestTime.UtcNow,
            _links = relations.ToDictionary(
            relation => relation,
            relation => new HalLink
            {
                Href = relation == "checkout-redirect" ? "bff/registration-payments/events/018e4e5c-7f00-7000-8000-000000000001/orders/018e4e5c-7f00-7000-8000-000000000002/checkout-ticket" : $"/api/payments/{relation}",
                Method = relation == "payment-status" ? "GET" : "POST"
            })
        };
}
