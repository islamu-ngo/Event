using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class RegistrationPaymentServiceTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IAuthenticatedRegistrationOrderPaymentClient _authPaymentClient;
    private readonly IGuestRegistrationOrderPaymentClient _guestPaymentClient;
    private readonly IStudioRegistrationOrderPaymentClient _studioPaymentClient;
    private readonly IRefundCampaignClient _refundCampaignClient;
    private readonly IBffClient _bffClient;
    private readonly RegistrationPaymentService _service;

    public RegistrationPaymentServiceTests()
    {
        _authPaymentClient = Substitute.For<IAuthenticatedRegistrationOrderPaymentClient>();
        _guestPaymentClient = Substitute.For<IGuestRegistrationOrderPaymentClient>();
        _studioPaymentClient = Substitute.For<IStudioRegistrationOrderPaymentClient>();
        _refundCampaignClient = Substitute.For<IRefundCampaignClient>();
        _bffClient = Substitute.For<IBffClient>();
        var logger = Substitute.For<ILogger<RegistrationPaymentService>>();

        _service = new RegistrationPaymentService(
            _authPaymentClient,
            _guestPaymentClient,
            _studioPaymentClient,
            _refundCampaignClient,
            _bffClient,
            logger);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task IssueGuestPaymentCheckoutTicketAsync_ForwardsCapabilityOnlyToBffHeaderBoundary()
    {
        var capability = new GuestRegistrationOrderCapability("guest-capability");
        const string issuePath = "/bff/registration-payments/events/event/orders/order/checkout-ticket";
        _bffClient.IssueRegistrationPaymentCheckoutTicketAsync(issuePath, capability.Value, Arg.Any<CancellationToken>())
            .Returns(new BffRegistrationPaymentCheckoutTicketResponseDto("/bff/registration-payments/checkout/opaque"));

        string? checkoutPath = await _service.IssueGuestPaymentCheckoutTicketAsync(issuePath, capability);

        await Assert.That(checkoutPath).IsEqualTo("/bff/registration-payments/checkout/opaque");
        await _bffClient.Received(1).IssueRegistrationPaymentCheckoutTicketAsync(
            issuePath, capability.Value, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StartCurrentPaymentAsync_WithoutStartPaymentRelation_DoesNotCallGeneratedClient()
    {
        var order = CreateOrder();

        var result = await _service.StartCurrentPaymentAsync(order.EventId!.Value, order.Id!.Value, order, "revision");

        await Assert.That(result).IsNull();
        await _authPaymentClient.DidNotReceive().StartAuthenticatedRegistrationPaymentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<PaidOrderAcceptanceAcknowledgementDto?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StartCurrentPaymentAsync_WithExactRelation_UsesFreshUuid7()
    {
        var order = CreateOrder("start-payment");
        var payment = CreatePayment("payment-status", "checkout-redirect");
        _authPaymentClient.StartAuthenticatedRegistrationPaymentAsync(
                order.EventId!.Value, order.Id!.Value, Arg.Is<string>(value => IsUuid7(value)), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Is<PaidOrderAcceptanceAcknowledgementDto>(value => value.Acknowledged == true && value.DisclosureRevision == "revision"),
                Arg.Any<CancellationToken>())
            .Returns(payment);

        var result = await _service.StartCurrentPaymentAsync(order.EventId.Value, order.Id.Value, order, "revision");

        await Assert.That(result).IsSameReferenceAs(payment);
    }

    [Test]
    public async Task RetryGuestPaymentAsync_RequiresExactRelationAndPreservesCapability()
    {
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        var payment = CreatePayment("retry-payment");
        var retried = CreatePayment("payment-status");
        _guestPaymentClient.RetryGuestRegistrationPaymentAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Is<string>(value => IsUuid7(value)), capability.Value,
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(retried);

        var result = await _service.RetryGuestPaymentAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), capability, payment);

        await Assert.That(result).IsSameReferenceAs(retried);
        await Assert.That(retried._links?.ContainsKey("retry-payment") == true).IsFalse();
    }

    [Test]
    public async Task GetStudioPaymentAsync_WithoutStudioRelation_DoesNotCallGeneratedClient()
    {
        var order = CreateOrder();

        var result = await _service.GetStudioPaymentAsync(order.EventId!.Value, order.Id!.Value, order);

        await Assert.That(result).IsNull();
        await _studioPaymentClient.DidNotReceive().GetStudioRegistrationPaymentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static HalResourceOfRegistrationOrderDto CreateOrder(params string[] relations)
    {
        var order = new HalResourceOfRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7()
        };

        if (relations.Length == 0)
        {
            return order;
        }

        order._links = relations.ToDictionary(
            relation => relation,
            relation => new HalLink { Href = $"/api/{relation}", Method = "POST" });
        return order;
    }

    private static HalResourceOfRegistrationPaymentDto CreatePayment(params string[] relations) => new()
    {
        Id = Guid.CreateVersion7(),
        StatusCode = "PENDING",
        _links = relations.ToDictionary(
            relation => relation,
            relation => new HalLink { Href = $"/api/{relation}", Method = "POST" })
    };

    private static bool IsUuid7(string candidate) =>
        Guid.TryParse(candidate, out var parsed) && parsed.Version == 7;
}
