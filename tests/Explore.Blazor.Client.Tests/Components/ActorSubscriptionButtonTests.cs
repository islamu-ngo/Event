using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Shared;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Tests.Components;

public sealed class ActorSubscriptionButtonTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IActorSubscriptionService _subscriptionService;
    private readonly IAccessibilityAnnouncerService _announcer;

    public ActorSubscriptionButtonTests()
    {
        _ctx = new BlazorTestContext();
        _subscriptionService = Substitute.For<IActorSubscriptionService>();
        _announcer = Substitute.For<IAccessibilityAnnouncerService>();
        _announcer.AnnouncePoliteAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _announcer.AnnounceAssertiveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        _ctx.Services.RemoveAll<IActorSubscriptionService>();
        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.AddSingleton(_subscriptionService);
        _ctx.Services.AddSingleton(_announcer);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_WhenNoHalAffordance_RendersNothing()
    {
        var cut = RenderButton(canSubscribe: false, canViewSubscription: false);

        await Assert.That(cut.FindAll(".actor-subscription-button")).IsEmpty();
    }

    [Test]
    public async Task Render_WhenCanSubscribe_ShowsAccessibleSubscribeButton()
    {
        var cut = RenderButton(canSubscribe: true, canViewSubscription: false, targetName: "Community Org");

        var button = cut.Find("button[aria-label='Subscribe to notifications for Community Org']");
        await Assert.That(button.TextContent).Contains("Subscribe");
    }

    [Test]
    public async Task Click_WhenUnsubscribed_CallsSubscribeAndAnnouncesResult()
    {
        var targetActorId = Guid.NewGuid();
        _subscriptionService.SubscribeAsync(targetActorId, Arg.Any<CancellationToken>())
            .Returns(new ActorSubscriptionCommandResult(true, Guid.NewGuid(), "Subscribed"));
        _subscriptionService.GetSubscriptionAsync(targetActorId, Arg.Any<CancellationToken>())
            .Returns(
                (ActorSubscriptionDto?)null,
                new ActorSubscriptionDto { TargetActorId = targetActorId, StatusCode = "ACTIVE", ConcurrencyStamp = Guid.NewGuid() });

        var cut = RenderButton(targetActorId, canSubscribe: true, canViewSubscription: true, targetName: "Community Org");
        await cut.InvokeAsync(() => cut.Find("button").Click());

        await _subscriptionService.Received(1).SubscribeAsync(targetActorId, Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("Subscribed to notifications for Community Org.");
    }

    [Test]
    public async Task Click_WhenSubscribed_CallsUnsubscribeWithConcurrencyStamp()
    {
        var targetActorId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        _subscriptionService.GetSubscriptionAsync(targetActorId, Arg.Any<CancellationToken>())
            .Returns(new ActorSubscriptionDto { TargetActorId = targetActorId, StatusCode = "ACTIVE", ConcurrencyStamp = concurrencyStamp });
        _subscriptionService.UnsubscribeAsync(targetActorId, concurrencyStamp, Arg.Any<CancellationToken>())
            .Returns(new ActorSubscriptionCommandResult(true, Guid.NewGuid(), "Unsubscribed"));

        var cut = RenderButton(targetActorId, canSubscribe: true, canViewSubscription: true, targetName: "Community Org");
        await cut.InvokeAsync(() => cut.Find("button").Click());

        await _subscriptionService.Received(1).UnsubscribeAsync(targetActorId, concurrencyStamp, Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<ActorSubscriptionButton> RenderButton(
        bool canSubscribe,
        bool canViewSubscription,
        string targetName = "Target") => RenderButton(Guid.NewGuid(), canSubscribe, canViewSubscription, targetName);

    private IRenderedComponent<ActorSubscriptionButton> RenderButton(
        Guid targetActorId,
        bool canSubscribe,
        bool canViewSubscription,
        string targetName = "Target")
    {
        return _ctx.RenderMudComponent<ActorSubscriptionButton>(parameters => parameters
            .Add(component => component.TargetActorId, targetActorId)
            .Add(component => component.TargetName, targetName)
            .Add(component => component.CanSubscribe, canSubscribe)
            .Add(component => component.CanViewSubscription, canViewSubscription));
    }
}
