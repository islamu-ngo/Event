using AngleSharp.Dom;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Registration;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using NSubstitute;
using System.Globalization;
using System.Reflection;

namespace Explore.Blazor.Client.Tests.Components.Registration;

public sealed class PaymentStatusPanelTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly RecordingAnnouncer _announcer = new();
    private readonly RecordingFocus _focus = new();

    public PaymentStatusPanelTests()
    {
        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.RemoveAll<IAccessibilityFocusService>();
        _ctx.Services.AddSingleton<IAccessibilityAnnouncerService>(_announcer);
        _ctx.Services.AddSingleton<IAccessibilityFocusService>(_focus);
        _ctx.Services.RemoveAll<ITranslationService>();
        var translations = Substitute.For<ITranslationService>();
        translations.CurrentLanguage.Returns("en-GB");
        translations.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(1) ?? call.ArgAt<string>(0));
        translations.T("payment.acceptance.heading", Arg.Any<string?>()).Returns("Localized payment review");
        _ctx.Services.AddSingleton(translations);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments("Created", "waiting to continue")]
    [Arguments("Processing", "still processing")]
    [Arguments("RequiresAction", "Continue to secure checkout")]
    [Arguments("Unknown", "outcome is not known yet")]
    [Arguments("Failed", "payment failed")]
    [Arguments("Cancelled", "payment was cancelled")]
    [Arguments("Succeeded", "confirmed by the server")]
    [Arguments("NeedsReconciliation", "needs organizer review")]
    public async Task RendersCompleteGeneratedContractStateMatrix(string status, string copy)
    {
        var payment = CreatePayment(status, "payment-status");
        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment)));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains(copy));
        await Assert.That(cut.Find("#payment-actionable-status").GetAttribute("role")).IsNull();
        await Assert.That(cut.Find("#payment-actionable-status").GetAttribute("aria-live")).IsNull();
        await Assert.That(cut.FindAll("[data-testid='retry-payment']")).IsEmpty();
    }

    [Test]
    public async Task AcceptanceRendersExactServerFactsAndRequiresExplicitKeyboardOperableAcknowledgement()
    {
        string? acceptedRevision = null;
        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.LoadAcceptance, _ => Task.FromResult<PaidOrderAcceptanceDisclosureDto?>(Acceptance()))
            .Add(component => component.Start, (revision, _) =>
            {
                acceptedRevision = revision;
                return Task.FromResult<HalResourceOfRegistrationPaymentDto?>(CreatePayment("Created", "payment-status"));
            }));

        var checkbox = cut.WaitForElement("[data-testid='payment-acceptance-acknowledgement']");
        var start = cut.Find("[data-testid='start-payment']");
        await Assert.That(start.HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Markup).Contains("Localized payment review");
        await Assert.That(cut.Markup).DoesNotContain("Review before payment");
        await Assert.That(cut.Find("[data-testid='payment-acceptance-organizer-merchant']").TextContent)
            .Contains("Example Organizer, legal merchant");
        await Assert.That(cut.Find("[data-testid='payment-acceptance-directory-operator']").TextContent)
            .Contains("Community Directory Foundation");
        await Assert.That(cut.Find("[data-testid='payment-acceptance-instance-operator']").TextContent)
            .Contains("Independent Operator");
        await Assert.That(cut.Markup)
            .DoesNotContain("provides an event discovery and management directory only");
        await Assert.That(cut.Markup).Contains("Independent Operator");
        await Assert.That(cut.Markup).Contains("EUR 10.00");
        await Assert.That(cut.Markup).Contains("Europe/Brussels");
        await Assert.That(cut.Markup).Contains("Refund policy");
        await Assert.That(cut.Markup).Contains("complaints@example.test");
        await Assert.That(cut.Find("[data-testid='acceptance-official-origin']").TextContent)
            .Contains("https://events.example.test");
        await Assert.That(cut.Find("[data-testid='acceptance-activation-status']").TextContent)
            .Contains("approved");
        await Assert.That(cut.Find("[data-testid='acceptance-provider-profile']").TextContent)
            .Contains("OrganizerDirect");
        await Assert.That(cut.FindAll("[data-testid='acceptance-line']").Count).IsEqualTo(2);
        await Assert.That(cut.Markup).Contains("General admission");
        await Assert.That(cut.Markup).Contains("Family admission");
        await Assert.That(cut.Find("[lang='ar']")).IsNotNull();

        await checkbox.ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("[data-testid='start-payment']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        await Assert.That(acceptedRevision).IsEqualTo("revision");
    }

    [Test]
    public async Task AcceptanceUsesActiveCultureForMoneyAndScheduleOrdering()
    {
        using var context = new BlazorTestContext();
        context.Services.RemoveAll<ITranslationService>();
        var translations = Substitute.For<ITranslationService>();
        translations.CurrentLanguage.Returns("fr-FR");
        translations.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(1) ?? call.ArgAt<string>(0));
        context.Services.AddSingleton(translations);
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        string number = 10m.ToString("N2", culture);
        string money = culture.NumberFormat.CurrencyPositivePattern is 1 or 3
            ? $"{number} EUR"
            : $"EUR {number}";
        DateTimeOffset localStart = TimeZoneInfo.ConvertTime(
            DateTimeOffset.Parse("2026-09-10T17:00:00Z"),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels"));
        string scheduleStart = localStart.ToString("g", culture);

        var cut = context.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.LoadAcceptance, _ => Task.FromResult<PaidOrderAcceptanceDisclosureDto?>(Acceptance()))
            .Add(component => component.Start, (_, _) => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null)));

        cut.WaitForElement("[data-testid='payment-acceptance']");
        await Assert.That(cut.Markup).Contains(money);
        await Assert.That(cut.Markup).Contains(scheduleStart);
    }

    [Test]
    public async Task AcceptanceDirectionFollowsArabicUiCulture()
    {
        using var context = new BlazorTestContext();
        context.Services.RemoveAll<ITranslationService>();
        var translations = Substitute.For<ITranslationService>();
        translations.CurrentLanguage.Returns("ar");
        translations.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(1) ?? call.ArgAt<string>(0));
        context.Services.AddSingleton(translations);

        var cut = context.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.LoadAcceptance, _ => Task.FromResult<PaidOrderAcceptanceDisclosureDto?>(Acceptance()))
            .Add(component => component.Start, (_, _) => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(null)));

        IElement panel = cut.WaitForElement("[data-testid='payment-status']");
        IElement acceptance = cut.Find("[data-testid='payment-acceptance']");
        await Assert.That(panel.GetAttribute("dir")).IsEqualTo("rtl");
        await Assert.That(acceptance.GetAttribute("dir")).IsEqualTo("rtl");
    }

    [Test]
    public async Task CheckoutActionAcceptsOnlySameOriginBffRelation()
    {
        var unsafePayment = CreatePayment("RequiresAction", "checkout-redirect");
        unsafePayment._links!["checkout-redirect"].Href = "https://provider.example/checkout";
        var unsafeCut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(unsafePayment)));
        unsafeCut.WaitForElement("[data-testid='payment-status']");
        await Assert.That(unsafeCut.FindAll("[data-testid='checkout-redirect']")).IsEmpty();
        await Assert.That(unsafeCut.FindAll("[data-testid='prepare-checkout']")).IsEmpty();
        using var safeContext = new BlazorTestContext();
        var safePayment = CreatePayment("RequiresAction", "checkout-redirect");
        var safeCut = safeContext.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(safePayment))
            .Add(component => component.IssueCheckoutTicket, (_, _) => Task.FromResult<string?>(null)));
        safeCut.WaitForElement("[data-testid='prepare-checkout']");
        await Assert.That(safeCut.FindAll("[data-testid='prepare-checkout']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task CheckoutAction_PostsTicketIssuerThenNavigatesToOpaqueGetPath()
    {
        string? issuedFrom = null;
        var payment = CreatePayment("RequiresAction", "checkout-redirect");
        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment))
            .Add(component => component.IssueCheckoutTicket, (path, _) =>
            {
                issuedFrom = path;
                return Task.FromResult<string?>("/bff/registration-payments/checkout");
            }));
        cut.WaitForElement("[data-testid='prepare-checkout']");

        await cut.Find("[data-testid='prepare-checkout']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        await Assert.That(issuedFrom).EndsWith("/checkout-ticket");
        var link = cut.Find("[data-testid='checkout-redirect']");
        await Assert.That(link.GetAttribute("href")).IsEqualTo("/bff/registration-payments/checkout");
        await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(link.GetAttribute("rel")).IsEqualTo("opener");
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri).IsEqualTo("http://localhost/");
    }

    [Test]
    public async Task CheckoutAction_DisposedWhileIssuing_DoesNotForceNavigate()
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var payment = CreatePayment("RequiresAction", "checkout-redirect");
        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment))
            .Add(component => component.IssueCheckoutTicket, (_, _) => completion.Task));
        cut.WaitForElement("[data-testid='prepare-checkout']");
        Task click = cut.Find("[data-testid='prepare-checkout']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        await cut.Instance.DisposeAsync();
        cut.Dispose();
        completion.SetResult("/bff/registration-payments/checkout");
        await click;

        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri)
            .IsEqualTo("http://localhost/");
    }

    [Test]
    public async Task CheckoutAction_PreservesApplicationSubpath()
    {
        using var context = new BlazorTestContext();
        var navigation = new SubpathNavigationManager();
        context.Services.AddSingleton<NavigationManager>(navigation);
        string? issuePath = null;
        var payment = CreatePayment("RequiresAction", "checkout-redirect");
        var cut = context.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment))
            .Add(component => component.IssueCheckoutTicket, (path, _) =>
            {
                issuePath = path;
                return Task.FromResult<string?>("/events/bff/registration-payments/checkout");
            }));
        cut.WaitForElement("[data-testid='prepare-checkout']");

        await cut.Find("[data-testid='prepare-checkout']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForElement("[data-testid='checkout-redirect']");

        await Assert.That(issuePath).StartsWith("/events/bff/registration-payments/");
        await Assert.That(cut.Find("[data-testid='checkout-redirect']").GetAttribute("href"))
            .IsEqualTo("/events/bff/registration-payments/checkout");
    }

    [Test]
    public async Task InitialAuthoritativeSuccess_ReloadsOrderOnce()
    {
        int reloads = 0;
        var payment = CreatePayment("Succeeded", "payment-status");

        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment))
            .Add(component => component.Succeeded, EventCallback.Factory.Create(this, () => reloads++)));

        cut.WaitForAssertion(() => Assert.That(reloads).IsEqualTo(1));
        await Assert.That(cut.Markup).Contains("Payment is confirmed by the server");
    }

    [Test]
    public async Task PollTransitionToFailure_RendersAssertiveFocusableStatus()
    {
        var processing = CreatePayment("Processing", "payment-status");
        var failed = CreatePayment("Failed", "payment-status", "retry-payment");
        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(processing))
            .Add(component => component.Refresh, (_, _) => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(failed))
            .Add(component => component.Retry, (payment, _) => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment)));
        cut.WaitForAssertion(
            () =>
            {
                if (!cut.Markup.Contains("The payment failed", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Payment status has not transitioned to failure.");
                }
            },
            TimeSpan.FromSeconds(5));
        var status = cut.Find("#payment-actionable-status");
        await Assert.That(status.GetAttribute("role")).IsNull();
        await Assert.That(status.GetAttribute("aria-live")).IsNull();
        await Assert.That(status.GetAttribute("tabindex")).IsEqualTo("-1");
        await Assert.That(cut.FindAll("[data-testid='retry-payment']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task LoadNetworkFailure_RendersAccessibleBoundedErrorWithoutThrowing()
    {
        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromException<HalResourceOfRegistrationPaymentDto?>(new HttpRequestException("private transport detail"))));

        cut.WaitForElement("#payment-action-error");
        await Assert.That(cut.Markup).Contains("Payment status could not be loaded");
        await Assert.That(cut.Markup).DoesNotContain("private transport detail");
        await Assert.That(cut.Find("#payment-action-error").GetAttribute("tabindex")).IsEqualTo("-1");
    }

    [Test]
    public async Task CheckoutNetworkFailure_StaysOnPageAndShowsBoundedError()
    {
        var payment = CreatePayment("RequiresAction", "checkout-redirect");
        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment))
            .Add(component => component.IssueCheckoutTicket, (_, _) => Task.FromException<string?>(new JSException("private browser detail"))));
        cut.WaitForElement("[data-testid='prepare-checkout']");

        await cut.Find("[data-testid='prepare-checkout']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForElement("#payment-action-error");
        await Assert.That(cut.Markup).Contains("Secure checkout could not be opened");
        await Assert.That(cut.Markup).DoesNotContain("private browser detail");
        await Assert.That(_ctx.Services.GetRequiredService<NavigationManager>().Uri).IsEqualTo("http://localhost/");
        await Assert.That(_announcer.AssertiveMessages).IsEmpty();
        await Assert.That(_focus.Selectors).IsEquivalentTo(["#payment-action-error"]);
    }

    [Test]
    public async Task RetryableFailedTransition_UsesFocusAsItsOnlyAnnouncementChannel()
    {
        var failed = CreatePayment("Failed", "payment-status", "retry-payment");
        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(failed))
            .Add(component => component.Retry, (payment, _) => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment)));

        cut.WaitForElement("#payment-actionable-status");
        await Assert.That(_announcer.AssertiveMessages).IsEmpty();
        await Assert.That(_announcer.PoliteMessages).IsEmpty();
        await Assert.That(_focus.Selectors).IsEquivalentTo(["#payment-actionable-status"]);
        await Assert.That(cut.Find("#payment-actionable-status").GetAttribute("role")).IsNull();
        await Assert.That(cut.Find("#payment-actionable-status").GetAttribute("aria-live")).IsNull();
    }

    [Test]
    public async Task NeedsReconciliation_UsesGlobalAssertiveChannelWithoutMovingFocus()
    {
        var payment = CreatePayment("NeedsReconciliation", "payment-status");

        _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment)));

        await Assert.That(_announcer.AssertiveMessages.Count).IsEqualTo(1);
        await Assert.That(_announcer.PoliteMessages).IsEmpty();
        await Assert.That(_focus.Selectors).IsEmpty();
    }

    [Test]
    public async Task RefundAndDisputeTruthRendersWhileRequestAffordanceRequiresExactHalRelation()
    {
        var payment = CreatePayment("Succeeded", "payment-status", "request-refund");
        payment.CurrencyCode = "EUR";
        payment.CurrencyMinorUnitDigits = 2;
        payment.Refunds =
        [
            new RegistrationRefundDto { StatusCode = "Pending", StatusName = "Pending", AmountMinor = 100, CurrencyCode = "EUR", AcceptedRefundPolicyVersion = 7 },
            new RegistrationRefundDto { StatusCode = "Succeeded", StatusName = "Refunded", AmountMinor = 200, CurrencyCode = "EUR", AcceptedRefundPolicyVersion = 7 }
        ];
        payment.Disputes =
        [
            new RegistrationPaymentDisputeDto { StageCode = "Formal", StatusCode = "Open", AmountMinor = 300, CurrencyCode = "EUR" }
        ];

        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment))
            .Add(component => component.RequestRefund, (value, _) => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(value)));

        cut.WaitForElement("[data-testid='payment-refunds']");
        await Assert.That(cut.FindAll("[data-testid='payment-refund']").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("[data-testid='payment-dispute']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='request-refund']").Count).IsEqualTo(1);

        payment._links!.Remove("request-refund");
        cut.Render();
        await Assert.That(cut.FindAll("[data-testid='request-refund']")).IsEmpty();
    }

    [Test]
    public async Task MaterialChangeChoiceRendersButActionsRequireExactHalRelation()
    {
        var payment = CreatePayment("Succeeded", "payment-status", "respond-material-change");
        payment.MaterialChangeChoices =
        [
            new RegistrationMaterialChangeChoiceDto
            {
                Id = Guid.CreateVersion7(),
                CampaignId = Guid.CreateVersion7(),
                StatusCode = "Pending",
                CreatedAt = TestTime.UtcNow
            }
        ];

        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment))
            .Add(component => component.RespondMaterialChange,
                (value, _, _, _) => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(value)));

        cut.WaitForElement("[data-testid='payment-material-changes']");
        await Assert.That(cut.FindAll("[data-testid='accept-material-change']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='refund-material-change']").Count).IsEqualTo(1);

        payment._links!.Remove("respond-material-change");
        cut.Render();
        await Assert.That(cut.FindAll("[data-testid='accept-material-change']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='refund-material-change']")).IsEmpty();
    }

    [Test]
    public async Task Polling_UsesProgressiveCappedBackoffAndOneTrackedNonOverlappingTask()
    {
        var now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");
        var clock = new MutableTimeProvider(now);
        var delays = new List<TimeSpan>();
        int refreshCount = 0;
        int activeRefreshes = 0;
        int maximumActiveRefreshes = 0;
        var processing = CreatePayment("Processing", "payment-status");
        processing.ExpiresAt = now.AddMinutes(5);

        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(processing))
            .Add(component => component.Refresh, async (_, _) =>
            {
                activeRefreshes++;
                maximumActiveRefreshes = Math.Max(maximumActiveRefreshes, activeRefreshes);
                await Task.Yield();
                activeRefreshes--;
                refreshCount++;
                return refreshCount == 4 ? CreatePayment("Failed", "payment-status") : processing;
            })
            .Add(component => component.Clock, clock)
            .Add(component => component.PollDelay, (delay, _) =>
            {
                delays.Add(delay);
                clock.Advance(delay);
                return Task.CompletedTask;
            }));

        cut.WaitForAssertion(() =>
        {
            if (refreshCount != 4)
            {
                throw new InvalidOperationException("Polling sequence has not completed.");
            }
        });
        var pollingTask = (Task?)typeof(PaymentStatusPanel)
            .GetField("_pollingTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cut.Instance);
        cut.Render();
        var rerenderedTask = (Task?)typeof(PaymentStatusPanel)
            .GetField("_pollingTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cut.Instance);

        await Assert.That(delays).IsEquivalentTo([
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15)]);
        await Assert.That(maximumActiveRefreshes).IsEqualTo(1);
        await Assert.That(pollingTask).IsSameReferenceAs(rerenderedTask);
    }

    [Test]
    public async Task Polling_StopsAtEarliestAuthoritativeCutoffReloadsOnceAndOffersNoRetry()
    {
        var now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");
        var clock = new MutableTimeProvider(now);
        var delays = new List<TimeSpan>();
        int refreshCount = 0;
        int expiryReloads = 0;
        var processing = CreatePayment("Processing", "payment-status", "retry-payment");
        processing.ExpiresAt = now.AddSeconds(30);

        var cut = _ctx.RenderMudComponent<PaymentStatusPanel>(parameters => parameters
            .Add(component => component.Load, _ => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(processing))
            .Add(component => component.Refresh, (_, _) =>
            {
                refreshCount++;
                return Task.FromResult<HalResourceOfRegistrationPaymentDto?>(processing);
            })
            .Add(component => component.Retry, (payment, _) => Task.FromResult<HalResourceOfRegistrationPaymentDto?>(payment))
            .Add(component => component.OrderExpiresAt, now.AddSeconds(8))
            .Add(component => component.Expired, EventCallback.Factory.Create(this, () => expiryReloads++))
            .Add(component => component.Clock, clock)
            .Add(component => component.PollDelay, (delay, _) =>
            {
                delays.Add(delay);
                clock.Advance(delay);
                return Task.CompletedTask;
            }));

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Payment window expired", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expiry has not rendered.");
            }
        });

        await Assert.That(delays).IsEquivalentTo([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)]);
        await Assert.That(refreshCount).IsEqualTo(1);
        await Assert.That(expiryReloads).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='retry-payment']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='checkout-redirect']")).IsEmpty();
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
        RefundPolicyLanguageTag = "ar",
        SupportContact = "support@example.test",
        Lines =
        [
            new PaidOrderAcceptanceLineDto
            {
                OrderLineId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101"),
                Name = "General admission",
                Quantity = 1,
                UnitAmountMinor = 600,
                DiscountAmountMinor = 0,
                LineTotalMinor = 600
            },
            new PaidOrderAcceptanceLineDto
            {
                OrderLineId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000102"),
                Name = "Family admission",
                Quantity = 2,
                UnitAmountMinor = 250,
                DiscountAmountMinor = 100,
                LineTotalMinor = 400
            }
        ]
    };

    private static HalResourceOfRegistrationPaymentDto CreatePayment(string status, params string[] relations) => new()
    {
        StatusCode = status,
        StatusName = status.Replace('_', ' '),
        LastUpdatedAt = TestTime.UtcNow,
        _links = relations.ToDictionary(
            relation => relation,
            relation => new HalLink
            {
                Href = relation == "checkout-redirect"
                    ? "bff/registration-payments/events/018e4e5c-7f00-7000-8000-000000000001/orders/018e4e5c-7f00-7000-8000-000000000002/checkout-ticket"
                    : "/api/payment",
                Method = relation == "payment-status" ? "GET" : "POST"
            })
    };

    private sealed class RecordingAnnouncer : IAccessibilityAnnouncerService
    {
        public List<string> PoliteMessages { get; } = [];
        public List<string> AssertiveMessages { get; } = [];
        public Task AnnouncePoliteAsync(string message) { PoliteMessages.Add(message); return Task.CompletedTask; }
        public Task AnnounceAssertiveAsync(string message) { AssertiveMessages.Add(message); return Task.CompletedTask; }
    }

    private sealed class SubpathNavigationManager : NavigationManager
    {
        public SubpathNavigationManager() => Initialize("https://event.example/events/", "https://event.example/events/");

        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }

    private sealed class RecordingFocus : IAccessibilityFocusService
    {
        public List<string> Selectors { get; } = [];
        public Task FocusAsync(string cssSelector, bool preventScroll = false) { Selectors.Add(cssSelector); return Task.CompletedTask; }
        public Task FocusByIdAsync(string elementId, bool preventScroll = false) => Task.CompletedTask;
        public Task FocusMainContentAsync() => Task.CompletedTask;
        public Task FocusOnNavigateAsync() => Task.CompletedTask;
        public Task SaveFocusAsync() => Task.CompletedTask;
        public Task RestoreFocusAsync(string? fallbackSelector = null) => Task.CompletedTask;
        public Task<string> GetPreferredMotionAsync() => Task.FromResult("reduce");
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
