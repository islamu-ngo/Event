using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class NotificationRefreshStreamClientTests
{
    [Test]
    public async Task RefreshCallbacks_DoNotOverlapOrRunAfterDisposal()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = CreateClient();
        var count = 0;
        client.RefreshReceived += async _ =>
        {
            Interlocked.Increment(ref count);
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };

        var first = client.HandleWebPushRefresh();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var concurrent = client.HandleNotificationRefresh(5, true, "refresh", null);
            await Assert.That(count).IsEqualTo(1);
            await concurrent.WaitAsync(TimeSpan.FromSeconds(5));
            await client.HandleNotificationPoll().WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(count).IsEqualTo(1);
        }
        finally
        {
            release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            await client.DisposeAsync();
        }

        await client.HandleWebPushRefresh();
        await client.HandleNotificationPoll();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task HandleNotificationRefresh_RaisesRefreshReceivedWithParsedGeneratedAt()
    {
        var generatedAt = TestTime.UtcNow;
        var received = new List<NotificationRefreshHintReceivedEventArgs>();
        var client = CreateClient();
        client.RefreshReceived += args =>
        {
            received.Add(args);
            return Task.CompletedTask;
        };

        await client.HandleNotificationRefresh(5, true, "unread-count-changed", generatedAt.ToString("O"));

        await Assert.That(received).Count().IsEqualTo(1);
        await Assert.That(received[0].UnreadCount).IsEqualTo(5);
        await Assert.That(received[0].HasUnread).IsTrue();
        await Assert.That(received[0].Reason).IsEqualTo("unread-count-changed");
        await Assert.That(received[0].GeneratedAt).IsEqualTo(generatedAt);
    }

    [Test]
    public async Task HandleNotificationRefresh_WithMissingReason_UsesSafeDefaultReason()
    {
        var received = new List<NotificationRefreshHintReceivedEventArgs>();
        var client = CreateClient();
        client.RefreshReceived += args =>
        {
            received.Add(args);
            return Task.CompletedTask;
        };

        await client.HandleNotificationRefresh(0, false, null, null);

        await Assert.That(received).Count().IsEqualTo(1);
        await Assert.That(received[0].Reason).IsEqualTo("refresh");
        await Assert.That(received[0].GeneratedAt).IsNotEqualTo(default);
    }

    [Test]
    public async Task StartAsync_WhenJavaScriptInteropUnavailable_DoesNotThrow()
    {
        var jsRuntime = new ThrowingJsRuntime(new InvalidOperationException(
            "JavaScript interop calls cannot be issued at this time. This is because the component is being statically rendered."));
        var navigation = new TestNavigationManager();
        var logger = Substitute.For<ILogger<NotificationRefreshStreamClient>>();
        var client = new NotificationRefreshStreamClient(jsRuntime, navigation, logger);

        await client.StartAsync();
    }

    private static NotificationRefreshStreamClient CreateClient()
    {
        return new NotificationRefreshStreamClient(
            Substitute.For<IJSRuntime>(),
            new TestNavigationManager(),
            Substitute.For<ILogger<NotificationRefreshStreamClient>>());
    }

    private sealed class ThrowingJsRuntime(Exception exception) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return ValueTask.FromException<TValue>(exception);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            return ValueTask.FromException<TValue>(exception);
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("https://client.test/", "https://client.test/");
        }
    }
}
