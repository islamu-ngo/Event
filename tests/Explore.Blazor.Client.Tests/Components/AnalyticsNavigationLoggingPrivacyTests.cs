using System.Threading.Channels;
using Explore.Blazor.Client.Shared;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Tests.Components;

public sealed class AnalyticsNavigationLoggingPrivacyTests
{
    private const string QuerySentinel = "QUERY_SECRET_118";
    private const string FragmentSentinel = "FRAGMENT_SECRET_118";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Test]
    [Arguments("/events", "/events")]
    [Arguments("/events?token=" + QuerySentinel, "/events")]
    [Arguments("/events#" + FragmentSentinel, "/events")]
    [Arguments("/events/%0D%0Aforged?token=" + QuerySentinel + "#" + FragmentSentinel,
        "/events/%0D%0Aforged")]
    [Arguments("/events/%0d%0aforged?token=" + QuerySentinel + "#" + FragmentSentinel,
        "/events/%0d%0aforged")]
    public async Task Navigation_WhenJavaScriptPageViewFails_LogsOnlyFailureMetadata(
        string location, string expectedPath)
    {
        await using var context = new BlazorTestContext();
        var settings = Substitute.For<IPublicExperienceService>();
        settings.GetSettingsAsync().Returns(new PublicExperienceSettingsDto
        {
            AnalyticsProvider = "posthog",
            AnalyticsEnabled = true,
            AnalyticsConsentMode = "pseudonymous",
            AnalyticsTransportMode = "direct",
            AnalyticsConsent = new AnalyticsConsentBootstrapDto { CookieBannerEnabled = false }
        });
        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(Substitute.For<ICookieConsentInterop>());
        context.Services.AddSingleton<CookieConsentStateService>();
        var logger = new PageViewLogger();
        context.Services.AddSingleton<ILogger<AnalyticsInterop>>(logger);
        context.Services.AddScoped<IAnalyticsInterop, AnalyticsInterop>();

        var module = context.JSInterop.SetupModule("/js/analytics-bridge.js");
        module.SetupVoid("initAnalytics", _ => true).SetVoidResult();
        var failure = new JSException(QuerySentinel + "\r\n" + FragmentSentinel);
        // Match any path: neither normalization nor logging is implemented by the test double.
        module.SetupVoid("trackPageView", _ => true).SetException(failure);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();

        // Subscribe before each trigger. Logging is the exact source-to-sink completion signal.
        var initialLog = logger.ReadAsync();
        var cut = context.Render<AnalyticsInitializer>();
        await AssertLogAsync(await initialLog);

        var navigationLog = logger.ReadAsync();
        await cut.InvokeAsync(() => navigation.NavigateTo(location)).WaitAsync(Timeout);
        await AssertLogAsync(await navigationLog);

        await Assert.That(module.Invocations["initAnalytics"].Count).IsEqualTo(1);
        var pageViews = module.Invocations["trackPageView"];
        await Assert.That(pageViews.Count).IsEqualTo(2);
        await Assert.That(pageViews[0].Arguments[0]).IsEqualTo("/");
        await Assert.That(pageViews[1].Arguments[0]).IsEqualTo(expectedPath);
        var properties = (IDictionary<string, object>)pageViews[1].Arguments[1]!;
        await Assert.That(properties["navigation_source"]).IsEqualTo("programmatic_navigation");
        await Assert.That(properties["page_referrer"]).IsEqualTo("/");
    }

    [Test]
    [Arguments("initAnalytics", "initialization")]
    [Arguments("trackEvent", "track")]
    [Arguments("identifyUser", "identify")]
    [Arguments("trackPageView", "page view")]
    [Arguments("optInCapturing", "opt-in")]
    [Arguments("optOutCapturing", "opt-out")]
    public async Task JavaScriptFailures_DoNotExposeInputsOrExceptionPayloads(string operation, string expectedOperation)
    {
        await using var context = new BlazorTestContext();
        var logger = new PageViewLogger();
        var module = context.JSInterop.SetupModule("/js/analytics-bridge.js");
        module.SetupVoid(operation, _ => true)
            .SetException(new JSException(QuerySentinel + "\r\n" + FragmentSentinel));
        await using var interop = new AnalyticsInterop(context.JSInterop.JSRuntime, logger);
        Task<LogEntry> logged = logger.ReadAsync();

        await (operation switch
        {
            "initAnalytics" => interop.InitAsync(QuerySentinel, true, "pseudonymous", "direct", false, null, null),
            "trackEvent" => interop.TrackAsync(QuerySentinel),
            "identifyUser" => interop.IdentifyAsync(QuerySentinel),
            "trackPageView" => interop.PageViewAsync(QuerySentinel),
            "optInCapturing" => interop.OptInCapturingAsync(),
            "optOutCapturing" => interop.OptOutCapturingAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });

        await AssertLogAsync(await logged, expectedOperation);
    }

    private static async Task AssertLogAsync(LogEntry entry, string expectedOperation = "page view")
    {
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Exception).IsNull();
        await Assert.That(entry.Formatted).Contains($"Analytics bridge {expectedOperation} failed");
        await Assert.That(entry.State.Single(pair => pair.Key == "ExceptionType").Value)
            .IsEqualTo(nameof(JSException));
        await Assert.That(entry.State.Any(pair => pair.Key is "PagePath" or "DistinctId" or "EventName")).IsFalse();

        // Inspect both the provider-facing formatted message and every structured field.
        foreach (var text in entry.State.Select(pair => pair.Value?.ToString() ?? string.Empty)
                     .Prepend(entry.Formatted))
        {
            await Assert.That(text.Contains(QuerySentinel, StringComparison.Ordinal)).IsFalse();
            await Assert.That(text.Contains(FragmentSentinel, StringComparison.Ordinal)).IsFalse();
            await Assert.That(text.Contains('\r')).IsFalse();
            await Assert.That(text.Contains('\n')).IsFalse();
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
        string Formatted,
        KeyValuePair<string, object?>[] State);

    private sealed class PageViewLogger : ILogger<AnalyticsInterop>
    {
        private readonly Channel<LogEntry> _entries = Channel.CreateUnbounded<LogEntry>();

        public Task<LogEntry> ReadAsync() => _entries.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToArray();
            if (!_entries.Writer.TryWrite(new LogEntry(logLevel, exception, formatter(state, exception), fields)))
            {
                throw new InvalidOperationException("Log capture channel is closed.");
            }
        }
    }
}
