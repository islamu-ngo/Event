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
    public async Task Navigation_WhenJavaScriptPageViewFails_LogsOnlyEscapedPath(
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
        var failure = new JSException("pageview-boundary-unavailable");
        // Match any path: neither normalization nor logging is implemented by the test double.
        module.SetupVoid("trackPageView", _ => true).SetException(failure);
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();

        // Subscribe before each trigger. Logging is the exact source-to-sink completion signal.
        var initialLog = logger.ReadAsync();
        var cut = context.Render<AnalyticsInitializer>();
        await AssertLogAsync(await initialLog, "/", failure);

        var navigationLog = logger.ReadAsync();
        await cut.InvokeAsync(() => navigation.NavigateTo(location)).WaitAsync(Timeout);
        await AssertLogAsync(await navigationLog, expectedPath, failure);

        await Assert.That(module.Invocations["initAnalytics"].Count).IsEqualTo(1);
        var pageViews = module.Invocations["trackPageView"];
        await Assert.That(pageViews.Count).IsEqualTo(2);
        await Assert.That(pageViews[0].Arguments[0]).IsEqualTo("/");
        await Assert.That(pageViews[1].Arguments[0]).IsEqualTo(expectedPath);
        var properties = (IDictionary<string, object>)pageViews[1].Arguments[1]!;
        await Assert.That(properties["navigation_source"]).IsEqualTo("programmatic_navigation");
        await Assert.That(properties["page_referrer"]).IsEqualTo("/");
    }

    private static async Task AssertLogAsync(LogEntry entry, string expectedPath, JSException failure)
    {
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(ReferenceEquals(entry.Exception, failure)).IsTrue();
        await Assert.That(entry.State.Single(pair => pair.Key == "PagePath").Value)
            .IsEqualTo(expectedPath);
        await Assert.That(entry.Formatted.Contains(expectedPath, StringComparison.Ordinal)).IsTrue();

        // Inspect both the provider-facing formatted message and every structured field.
        // Exact PagePath equality above also rejects decoding, truncation, or dropped logging.
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
