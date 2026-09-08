using Explore.Application.Contracts.Payments;

namespace Event.Application.UnitTests.Contracts.Payments;

public sealed class HostedCheckoutContractsTests
{
    private static readonly DateTime Cutoff = new(2026, 8, 20, 12, 30, 0, DateTimeKind.Utc);
    private static readonly Guid AttemptId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    private static readonly Guid OrderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000102");

    [Test]
    public async Task CreateRequest_AcceptsSameOriginHttpsUrlsAndImmutableComposition()
    {
        HostedCheckoutCreateRequest request = HostedCheckoutCreateRequest.Create(
            AttemptId,
            OrderId,
            "stripe",
            "acct_123",
            "checkout:stable",
            "EUR",
            12_50,
            2_50,
            Cutoff,
            new Uri("https://events.example.test"),
            new Uri("https://events.example.test/checkout/success"),
            new Uri("https://events.example.test/checkout/cancel"));

        await Assert.That(request.TotalMinor).IsEqualTo(12_50);
        await Assert.That(request.ApplicationFeeMinor).IsEqualTo(2_50);
        await Assert.That(request.CurrencyCode).IsEqualTo("EUR");
    }

    [Test]
    [Arguments("http://events.example.test", "https://events.example.test/checkout/success", "https://events.example.test/checkout/cancel")]
    [Arguments("https://events.example.test", "https://evil.example.test/checkout/success", "https://events.example.test/checkout/cancel")]
    [Arguments("https://events.example.test", "https://events.example.test/checkout/success", "https://events.example.test:444/checkout/cancel")]
    public async Task CreateRequest_RejectsInsecureOrCrossOriginUrls(string origin, string success, string cancel)
    {
        await Assert.That(() => HostedCheckoutCreateRequest.Create(
                AttemptId,
                OrderId,
                "stripe",
                "acct_123",
                "checkout:stable",
                "EUR",
                12_50,
                2_50,
                Cutoff,
                new Uri(origin),
                new Uri(success),
                new Uri(cancel)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateRequest_RejectsMalformedIdentityAndMoney()
    {
        await Assert.That(() => HostedCheckoutCreateRequest.Create(
                Guid.Empty,
                OrderId,
                "stripe",
                "acct_123",
                "checkout:stable",
                "EUR",
                12_50,
                2_50,
                Cutoff,
                new Uri("https://events.example.test"),
                new Uri("https://events.example.test/checkout/success"),
                new Uri("https://events.example.test/checkout/cancel")))
            .Throws<ArgumentException>();

        await Assert.That(() => HostedCheckoutCreateRequest.Create(
                AttemptId,
                OrderId,
                "stripe",
                "acct_123",
                "checkout:stable",
                "EUR",
                100,
                101,
                Cutoff,
                new Uri("https://events.example.test"),
                new Uri("https://events.example.test/checkout/success"),
                new Uri("https://events.example.test/checkout/cancel")))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RetrieveRequest_RejectsMalformedProviderIdentifiers()
    {
        await Assert.That(() => HostedCheckoutRetrieveRequest.Create("stripe", "acct_123", "\u0001"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ReturnUrls_RejectFragmentsEvenOnAllowedOrigin()
    {
        await Assert.That(() => HostedCheckoutReturnUrls.Create(
                new Uri("https://events.example.test"),
                new Uri("https://events.example.test/checkout/success#paid"),
                new Uri("https://events.example.test/checkout/cancel")))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PaymentIntentRetrieveRequest_RejectsMalformedIdentity()
    {
        await Assert.That(() => PaymentIntentRetrieveRequest.Create("stripe", "acct_123", "\u0001"))
            .Throws<ArgumentException>();
    }
}
