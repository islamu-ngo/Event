using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class TicketPurchaseAccessNativeAuthenticationTests
{
    [Test]
    public async Task AuthenticatedAccessSelectsCurrentTenantEventPolicy()
    {
        await using var fixture = await TicketingNativeAuthenticationFixture.CreateAsync();
        TicketingNativeAuthenticationFixture.OrderSeed seed = await fixture.SeedOrderAsync();
        Guid currentPolicyId;
        Guid oldPolicyId;
        await using (ExploreDbContext database = fixture.Database())
        {
            DateTime now = fixture.Http.Clock.GetUtcNow().UtcDateTime;
            var old = TicketPurchasePolicyVersion.Create(seed.TenantId, seed.EventId,
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, 1, now.AddMinutes(-2));
            oldPolicyId = old.Id;
            database.Add(old);
            var current = TicketPurchasePolicyVersion.Create(seed.TenantId, seed.EventId,
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 5, 4, 6, now.AddMinutes(-1));
            currentPolicyId = current.Id;
            database.Add(current);
            Guid actor = await database.Actors.Where(row => row.UserId == fixture.Http.Binding.LocalSubjectId)
                .Select(row => row.Id).SingleAsync();
            var otherEvent = new EventBuilder().WithActorId(actor).WithTenantId(seed.TenantId).Build();
            database.AddRange(otherEvent, TicketPurchasePolicyVersion.Create(seed.TenantId, otherEvent.Id,
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, 1, now));
            await database.SaveChangesAsync();
        }

        string owner = await fixture.Http.SignInAsync();
        using (HttpResponseMessage clientPolicy = await fixture.PurchaseAsync(seed, owner, clientPolicyId: oldPolicyId))
            await Assert.That(clientPolicy.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await using (ExploreDbContext before = fixture.Database())
        {
            await Assert.That(await before.TicketPurchaseOperations.AnyAsync()).IsFalse();
            await Assert.That(await before.TicketPurchaseAuthorityUsages.AnyAsync()).IsFalse();
        }
        using HttpResponseMessage response = await fixture.PurchaseAsync(seed, owner);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using ExploreDbContext observer = fixture.Database();
        var operation = await observer.TicketPurchaseOperations.AsNoTracking().SingleAsync();
        var usage = await observer.TicketPurchaseAuthorityUsages.AsNoTracking().SingleAsync();
        await Assert.That(operation.TenantId).IsEqualTo(seed.TenantId);
        await Assert.That(operation.EventId).IsEqualTo(seed.EventId);
        await Assert.That(operation.OrderId).IsEqualTo(seed.OrderId);
        await Assert.That(operation.PolicyVersionId).IsEqualTo(currentPolicyId);
        await Assert.That(operation.Disposition).IsEqualTo(TicketPurchaseReservationDisposition.Reserved);
        await Assert.That(operation.RequestedQuantity).IsEqualTo(2);
        await Assert.That(operation.EffectiveCeiling).IsEqualTo(4);
        await Assert.That(usage.TenantId).IsEqualTo(seed.TenantId);
        await Assert.That(usage.EventId).IsEqualTo(seed.EventId);
        await Assert.That(usage.AccessMode).IsEqualTo(TicketPurchaseAccessMode.AuthenticatedAccount);
        await Assert.That(usage.ActingAccountUserId).IsEqualTo(fixture.Http.Binding.LocalSubjectId);
        await Assert.That(usage.ConsumedQuantity).IsEqualTo(2);
    }

    [Test]
    public async Task AccountOwnershipFailurePrecedesCurrentPolicyLookup()
    {
        await using var fixture = await TicketingNativeAuthenticationFixture.CreateAsync();
        var seed = await fixture.SeedOrderAsync();
        var otherLogin = await fixture.Http.Native.SeedLocalUserAsync(true);
        string unrelated = await fixture.Http.SignInAsync(otherLogin);
        using HttpResponseMessage response = await fixture.PurchaseAsync(seed, unrelated);
        await TicketingNativeAuthenticationFixture.AssertOrderNotFound(response);
        await Assert.That(await fixture.NativeFailureCode(seed, unrelated)).IsEqualTo("registration_order_not_found");
        await fixture.AssertNoPurchaseEffects();
        string ownerToken = await fixture.Http.SignInAsync();
        using HttpResponseMessage owner = await fixture.PurchaseAsync(seed, ownerToken);
        await TicketingNativeAuthenticationFixture.AssertOrderNotFound(owner);
        await Assert.That(await fixture.NativeFailureCode(seed, ownerToken)).IsEqualTo("ticket_purchase_policy_unavailable");
        await fixture.AssertNoPurchaseEffects();
    }

    [Test]
    public async Task GuestCapabilityFailurePrecedesCurrentPolicyLookup()
    {
        await using var fixture = await TicketingNativeAuthenticationFixture.CreateAsync();
        var seed = await fixture.SeedOrderAsync(guest: true);
        string invalid = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using HttpResponseMessage response = await fixture.PurchaseAsync(seed, null, invalid);
        await TicketingNativeAuthenticationFixture.AssertOrderNotFound(response);
        await Assert.That(await fixture.NativeFailureCode(seed, null, invalid)).IsEqualTo("registration_order_not_found");
        await fixture.AssertNoPurchaseEffects();
        using HttpResponseMessage valid = await fixture.PurchaseAsync(seed, null, seed.GuestCapability);
        await TicketingNativeAuthenticationFixture.AssertOrderNotFound(valid);
        await Assert.That(await fixture.NativeFailureCode(seed, null, seed.GuestCapability))
            .IsEqualTo("ticket_purchase_policy_unavailable");
        await fixture.AssertNoPurchaseEffects();
    }
}

internal sealed class TicketingNativeAuthenticationFixture : IAsyncDisposable
{
    internal LocalIdentityLifecycleHttpFixture Http { get; private set; } = null!;

    private TicketingNativeAuthenticationFixture() { }

    internal static async Task<TicketingNativeAuthenticationFixture> CreateAsync()
    {
        var fixture = new TicketingNativeAuthenticationFixture();
        try
        {
            fixture.Http = await LocalIdentityLifecycleHttpFixture.CreateAsync(emailEnabled: false,
                configureServices: services => services.AddControllers().ConfigureApplicationPartManager(manager =>
                    manager.FeatureProviders.Add(new NativeContractProbeFeatureProvider())));
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal ExploreDbContext Database()
    {
        ExploreDbContext database = Http.Native.CreateDatabase();
        database.TenantContext = new SelectedTenant(PlatformDefaults.DefaultTenantId);
        database.BindTenantLease();
        return database;
    }

    internal async Task<OrderSeed> SeedOrderAsync(bool guest = false, bool transfer = false)
    {
        await using ExploreDbContext database = Database();
        Guid tenant = PlatformDefaults.DefaultTenantId;
        Guid user = Http.Binding.LocalSubjectId;
        Guid actor = await database.Actors.Where(row => row.UserId == user).Select(row => row.Id).SingleAsync();
        database.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            Tenant = null!,
            UserId = user,
            User = null!,
            ActorId = actor,
            Actor = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = Http.Clock.GetUtcNow().UtcDateTime
        });
        DateTime now = Http.Clock.GetUtcNow().UtcDateTime.AddHours(transfer ? -2 : 0);
        var target = new EventBuilder().WithActorId(actor).WithTenantId(tenant)
            .WithSessionDates(DateOnly.FromDateTime(now.AddDays(2)), DateOnly.FromDateTime(now.AddDays(2))).Build();
        var catalog = EventTicketCatalogVersion.Create(tenant, target.Id, "EUR", 1);
        var type = EventTicketType.Create(Guid.CreateVersion7(), tenant, catalog.Id, "Native admission", "EUR",
            TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None,
            null, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(type, null);
        catalog.AddEntitlement(type, TicketTypeEntitlement.CreateForEvent(type.Id, tenant, target.Id, 1));
        catalog.Publish();
        using AsyncServiceScope scope = Http.Host.Services.CreateAsyncScope();
        GuestCapabilityTokenIssue capability = scope.ServiceProvider.GetRequiredService<IGuestCapabilityTokenService>().Issue();
        var order = RegistrationOrder.Create(tenant, target.Id, guest ? null : user, guest ? null : actor,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required, (int)IdentityAccessModeEnum.CapabilityTokenAllowed,
                GuestRecoveryPolicyEnum.CapabilityLinkOnly), null, guest ? capability.Hash : null,
            "EUR", now, now.AddDays(1));
        var line = RegistrationOrderLine.Create(catalog, type, order.Id, transfer ? 1 : 2, null, null);
        order.AddLine(line);
        Guid? ticketId = null;
        if (transfer)
        {
            var participant = RegistrationParticipant.Create(tenant, order.Id, user, ParticipantTypeEnum.Adult, null);
            var assignment = RegistrationTicketAssignment.CreateAssigned(Guid.CreateVersion7(), line.Id, 1, participant, now);
            order.AddParticipant(participant);
            order.AddAssignment(line, assignment, participant);
            order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("EUR", 0, 0, 0, 0));
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, now);
            order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, now);
            order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, now);
            var eligibility = ParticipantAdmissionEligibility.Create(tenant, target.Id, assignment, participant, false, false, now);
            eligibility.RecordSubjectCompletion(participant, user, null, now, Guid.CreateVersion7());
            var ticket = AdmissionTicket.Issue(order, line, assignment, participant, catalog, type,
                Guid.CreateVersion7(), $"NATIVE-{Guid.CreateVersion7():N}", Guid.CreateVersion7(), 1, 1,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), now);
            ticketId = ticket.Id;
            database.AddRange(ticket, eligibility,
                TicketTransferPolicy.Create(Guid.CreateVersion7(), tenant, type, true, 3, 60, 30, now));
        }
        database.AddRange(target, catalog, order);
        await database.SaveChangesAsync();
        return new(tenant, target.Id, order.Id, ticketId, guest ? capability.RawToken : null);
    }

    internal async Task<HttpResponseMessage> PurchaseAsync(OrderSeed seed, string? token, string? capability = null,
        Guid? clientPolicyId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/events/{seed.EventId}/registration-orders/{(seed.GuestCapability is null ? "" : "guest/")}{seed.OrderId}/purchase-authority")
        {
            Content = clientPolicyId.HasValue
                ? JsonContent.Create(new { accessMode = TicketPurchaseAccessMode.NameOnly, policyVersionId = clientPolicyId.Value })
                : JsonContent.Create(new { accessMode = TicketPurchaseAccessMode.NameOnly })
        };
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (capability is not null) request.Headers.Add("X-Registration-Order-Capability", capability);
        return await Http.Client.SendAsync(request);
    }

    internal static async Task AssertOrderNotFound(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("title").GetString()).IsEqualTo("Registration order not found");
        await Assert.That(body.RootElement.GetProperty("detail").GetString()).IsEqualTo("Registration order was not found.");
    }

    internal async Task<string?> NativeFailureCode(OrderSeed seed, string? token, string? capability = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/__testing/native-ticket-purchase/{seed.EventId}/{seed.OrderId}/{(seed.GuestCapability is null ? "account" : "guest")}");
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (capability is not null) request.Headers.Add("X-Registration-Order-Capability", capability);
        using HttpResponseMessage response = await Http.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("failureCode").GetString();
    }

    internal async Task AssertNoPurchaseEffects()
    {
        await using ExploreDbContext observer = Database();
        await Assert.That(await observer.TicketPurchasePolicyVersions.AnyAsync()).IsFalse();
        await Assert.That(await observer.TicketPurchaseOperations.AnyAsync()).IsFalse();
        await Assert.That(await observer.TicketPurchaseAuthorityUsages.AnyAsync()).IsFalse();
    }

    public ValueTask DisposeAsync() => Http is null ? ValueTask.CompletedTask : Http.DisposeAsync();

    internal sealed record OrderSeed(Guid TenantId, Guid EventId, Guid OrderId, Guid? TicketId, string? GuestCapability);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    private sealed class NativeContractProbeFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
            feature.Controllers.Add(typeof(NativeTicketPurchaseContractProbeController).GetTypeInfo());
    }
}

[ApiController]
[Route("__testing/native-ticket-purchase/{eventId:guid}/{orderId:guid}")]
[RequireIdempotencyKey]
public sealed class NativeTicketPurchaseContractProbeController(
    ICommandHandler<ReserveAuthenticatedTicketPurchaseCommand, BaseCommandResponse<Guid>> account,
    ICommandHandler<ReserveGuestTicketPurchaseCommand, BaseCommandResponse<Guid>> guest) : ControllerBase
{
    [HttpPost("account")]
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    public Task<BaseCommandResponse<Guid>> Account(Guid eventId, Guid orderId,
        [FromHeader(Name = "Idempotency-Key")] string operationKey, CancellationToken cancellationToken) =>
        account.ExecuteAsync(new(eventId, orderId, null, operationKey), cancellationToken);

    [HttpPost("guest")]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    public Task<BaseCommandResponse<Guid>> Guest(Guid eventId, Guid orderId,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        [FromHeader(Name = "X-Registration-Order-Capability")] string? capability, CancellationToken cancellationToken) =>
        guest.ExecuteAsync(new(eventId, orderId, TicketPurchaseAccessMode.NameOnly, capability, operationKey), cancellationToken);
}
