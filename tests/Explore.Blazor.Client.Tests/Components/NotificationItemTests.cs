using Explore.Blazor.Client.Layout;

namespace Explore.Blazor.Client.Tests.Components;

public sealed class NotificationItemTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Render_WithSubscriptionNotification_ShowsReasonSourceAndContext()
    {
        var notification = CreateSubscriptionNotification();

        var cut = Render(notification);

        await Assert.That(cut.Markup).Contains("Subscription");
        await Assert.That(cut.Markup).Contains("From Islamic Center");
        await Assert.That(cut.Markup).Contains("via Youth Group");
        await Assert.That(cut.Markup).Contains("Reason: Subscription");
    }

    [Test]
    public async Task KeyDown_WithEnter_InvokesClickCallback()
    {
        var notification = CreateSubscriptionNotification();
        var clicked = false;

        var cut = Render(notification, _ => clicked = true);

        await cut.Find(".notification-item").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(clicked).IsTrue();
    }

    [Test]
    public async Task KeyDown_WithSpace_InvokesClickCallback()
    {
        var notification = CreateSubscriptionNotification();
        var clicked = false;

        var cut = Render(notification, _ => clicked = true);

        await cut.Find(".notification-item").KeyDownAsync(new KeyboardEventArgs { Key = " " });

        await Assert.That(clicked).IsTrue();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private IRenderedComponent<NotificationItem> Render(NotificationListDto notification, Action<NotificationListDto>? onClick = null)
    {
        return _ctx.RenderMudComponent<NotificationItem>(parameters => parameters
            .Add(component => component.Notification, notification)
            .Add(component => component.OnClick, notification => onClick?.Invoke(notification)));
    }

    private static NotificationListDto CreateSubscriptionNotification()
    {
        return new NotificationListDto
        {
            Id = Guid.NewGuid(),
            Title = "New event: Community Iftar",
            Body = "An organization or group you follow published a new event.",
            NotificationReasonName = "Subscription",
            NotificationScopeName = "Group",
            SourceActorName = "Islamic Center",
            RecipientContextActorName = "Youth Group",
            CreatedAt = TestTime.UtcNow,
            IsRead = false
        };
    }
}
