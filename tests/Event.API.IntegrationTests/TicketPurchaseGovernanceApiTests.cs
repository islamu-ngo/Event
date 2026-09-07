using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Hateoas.Policies;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class TicketPurchaseGovernanceApiTests
{
    private const string AuthenticatedMethod =
        "ReserveAuthenticatedPurchaseAuthority";
    private const string GuestMethod =
        "ReserveGuestPurchaseAuthority";
    private const string AuthenticatedPath =
        "/api/events/{eventId}/registration-orders/{orderId}/purchase-authority";
    private const string GuestPath =
        "/api/events/{eventId}/registration-orders/guest/{orderId}/purchase-authority";
    private readonly ContractApiFixture _fixture;

    public TicketPurchaseGovernanceApiTests(
        ContractApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task PurchaseEndpointContractsUseScopedAuthRateLimitAndIdempotency()
    {
        MethodInfo? authenticated =
            typeof(TicketPurchaseController)
                .GetMethod(AuthenticatedMethod);
        MethodInfo? guest =
            typeof(TicketPurchaseController)
                .GetMethod(GuestMethod);

        await Assert.That(authenticated).IsNotNull();
        await Assert.That(guest).IsNotNull();
        await Assert.That(
                authenticated!.GetCustomAttribute<AuthorizeAttribute>())
            .IsNotNull();
        await Assert.That(
                guest!.GetCustomAttribute<AllowAnonymousAttribute>())
            .IsNotNull();
        foreach (MethodInfo endpoint in
                 new[] { authenticated, guest })
        {
            await Assert.That(
                    endpoint.GetCustomAttribute<
                        EnableRateLimitingAttribute>())
                .IsNotNull();
            await Assert.That(
                    endpoint.GetCustomAttribute<
                        RequireIdempotencyKeyAttribute>())
                .IsNotNull();
            await Assert.That(
                    endpoint.GetCustomAttribute<
                        ProtectIdempotencyReplayAttribute>())
                .IsNotNull();
        }
    }

    [Test]
    public async Task PublicSchemaExcludesCallerOwnedAuthorityAndExposesHonestScope()
    {
        Assembly api = typeof(AuthenticatedRegistrationOrderController)
            .Assembly;
        Type? request = api.GetType(
            "Explore.API.Models.ReserveTicketPurchaseRequest");
        Type? resource = api.GetType(
            "Explore.API.Models.TicketPurchaseGovernanceResource");

        await Assert.That(request).IsNotNull();
        await Assert.That(resource).IsNotNull();
        string[] requestProperties = request!
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(requestProperties).DoesNotContain(
            "TenantId");
        await Assert.That(requestProperties).DoesNotContain(
            "AccountUserId");
        await Assert.That(requestProperties).DoesNotContain(
            "EnforcementKey");
        await Assert.That(requestProperties).DoesNotContain(
            "Quantity");
        await Assert.That(requestProperties).DoesNotContain(
            "PolicyVersionId");
        await Assert.That(resource!.GetProperty(
                "SupportsHardCrossOrderCeiling"))
            .IsNotNull();
        await Assert.That(resource.GetProperty(
                "EnforcementScopeCode"))
            .IsNotNull();
    }

    [Test]
    public async Task AnonymousAccountPurchaseFailsAsUnauthorizedAndPrivate()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        using var request = CreatePurchaseRequest(
            eventId,
            orderId,
            guest: false);

        using HttpResponseMessage response =
            await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertPrivateNoStore(response);
    }

    [Test]
    public async Task MissingGuestCapabilityFailsIndistinguishablyAndPrivate()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        using var request = CreatePurchaseRequest(
            eventId,
            orderId,
            guest: true);
        request.Headers.Add(
            "Idempotency-Key",
            "missing-capability-test");

        using HttpResponseMessage response =
            await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await AssertPrivateNoStore(response);
    }

    [Test]
    public async Task OpenApiPublishesBothOperationsAndRequiredAuthorityHeaders()
    {
        using HttpResponseMessage response =
            await _fixture.Client.GetAsync(
                "/openapi/islamu-event.json");
        using JsonDocument document =
            JsonDocument.Parse(
                await response.Content.ReadAsStreamAsync());
        JsonElement paths =
            document.RootElement.GetProperty("paths");

        await Assert.That(paths.TryGetProperty(
                AuthenticatedPath,
                out JsonElement authenticated))
            .IsTrue();
        await Assert.That(paths.TryGetProperty(
                GuestPath,
                out JsonElement guest))
            .IsTrue();
        await AssertRequiredHeader(
            authenticated.GetProperty("post"),
            "Idempotency-Key");
        await AssertRequiredHeader(
            guest.GetProperty("post"),
            "Idempotency-Key");
        await AssertRequiredHeader(
            guest.GetProperty("post"),
            "X-Registration-Order-Capability",
            requireRequired: false);
    }

    [Test]
    public async Task AwaitingPaymentOrderOffersPurchaseAuthorityHalAction()
    {
        DateTime now = new(
            2026,
            8,
            27,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var order = new RegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            AccountUserId = Guid.CreateVersion7(),
            StatusId =
                (int)RegistrationOrderStatusEnum.AwaitingPayment,
            StatusCode = "AWAITING_PAYMENT",
            TotalDueMinor = 1_000,
            ExpiresAt = now.AddMinutes(5),
        };
        var policy = new RegistrationOrderLinkPolicy(
            new FixedTimeProvider(now));

        string[] relations = policy.GetLinks(order, null)
            .Select(link => link.Rel)
            .ToArray();

        await Assert.That(relations)
            .Contains("reserve-purchase-authority");
    }

    private static HttpRequestMessage CreatePurchaseRequest(
        Guid eventId,
        Guid orderId,
        bool guest)
    {
        string path = guest
            ? $"/api/events/{eventId:D}/registration-orders/guest/{orderId:D}/purchase-authority"
            : $"/api/events/{eventId:D}/registration-orders/{orderId:D}/purchase-authority";
        return new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                $$"""
                  {
                    "accessMode": "NameOnly"
                  }
                  """,
                Encoding.UTF8,
                "application/hal+json"),
        };
    }

    private static async Task AssertRequiredHeader(
        JsonElement operation,
        string header,
        bool requireRequired = true)
    {
        bool present = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .Any(parameter =>
                parameter.TryGetProperty(
                    "in",
                    out JsonElement location)
                && location.GetString() ==
                "header"
                && parameter.TryGetProperty(
                    "name",
                    out JsonElement name)
                && name.GetString() ==
                header
                && (!requireRequired
                    || parameter.TryGetProperty(
                        "required",
                        out JsonElement required)
                    && required.GetBoolean()));
        await Assert.That(present).IsTrue();
    }

    private static async Task AssertPrivateNoStore(
        HttpResponseMessage response)
    {
        await Assert.That(
                response.Headers.CacheControl?.Private)
            .IsTrue();
        await Assert.That(
                response.Headers.CacheControl?.NoStore)
            .IsTrue();
    }

    private sealed class FixedTimeProvider(
        DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(utcNow);
    }
}
