namespace Event.Domain.UnitTests.Entities;

public sealed class WebPushSubscriptionTests
{
    [Test]
    public async Task CreateInitializesActiveSubscriptionForUserDevice()
    {
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var now = DomainTestClock.UtcNow;

        var subscription = WebPushSubscription.Create(
            tenantId,
            userId,
            "device-1",
            "https://push.example/subscription/1",
            "p256dh-key",
            "auth-secret",
            now.AddDays(30),
            now);

        await Assert.That(subscription.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(subscription.TenantId).IsEqualTo(tenantId);
        await Assert.That(subscription.UserId).IsEqualTo(userId);
        await Assert.That(subscription.DeviceIdentifier).IsEqualTo("device-1");
        await Assert.That(subscription.Endpoint).IsEqualTo("https://push.example/subscription/1");
        await Assert.That(subscription.P256Dh).IsEqualTo("p256dh-key");
        await Assert.That(subscription.AuthSecret).IsEqualTo("auth-secret");
        await Assert.That(subscription.IsActive).IsTrue();
        await Assert.That(subscription.CreatedAt).IsEqualTo(now);
        await Assert.That(subscription.LastSeenAt).IsEqualTo(now);
        await Assert.That(subscription.UnsubscribedAt).IsNull();
        await Assert.That(subscription.DeactivatedAt).IsNull();
    }

    [Test]
    public async Task TouchRotatesEndpointAndKeysOnlyWhileActive()
    {
        var now = DomainTestClock.UtcNow;
        var subscription = WebPushSubscription.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "device-1",
            "https://push.example/subscription/1",
            "p256dh-key",
            "auth-secret",
            null,
            now);

        var touchedAt = now.AddMinutes(5);
        subscription.Touch(
            "https://push.example/subscription/2",
            "p256dh-key-2",
            "auth-secret-2",
            touchedAt.AddDays(7),
            touchedAt);

        await Assert.That(subscription.Endpoint).IsEqualTo("https://push.example/subscription/2");
        await Assert.That(subscription.P256Dh).IsEqualTo("p256dh-key-2");
        await Assert.That(subscription.AuthSecret).IsEqualTo("auth-secret-2");
        await Assert.That(subscription.ExpirationTime).IsEqualTo(touchedAt.AddDays(7));
        await Assert.That(subscription.LastSeenAt).IsEqualTo(touchedAt);
        await Assert.That(subscription.UpdatedAt).IsEqualTo(touchedAt);

        subscription.Unsubscribe(touchedAt.AddMinutes(1));

        await Assert.That(() => subscription.Touch(
                "https://push.example/subscription/3",
                "p256dh-key-3",
                "auth-secret-3",
                null,
                touchedAt.AddMinutes(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnsubscribeAndDeactivateAreTerminalInactiveTransitions()
    {
        var now = DomainTestClock.UtcNow;
        var subscription = WebPushSubscription.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "device-1",
            "https://push.example/subscription/1",
            "p256dh-key",
            "auth-secret",
            null,
            now);

        subscription.Unsubscribe(now.AddMinutes(1));

        await Assert.That(subscription.IsActive).IsFalse();
        await Assert.That(subscription.UnsubscribedAt).IsEqualTo(now.AddMinutes(1));
        await Assert.That(subscription.DeactivatedAt).IsNull();

        await Assert.That(() => subscription.Deactivate("stale_endpoint", now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
    }
}
