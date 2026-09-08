// ABOUTME: Service tests for registration-order lifecycle transport and safe guest recovery failures.
// ABOUTME: Proves missing capability transport metadata never escapes into the page flow.

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class RegistrationOrderServiceTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IRegistrationOrderClient _orderClient;
    private readonly IAuthenticatedRegistrationOrderClient _authenticatedClient;
    private readonly IGuestRegistrationOrderClient _guestClient;
    private readonly RegistrationOrderService _service;

    public RegistrationOrderServiceTests()
    {
        _orderClient = Substitute.For<IRegistrationOrderClient>();
        _authenticatedClient = Substitute.For<IAuthenticatedRegistrationOrderClient>();
        _guestClient = Substitute.For<IGuestRegistrationOrderClient>();
        var eventService = Substitute.For<IEventService>();
        var capabilityStore = Substitute.For<IGuestRegistrationOrderCapabilityStore>();
        var logger = Substitute.For<ILogger<RegistrationOrderService>>();
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        var shellState = _ctx.Services.GetRequiredService<UiShellState>();
        var challengeClient = Substitute.For<IAnonymousRegistrationChallengeClient>();
        challengeClient.CreateAnonymousRegistrationChallengeAsync(Arg.Any<Guid>(),
            idempotency_Key: Arg.Any<string>(), body: Arg.Any<StartRegistrationOrderRequest>(),
            cancellationToken: Arg.Any<CancellationToken>()).Returns(new HalResourceOfAnonymousRegistrationChallengeDto
            {
                ProtectedChallenge = Guid.NewGuid().ToString("N"), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
                Difficulty = 18, Version = 1
            });
        var solver = Substitute.For<IAnonymousRegistrationChallengeSolver>();
        solver.SolveAsync(Arg.Any<HalResourceOfAnonymousRegistrationChallengeDto>(), Arg.Any<Action<int>>(), Arg.Any<CancellationToken>())
            .Returns("0000000000000000");
        _service = new RegistrationOrderService(
            _orderClient,
            _authenticatedClient,
            _guestClient,
            eventService,
            shellState,
            capabilityStore,
            logger,
            challengeClient,
            solver,
            _ctx.Services.GetRequiredService<NavigationManager>(),
            TimeProvider.System,
            _ctx.Services.GetRequiredService<AuthenticationStateProvider>(),
            Substitute.For<IGuestRegistrationStatusClient>());
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task StartGuestAsync_WhenCapabilityHeaderIsMissing_ReturnsNullInsteadOfEscaping()
    {
        _guestClient.StartGuestRegistrationOrderWithCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<StartRegistrationOrderRequest>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<GuestRegistrationOrderStartResult>(
                new InvalidOperationException("Guest registration capability was not returned.")));

        var result = await _service.StartGuestAsync(Guid.CreateVersion7(), new StartRegistrationOrderRequest());

        await Assert.That(result).IsNull();
        await Assert.That(_service.PendingGuestEventId.HasValue).IsTrue();
        await Assert.That(_service.GuestStartPhase).IsEqualTo(GuestRegistrationStartPhase.Uncertain);
    }

    [Test]
    public async Task ApplyCurrentPromotionAsync_WithoutApplyPromotionRelation_DoesNotCallGeneratedClient()
    {
        var order = CreateOrder();

        var result = await _service.ApplyCurrentPromotionAsync(order.EventId!.Value, order.Id!.Value, order, "SAVE10");

        await Assert.That(result).IsNull();
        await _authenticatedClient.DidNotReceive().ApplyAuthenticatedRegistrationOrderPromotionAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<PromotionCodeRequest>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyCurrentPromotionAsync_WithApplyPromotionRelation_UsesGeneratedClientThenReloadsOrder()
    {
        var order = CreateOrder("apply-promotion");
        var updated = CreateOrder("remove-promotion");
        updated.Id = order.Id;
        updated.EventId = order.EventId;
        _authenticatedClient.ApplyAuthenticatedRegistrationOrderPromotionAsync(
                order.EventId!.Value,
                order.Id!.Value,
                Arg.Is<string>(value => IsUuid7(value)),
                Arg.Is<PromotionCodeRequest>(request => request.Code == "SAVE10"),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new PromotionRedemptionResponseDto { AppliedPromotionDisplayLabel = "Promotion ending in 10" });
        _authenticatedClient.GetCurrentRegistrationOrderAsync(order.EventId.Value, order.Id.Value, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(updated);

        var result = await _service.ApplyCurrentPromotionAsync(order.EventId.Value, order.Id.Value, order, "SAVE10");

        await Assert.That(result).IsSameReferenceAs(updated);
        await _authenticatedClient.Received(1).ApplyAuthenticatedRegistrationOrderPromotionAsync(
            order.EventId.Value,
            order.Id.Value,
            Arg.Is<string>(value => IsUuid7(value)),
            Arg.Any<PromotionCodeRequest>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RemoveCurrentPromotionAsync_WithoutRemovePromotionRelation_DoesNotCallGeneratedClient()
    {
        var order = CreateOrder();

        var result = await _service.RemoveCurrentPromotionAsync(order.EventId!.Value, order.Id!.Value, order);

        await Assert.That(result).IsNull();
        await _authenticatedClient.DidNotReceive().RemoveAuthenticatedRegistrationOrderPromotionAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RemoveGuestPromotionAsync_WithRemovePromotionRelation_PreservesCapabilityHeaderTransport()
    {
        var order = CreateGuestOrder("remove-promotion");
        var capability = new GuestRegistrationOrderCapability("opaque-capability");
        _guestClient.RemoveGuestRegistrationOrderPromotionAsync(
                order.EventId!.Value,
                order.Id!.Value,
                Arg.Is<string?>(value => IsUuid7(value)),
                capability.Value,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new PromotionRedemptionResponseDto());
        _guestClient.GetGuestRegistrationOrderAsync(order.EventId.Value, order.Id.Value, capability.Value, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _service.RemoveGuestPromotionAsync(order.EventId.Value, order.Id.Value, capability, order);

        await Assert.That(result).IsSameReferenceAs(order);
        await _guestClient.Received(1).RemoveGuestRegistrationOrderPromotionAsync(
            order.EventId.Value,
            order.Id.Value,
            Arg.Is<string?>(value => IsUuid7(value)),
            capability.Value,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyGuestPromotionAsync_WithoutApplyPromotionRelation_DoesNotSendCapabilityOrCode()
    {
        var order = CreateGuestOrder();

        var result = await _service.ApplyGuestPromotionAsync(
            order.EventId!.Value,
            order.Id!.Value,
            new GuestRegistrationOrderCapability("opaque-capability"),
            order,
            "SAVE10");

        await Assert.That(result).IsNull();
        await _guestClient.DidNotReceive().ApplyGuestRegistrationOrderPromotionAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<PromotionCodeRequest>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    private static HalResourceOfRegistrationOrderDto CreateOrder(params string[] relations)
    {
        var order = new HalResourceOfRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7()
        };
        foreach (var relation in relations)
        {
            order._links ??= new Dictionary<string, HalLink>();
            order._links[relation] = new HalLink { Href = $"/orders/{order.Id}/{relation}", Method = "POST" };
        }

        return order;
    }

    private static HalResourceOfGuestRegistrationOrderDto CreateGuestOrder(params string[] relations)
    {
        var order = new HalResourceOfGuestRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7()
        };
        foreach (var relation in relations)
        {
            order._links ??= new Dictionary<string, HalLink>();
            order._links[relation] = new HalLink { Href = $"/guest/orders/{order.Id}/{relation}", Method = "POST" };
        }

        return order;
    }

    private static bool IsUuid7(string? value) =>
        Guid.TryParse(value, out Guid idempotencyKey) && idempotencyKey.Version == 7;
}
