namespace Explore.Blazor.Client.Tests.Services;

public class CookieConsentStateServiceTests
{
    [Test]
    public async Task RequestReopenAsync_WithSubscriber_InvokesHandler()
    {
        var service = new CookieConsentStateService();
        var invoked = false;

        service.OnReopenRequested += () =>
        {
            invoked = true;
            return Task.CompletedTask;
        };

        await service.RequestReopenAsync();

        await Assert.That(invoked).IsTrue();
    }

    [Test]
    public async Task RequestReopenAsync_WithNoSubscribers_DoesNotThrow()
    {
        var service = new CookieConsentStateService();

        // Should not throw when no handlers are attached
        await service.RequestReopenAsync();

        // If we get here, no exception was thrown
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task RequestReopenAsync_WithMultipleSubscribers_InvokesAll()
    {
        var service = new CookieConsentStateService();
        var count = 0;

        service.OnReopenRequested += () =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        };

        service.OnReopenRequested += () =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        };

        await service.RequestReopenAsync();

        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task OnReopenRequested_UnsubscribedHandler_IsNotInvoked()
    {
        var service = new CookieConsentStateService();
        var invoked = false;

        Func<Task> handler = () =>
        {
            invoked = true;
            return Task.CompletedTask;
        };

        service.OnReopenRequested += handler;
        service.OnReopenRequested -= handler;

        await service.RequestReopenAsync();

        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task RequestReopenAsync_CalledMultipleTimes_InvokesEachTime()
    {
        var service = new CookieConsentStateService();
        var count = 0;

        service.OnReopenRequested += () =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        };

        await service.RequestReopenAsync();
        await service.RequestReopenAsync();
        await service.RequestReopenAsync();

        await Assert.That(count).IsEqualTo(3);
    }
}
