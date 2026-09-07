using Explore.Blazor.Extensions;
using Explore.Blazor.Client.Services.Http;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class RegistrationPaymentCheckoutTicketStatelessTests
{
    private const string CheckoutHost = "checkout-bff.example.test";

    [Test]
    public async Task TicketStore_ProtectsAndExtractsTargetWithTheStatelessApi()
    {
        DirectoryInfo keyDirectory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"{nameof(RegistrationPaymentCheckoutTicketStatelessTests)}-{Guid.NewGuid():N}"));
        try
        {
            var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
            var requestContext = new DefaultHttpContext();
            requestContext.Request.Scheme = Uri.UriSchemeHttps;
            requestContext.Request.Host = new HostString(CheckoutHost);
            requestContext.Request.PathBase = "/t/acme";
            string checkoutSession = Guid.NewGuid().ToString("N");
            var issuer = new RegistrationPaymentCheckoutTicketStore(CreateProvider(keyDirectory), timeProvider);
            RegistrationPaymentCheckoutTicketIssue? issue = issuer.PrepareIssue(
                new Uri("https://checkout.stripe.com/c/pay/cs_store_api"),
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000101"),
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000201"),
                requestContext.Request,
                "acme",
                checkoutSession);
            var navigator = new RegistrationPaymentCheckoutTicketStore(CreateProvider(keyDirectory), timeProvider);
            Uri? target = navigator.ValidateAndExtractTarget(
                issue!.ProtectedCookie,
                requestContext.Request,
                "acme",
                checkoutSession);

            await Assert.That(issue.ProtectedCookie).IsNotEmpty();
            await Assert.That(issue.ExpiresAt).IsEqualTo(timeProvider.GetUtcNow().AddMinutes(5));
            await Assert.That(target?.AbsoluteUri).IsEqualTo("https://checkout.stripe.com/c/pay/cs_store_api");
        }
        finally
        {
            keyDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task SharedKeyRing_AllowsIssuanceOnOneSplitHostAndNavigationOnAnother()
    {
        DirectoryInfo keyDirectory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"{nameof(RegistrationPaymentCheckoutTicketStatelessTests)}-{Guid.NewGuid():N}"));
        try
        {
            IDataProtectionProvider issuerProvider = CreateProvider(keyDirectory);
            ITestRegistrationPaymentClient issuerApiClient = BffRegistrationPaymentEndpointTests.CheckoutTargetClient(
                "https://checkout.stripe.com/c/pay/cs_cross_host");
            await using WebApplicationFactory<Program> issuerFactory = BffRegistrationPaymentEndpointTests.CreateFactory(
                issuerApiClient,
                dataProtectionProvider: issuerProvider);
            using HttpClient issuerClient = issuerFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false
            });
            BffRegistrationPaymentEndpointTests.BrowserSession session =
                await BffRegistrationPaymentEndpointTests.IssueBrowserSessionAsync(issuerClient, host: CheckoutHost);
            using HttpResponseMessage issued = await BffRegistrationPaymentEndpointTests.IssueCheckoutAsync(
                issuerClient,
                BffRegistrationPaymentEndpointTests.CheckoutIssuePath,
                session,
                host: CheckoutHost);
            BffRegistrationPaymentCheckoutTicketResponseDto? ticket =
                await issued.Content.ReadFromJsonAsync<BffRegistrationPaymentCheckoutTicketResponseDto>();
            string cookie = BffRegistrationPaymentEndpointTests.GetCheckoutCookie(issued);

            IDataProtectionProvider navigatorProvider = CreateProvider(keyDirectory);
            ITestRegistrationPaymentClient navigatorApiClient = BffRegistrationPaymentEndpointTests.CheckoutTargetClient(
                "https://checkout.stripe.com/c/pay/cs_cross_host");
            await using WebApplicationFactory<Program> navigatorFactory = BffRegistrationPaymentEndpointTests.CreateFactory(
                navigatorApiClient,
                dataProtectionProvider: navigatorProvider);
            using HttpClient navigatorClient = navigatorFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false
            });
            using var navigationRequest = new HttpRequestMessage(HttpMethod.Get, ticket!.CheckoutPath);
            BffRegistrationPaymentEndpointTests.AddBrowserSession(navigationRequest, session, cookie);
            navigationRequest.Headers.Host = CheckoutHost;
            using HttpResponseMessage navigation = await navigatorClient.SendAsync(navigationRequest);

            await Assert.That(issued.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(navigation.StatusCode).IsEqualTo(HttpStatusCode.SeeOther);
            await Assert.That(navigation.Headers.Location?.AbsoluteUri)
                .IsEqualTo("https://checkout.stripe.com/c/pay/cs_cross_host");
        }
        finally
        {
            keyDirectory.Delete(recursive: true);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MismatchedProtectionContext_FailsClosedAcrossSplitHosts(bool differentApplication)
    {
        DirectoryInfo issuerKeys = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"{nameof(RegistrationPaymentCheckoutTicketStatelessTests)}-issuer-{Guid.NewGuid():N}"));
        DirectoryInfo navigatorKeys = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"{nameof(RegistrationPaymentCheckoutTicketStatelessTests)}-navigator-{Guid.NewGuid():N}"));
        try
        {
            ITestRegistrationPaymentClient issuerApiClient = BffRegistrationPaymentEndpointTests.CheckoutTargetClient(
                "https://checkout.stripe.com/c/pay/cs_key_isolation");
            await using WebApplicationFactory<Program> issuerFactory = BffRegistrationPaymentEndpointTests.CreateFactory(
                issuerApiClient,
                dataProtectionProvider: CreateProvider(issuerKeys));
            using HttpClient issuerClient = issuerFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false
            });
            BffRegistrationPaymentEndpointTests.BrowserSession session =
                await BffRegistrationPaymentEndpointTests.IssueBrowserSessionAsync(issuerClient, host: CheckoutHost);
            using HttpResponseMessage issued = await BffRegistrationPaymentEndpointTests.IssueCheckoutAsync(
                issuerClient,
                BffRegistrationPaymentEndpointTests.CheckoutIssuePath,
                session,
                host: CheckoutHost);
            BffRegistrationPaymentCheckoutTicketResponseDto? ticket =
                await issued.Content.ReadFromJsonAsync<BffRegistrationPaymentCheckoutTicketResponseDto>();
            string cookie = BffRegistrationPaymentEndpointTests.GetCheckoutCookie(issued);

            ITestRegistrationPaymentClient navigatorApiClient = BffRegistrationPaymentEndpointTests.CheckoutTargetClient(
                "https://checkout.stripe.com/c/pay/cs_key_isolation");
            await using WebApplicationFactory<Program> navigatorFactory = BffRegistrationPaymentEndpointTests.CreateFactory(
                navigatorApiClient,
                dataProtectionProvider: differentApplication
                    ? DataProtectionProvider.Create(issuerKeys, builder => builder.SetApplicationName("different-checkout-application"))
                    : CreateProvider(navigatorKeys));
            using HttpClient navigatorClient = navigatorFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false
            });
            using var navigationRequest = new HttpRequestMessage(HttpMethod.Get, ticket!.CheckoutPath);
            BffRegistrationPaymentEndpointTests.AddBrowserSession(navigationRequest, session, cookie);
            navigationRequest.Headers.Host = CheckoutHost;
            using HttpResponseMessage navigation = await navigatorClient.SendAsync(navigationRequest);

            await Assert.That(issued.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(navigation.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        finally
        {
            issuerKeys.Delete(recursive: true);
            navigatorKeys.Delete(recursive: true);
        }
    }

    private static IDataProtectionProvider CreateProvider(DirectoryInfo keyDirectory) =>
        DataProtectionProvider.Create(keyDirectory, builder =>
            builder.SetApplicationName(BffDataProtectionExtensions.ApplicationName));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
