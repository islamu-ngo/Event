using Explore.Blazor.Client.Components.Notifications;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services.Notifications;

namespace Explore.Blazor.Client.Tests.Components.Notifications;

public sealed class WebPushSubscriptionPanelTests : IDisposable
{
    private readonly BlazorTestContext context = new();
    private readonly IWebPushBrowserInterop browserInterop = Substitute.For<IWebPushBrowserInterop>();
    private readonly INotificationService notificationService = Substitute.For<INotificationService>();

    public WebPushSubscriptionPanelTests()
    {
        context.Services.AddSingleton(browserInterop);
        context.Services.AddSingleton(notificationService);
        notificationService.GetWebPushConfigurationAsync()
            .Returns(new WebPushPublicConfiguration { Enabled = true, PublicKey = "public-key" });
        notificationService.GetVapidPublicKeyAsync()
            .Returns("public-key");
        notificationService.GetCurrentWebPushSubscriptionAsync(Arg.Any<string>())
            .Returns((HalResourceOfWebPushSubscriptionDto?)null);
    }

    public void Dispose()
    {
        context.Dispose();
    }

    [Test]
    public async Task DeniedPermission_ShowsTerminalStateWithoutEnableAction()
    {
        browserInterop.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns(new WebPushBrowserState(true, "denied", false, "device-a"));

        var component = context.RenderMudComponent<WebPushSubscriptionPanel>(parameters => parameters
            .Add(panel => panel.CanSubscribe, true));

        await WaitForAsync(component, () =>
        {
            if (!component.Markup.Contains("The site will not ask again.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Denied permission state was not rendered.");
            }
        });

        await Assert.That(component.FindAll("button")).IsEmpty();
        await browserInterop.DidNotReceive().SubscribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EnableAction_RequestsBrowserSubscriptionOnlyAfterClick()
    {
        browserInterop.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns(
                new WebPushBrowserState(true, "default", false, "device-a"),
                new WebPushBrowserState(true, "granted", true, "device-a"));
        browserInterop.SubscribeAsync("public-key", Arg.Any<CancellationToken>())
            .Returns(new WebPushBrowserSubscription(
                "device-a",
                "https://push.example.test/subscription",
                "p256dh",
                "auth",
                null));
        notificationService.SubscribeWebPushAsync(
                "device-a",
                "https://push.example.test/subscription",
                "p256dh",
                "auth",
                null)
            .Returns(true);

        var component = context.RenderMudComponent<WebPushSubscriptionPanel>(parameters => parameters
            .Add(panel => panel.CanSubscribe, true));
        await WaitForAsync(component, () =>
        {
            if (component.FindAll("button").Count == 0)
            {
                throw new InvalidOperationException("Enable action was not rendered.");
            }
        });
        await browserInterop.DidNotReceive().SubscribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        component.Find("button").Click();

        await WaitForAsync(component, () => browserInterop.Received(1)
            .SubscribeAsync("public-key", Arg.Any<CancellationToken>()));
        await notificationService.Received(1).SubscribeWebPushAsync(
            "device-a",
            "https://push.example.test/subscription",
            "p256dh",
            "auth",
            null);
    }

    [Test]
    public async Task DisableAction_WhenBrowserUnsubscribeFails_DoesNotDeactivateServerSubscription()
    {
        var subscriptionId = Guid.NewGuid();
        browserInterop.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns(new WebPushBrowserState(true, "granted", true, "device-a"));
        browserInterop.UnsubscribeAsync(Arg.Any<CancellationToken>()).Returns(false);
        notificationService.GetCurrentWebPushSubscriptionAsync("device-a")
            .Returns(new HalResourceOfWebPushSubscriptionDto
            {
                Id = subscriptionId,
                DeviceIdentifier = "device-a",
                _links = new Dictionary<string, HalLink>
                {
                    ["unsubscribe"] = new() { Href = $"/api/notification/web-push/subscriptions/{subscriptionId}", Method = "DELETE" }
                }
            });

        var component = context.RenderMudComponent<WebPushSubscriptionPanel>(parameters => parameters
            .Add(panel => panel.CanSubscribe, true));
        await WaitForAsync(component, () =>
        {
            if (!component.Markup.Contains("Enabled for this browser", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Enabled subscription state was not rendered.");
            }
        });

        component.Find("button").Click();

        await WaitForAsync(component, () => browserInterop.Received(1).UnsubscribeAsync(Arg.Any<CancellationToken>()));
        await notificationService.DidNotReceive().UnsubscribeWebPushAsync(Arg.Any<Guid>());
    }

    private static Task WaitForAsync<TComponent>(
        IRenderedComponent<TComponent> rendered,
        Action assertion)
        where TComponent : IComponent
    {
        rendered.WaitForAssertion(assertion);
        return Task.CompletedTask;
    }
}
