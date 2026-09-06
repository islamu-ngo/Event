// ABOUTME: Real HTTP security coverage for guest payment capability, idempotency, privacy, and target access.
// ABOUTME: Proves replay scope cannot cross order, event, capability, expiry, or request fingerprints.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Event.Api.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class RegistrationPaymentHttpSecurityTests
{
    private static readonly Guid TenantId = PlatformDefaults.DefaultTenantId;
    private static readonly DateTime UtcNow = DateTime.UtcNow;

    [Test]
    public async Task GuestPaymentHttpContractScopesReplayAndRechecksCapabilityExpiryAndTargetAccess()
    {
        IHostedCheckoutSessionRetriever retriever = Substitute.For<IHostedCheckoutSessionRetriever>();
        IRegistrationPaymentAttemptRepository attempts = Substitute.For<IRegistrationPaymentAttemptRepository>();
        var freshness = Substitute.For<IPaidOrderAcceptanceFreshnessService>();
        freshness.IsCurrentAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>()).Returns(true);
        var timeProvider = new MutableTimeProvider(UtcNow);
        await using WebApplicationFactory<Program> factory = new AuthenticatedWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedCheckoutSessionRetriever>();
                services.AddSingleton(retriever);
                services.RemoveAll<IRegistrationPaymentAttemptRepository>();
                services.AddSingleton(attempts);
                services.RemoveAll<IPaidOrderAcceptanceFreshnessService>();
                services.AddSingleton(freshness);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(timeProvider);
            }));
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        SeededPayments seeded = await SeedAsync(factory.Services, attempts);
        retriever.RetrieveAsync(Arg.Any<HostedCheckoutRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(HostedCheckoutRetrieveResult.Succeeded(new HostedCheckoutSession(
                "cs_target",
                new Uri("https://checkout.stripe.com/c/pay/cs_target"),
                HostedCheckoutSessionStatus.Open,
                HostedCheckoutPaymentStatus.Unpaid,
                null,
                UtcNow.AddMinutes(10),
                1_125,
                "EUR"), null));

        const string sharedKey = "same-client-key";
        HttpResponseMessage first = await PostRetryAsync(client, seeded.First, sharedKey, "{}");
        HttpResponseMessage replay = await PostRetryAsync(client, seeded.First, sharedKey, "{}");
        HttpResponseMessage crossOrderCollision = await PostRetryAsync(client, seeded.Second, sharedKey, "{}");
        HttpResponseMessage wrongCapability = await PostRetryAsync(client, seeded.Second with { Capability = seeded.First.Capability }, "wrong-capability-key", "{}");
        HttpResponseMessage secondOrder = await PostRetryAsync(client, seeded.Second, "second-order-key", "{}");
        HttpResponseMessage expired = await PostRetryAsync(client, seeded.Expired, "expired-key", "{}");
        HttpResponseMessage missing = await PostRetryAsync(client, seeded.MissingCapability, "missing-key", "{}");
        HttpResponseMessage collision = await PostRetryAsync(client, seeded.First, sharedKey, "{\"different\":true}");

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(replay.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(replay.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(replay.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(crossOrderCollision.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(crossOrderCollision.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(wrongCapability.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(secondOrder.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondOrder.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(await secondOrder.Content.ReadAsStringAsync()).Contains(seeded.Second.OrderId.ToString("D"));
        await Assert.That(expired.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(collision.StatusCode).IsEqualTo(HttpStatusCode.Conflict);

        using HttpResponseMessage status = await GetAsync(client, seeded.Target, string.Empty);
        string statusJson = await status.Content.ReadAsStringAsync();
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(statusJson).DoesNotContain("acct_");
        await Assert.That(statusJson).DoesNotContain("providerRequest");
        await Assert.That(statusJson).DoesNotContain("idempotency");
        await Assert.That(statusJson).DoesNotContain(seeded.Target.Capability);

        using HttpResponseMessage target = await GetAsync(client, seeded.Target, "/checkout-target");
        using HttpResponseMessage deniedTarget = await GetAsync(client, seeded.Target with { Capability = "wrong" }, "/checkout-target");
        await Assert.That(target.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await target.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["url"])
            .IsEqualTo("https://checkout.stripe.com/c/pay/cs_target");
        await Assert.That(deniedTarget.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        timeProvider.Advance(TimeSpan.FromHours(1));
        using HttpResponseMessage expiredReplay = await PostRetryAsync(client, seeded.First, sharedKey, "{}");
        using HttpResponseMessage expiredStatus = await GetAsync(client, seeded.First, string.Empty);
        await Assert.That(expiredReplay.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(expiredReplay.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(expiredStatus.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TerminalGuestRetryWithStaleAcceptanceReplaysConflictWithoutReplacement()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid organizerActorId = Guid.CreateVersion7();
        var eventRepository = Substitute.For<IEventRepository>();
        eventRepository.GetEventWithDetailsAsync(eventId, TenantId, Arg.Any<CancellationToken>()).Returns(new Explore.Domain.Event
        {
            Id = eventId,
            TenantId = TenantId,
            OrganizerActorId = organizerActorId,
            Title = "Paid event",
            Actor = null!,
            Tenant = null!,
            EventFormat = null!,
            VisibilityType = null!,
            EventStatus = null!
        });
        OrganizerPaymentProviderConnection connection = OrganizerPaymentProviderConnection.Create(
            Guid.CreateVersion7(), TenantId, organizerActorId, "stripe", "platform-live-eu", "acct_authority", UtcNow);
        connection.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create(
            "BE", ChargeCapabilityState.Active, ProviderRequirementsState.Satisfied, ["EUR"], UtcNow, "ready-1"));
        var connections = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        connections.GetActiveByScopeAsync(
            TenantId, organizerActorId, "stripe", "platform-live-eu", Arg.Any<CancellationToken>()).Returns(connection);
        var policies = Substitute.For<IPaidEventPolicyRepository>();
        PaidEventPolicyVersion disabledPolicy = PaidEventPolicyVersion.CreateDefaultInstance();
        PaidEventPolicyVersion activePolicy = disabledPolicy.CreateRevision(
            true, disabledPolicy.AllowedOrganizerKinds, false, disabledPolicy.AllowedCurrencyCodes, "EUR",
            disabledPolicy.RefundProtections, [], false, null);
        policies.GetActiveInstanceAsync(Arg.Any<CancellationToken>()).Returns(activePolicy);
        var commerce = Substitute.For<IOrganizerPaymentCommerceConfiguration>();
        commerce.ProviderCode.Returns("stripe");
        commerce.ConnectPlatformId.Returns("platform-live-eu");
        var descriptor = Substitute.For<IPaymentProviderDescriptor>();
        descriptor.Describe().Returns(new PaymentProviderDescriptor(
            "stripe", "OrganizerDirect", "2026-07-29.dahlia", "test", "instance-operator"));
        var freshness = Substitute.For<IPaidOrderAcceptanceFreshnessService>();
        freshness.IsCurrentAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>()).Returns(false);
        await using WebApplicationFactory<Program> factory = new AuthenticatedWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEventRepository>();
                services.AddSingleton(eventRepository);
                services.RemoveAll<IOrganizerPaymentProviderConnectionRepository>();
                services.AddSingleton(connections);
                services.RemoveAll<IPaidEventPolicyRepository>();
                services.AddSingleton(policies);
                services.RemoveAll<IOrganizerPaymentCommerceConfiguration>();
                services.AddSingleton(commerce);
                services.RemoveAll<IPaymentProviderDescriptor>();
                services.AddSingleton(descriptor);
                services.RemoveAll<IPaidOrderAcceptanceFreshnessService>();
                services.AddSingleton(freshness);
                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
            }));
        PaymentScope payment;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            IGuestCapabilityTokenService capabilities = scope.ServiceProvider.GetRequiredService<IGuestCapabilityTokenService>();
            GuestCapabilityTokenIssue capability = capabilities.Issue();
            RegistrationOrder order = CreateOrder(eventId, orderId, capability.Hash, UtcNow.AddMinutes(30));
            Set(order, nameof(RegistrationOrder.OrganizerDirectedTotalMinorSnapshot), 1_000L);
            Set(order, nameof(RegistrationOrder.PlatformFeeTotalMinorSnapshot), 75L);
            Set(order, nameof(RegistrationOrder.PlatformContributionTotalMinorSnapshot), 125L);
            Set(order, nameof(RegistrationOrder.TotalDueMinorSnapshot), 1_200L);
            db.RegistrationOrders.Add(order);
            OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
                TenantId, organizerActorId, connection.Id, "stripe", "platform-live-eu", "acct_authority", "BE", "EUR",
                activePolicy.Id, null, UtcNow);
            PaymentAttempt terminal = PaymentAttempt.Create(
                Guid.CreateVersion7(), TenantId, orderId, recipient, "OrganizerDirect", "2026-07-29.dahlia",
                order.ConcurrencyStamp.ToString("N"), Money.Create(1_000, order.CurrencyCode), Money.Create(75, order.CurrencyCode), Money.Create(125, order.CurrencyCode), "checkout:terminal", UtcNow.AddMinutes(-1), UtcNow.AddMinutes(30));
            terminal.AttachAcceptance(PaidAcceptanceTestFacts.Create(
                recipient, orderId, eventId, order.ConcurrencyStamp.ToString("N"),
                1_000, 75, 125, UtcNow.AddMinutes(-1)));
            terminal.MarkDispatchFailed(UtcNow.AddSeconds(-30), "req-terminal");
            CheckoutDispatchEffect terminalEffect = CheckoutDispatchEffect.Create(terminal, UtcNow.AddMinutes(-1));
            db.PaymentAttempts.Add(terminal);
            db.CheckoutDispatchEffects.Add(terminalEffect);
            await db.SaveChangesAsync();
            payment = new(eventId, orderId, capability.RawToken);
        }

        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string replayKey = "terminal-retry-replay";
        using HttpResponseMessage first = await PostRetryAsync(client, payment, replayKey, "{}");
        string firstBody = await first.Content.ReadAsStringAsync();
        using HttpResponseMessage replay = await PostRetryAsync(client, payment, replayKey, "{}");
        string replayBody = await replay.Content.ReadAsStringAsync();

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(firstBody).Contains("payment_acceptance_stale");
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(replayBody).Contains("payment_acceptance_stale");
        using IServiceScope verificationScope = factory.Services.CreateScope();
        ExploreDbContext verification = verificationScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        PaymentAttempt[] attempts = await verification.PaymentAttempts
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(value => value.TenantId == TenantId && value.RegistrationOrderId == orderId)
            .ToArrayAsync();
        await Assert.That(attempts.Length).IsEqualTo(1);
        await Assert.That((PaymentAttemptStatusEnum)attempts[0].PaymentAttemptStatusId).IsEqualTo(PaymentAttemptStatusEnum.Failed);
        await Assert.That(await verification.CheckoutDispatchEffects
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .CountAsync(
            value => value.TenantId == TenantId && value.RegistrationOrderId == orderId)).IsEqualTo(1);
    }

    private static async Task<HttpResponseMessage> PostStartAsync(HttpClient client, PaymentScope scope, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route(scope));
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Registration-Order-Capability", scope.Capability);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostRetryAsync(HttpClient client, PaymentScope scope, string key, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route(scope) + "/retry")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", key);
        if (scope.Capability.Length > 0)
        {
            request.Headers.Add("X-Registration-Order-Capability", scope.Capability);
        }
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, PaymentScope scope, string suffix)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Route(scope) + suffix);
        request.Headers.Add("X-Registration-Order-Capability", scope.Capability);
        return await client.SendAsync(request);
    }

    private static string Route(PaymentScope scope) =>
        $"/api/events/{scope.EventId:D}/registration-orders/guest/{scope.OrderId:D}/payment";

    private static async Task<SeededPayments> SeedAsync(
        IServiceProvider services,
        IRegistrationPaymentAttemptRepository attempts)
    {
        using IServiceScope scope = services.CreateScope();
        ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        IGuestCapabilityTokenService capabilities = scope.ServiceProvider.GetRequiredService<IGuestCapabilityTokenService>();
        var attemptStates = new Dictionary<Guid, (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)>();
        PaymentScope first = AddRetryable(db, capabilities, attemptStates, Guid.CreateVersion7(), Guid.CreateVersion7(), UtcNow.AddMinutes(30));
        PaymentScope second = AddRetryable(db, capabilities, attemptStates, Guid.CreateVersion7(), Guid.CreateVersion7(), UtcNow.AddMinutes(30));
        PaymentScope expired = AddRetryable(db, capabilities, attemptStates, Guid.CreateVersion7(), Guid.CreateVersion7(), UtcNow.AddMinutes(-1));
        PaymentScope missing = AddRetryable(db, capabilities, attemptStates, Guid.CreateVersion7(), Guid.CreateVersion7(), UtcNow.AddMinutes(30)) with { Capability = string.Empty };
        PaymentScope target = AddTarget(db, capabilities, attemptStates, Guid.CreateVersion7(), Guid.CreateVersion7());
        await db.SaveChangesAsync();
        attempts.GetLatestByOrderAsync(TenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => attemptStates.TryGetValue(call.ArgAt<Guid>(1), out var state)
                ? state
                : ((PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)?)null);
        attempts.RetryParkedPreHandoffAsync(TenantId, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return new(first, second, expired, missing, target);
    }

    private static PaymentScope AddRetryable(
        ExploreDbContext db,
        IGuestCapabilityTokenService capabilities,
        IDictionary<Guid, (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)> attemptStates,
        Guid eventId,
        Guid orderId,
        DateTime expiresAt)
    {
        GuestCapabilityTokenIssue capability = capabilities.Issue();
        RegistrationOrder order = CreateOrder(eventId, orderId, capability.Hash, expiresAt);
        PaymentAttempt attempt = CreateAttempt(order, "cs_" + orderId.ToString("N"));
        attempt.MarkDispatchPending(UtcNow.AddSeconds(1), null);
        CheckoutDispatchEffect effect = CheckoutDispatchEffect.Create(attempt, UtcNow);
        Set(effect, nameof(CheckoutDispatchEffect.Status), OutboxMessageStatus.DeadLettered);
        Set(effect, nameof(CheckoutDispatchEffect.ParkedAt), UtcNow.AddSeconds(2));
        Set(effect, nameof(CheckoutDispatchEffect.LastFailureCode), "checkout_pre_handoff_failed");
        db.RegistrationOrders.Add(order);
        attemptStates[orderId] = (attempt, effect);
        return new(eventId, orderId, capability.RawToken);
    }

    private static PaymentScope AddTarget(
        ExploreDbContext db,
        IGuestCapabilityTokenService capabilities,
        IDictionary<Guid, (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)> attemptStates,
        Guid eventId,
        Guid orderId)
    {
        GuestCapabilityTokenIssue capability = capabilities.Issue();
        RegistrationOrder order = CreateOrder(eventId, orderId, capability.Hash, UtcNow.AddMinutes(30));
        PaymentAttempt attempt = CreateAttempt(order, "cs_target");
        attempt.MarkDispatchPending(UtcNow.AddSeconds(1), null);
        attempt.MarkRequiresAction("cs_target", UtcNow.AddSeconds(2), null);
        db.RegistrationOrders.Add(order);
        attemptStates[orderId] = (attempt, CheckoutDispatchEffect.Create(attempt, UtcNow));
        return new(eventId, orderId, capability.RawToken);
    }

    private static RegistrationOrder CreateOrder(Guid eventId, Guid orderId, CapabilityTokenHash capabilityHash, DateTime expiresAt)
    {
        RegistrationOrder order = RegistrationOrder.Create(
            orderId, TenantId, eventId, null, null, BookingPartyTypeEnum.Individual, Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required, (int)IdentityAccessModeEnum.CapabilityTokenAllowed,
                GuestRecoveryPolicyEnum.CapabilityLinkOnly),
            null, capabilityHash, "EUR", UtcNow.AddHours(-2), expiresAt);
        Set(order, nameof(RegistrationOrder.RegistrationOrderStatusId), (int)RegistrationOrderStatusEnum.AwaitingPayment);
        Set(order, nameof(RegistrationOrder.TotalDueMinorSnapshot), 1_200L);
        return order;
    }

    private static PaymentAttempt CreateAttempt(RegistrationOrder order, string sessionId)
    {
        OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
            TenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), "stripe", "platform-live-eu", "acct_private",
            "BE", "EUR", Guid.CreateVersion7(), null, UtcNow);
        PaymentAttempt attempt = PaymentAttempt.Create(
            Guid.CreateVersion7(), TenantId, order.Id, recipient, "OrganizerDirect", "2026-08-20.acacia",
            order.ConcurrencyStamp.ToString("N"), Money.Create(1_000, order.CurrencyCode), Money.Create(75, order.CurrencyCode), Money.Create(125, order.CurrencyCode), "checkout:" + sessionId, UtcNow,
            order.ExpiresAt <= UtcNow ? UtcNow.AddMinutes(30) : order.ExpiresAt);
        attempt.AttachAcceptance(PaidAcceptanceTestFacts.Create(
            recipient, order.Id, order.EventId, order.ConcurrencyStamp.ToString("N"),
            1_000, 75, 125, UtcNow));
        return attempt;
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    private sealed record PaymentScope(Guid EventId, Guid OrderId, string Capability);
    private sealed record SeededPayments(PaymentScope First, PaymentScope Second, PaymentScope Expired, PaymentScope MissingCapability, PaymentScope Target);

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = new(utcNow);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
