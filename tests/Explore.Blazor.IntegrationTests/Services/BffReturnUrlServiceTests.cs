using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffReturnUrlServiceTests
{
    private readonly BffReturnUrlService _service = new();

    [Arguments((string?)null)]
    [Arguments("")]
    [Arguments(" \t\r\n")]
    [Test]
    public async Task GetSafeReturnUrl_Blank_ReturnsRoot(string? returnUrl)
    {
        var context = CreateContext(returnUrl);

        var result = _service.GetSafeReturnUrl(context, NullLogger.Instance);

        await Assert.That(result).IsEqualTo("/");
    }

    [Arguments("/")]
    [Arguments("/admin/tenant/settings")]
    [Arguments("/dashboard?tab=one two&next=/settings#profile")]
    [Arguments("/search?q=https%3A%2F%2Fexample.com")]
    [Arguments("/search?q=https://example.com")]
    [Test]
    public async Task GetSafeReturnUrl_LocalPath_ReturnsPath(string returnUrl)
    {
        var context = CreateContext(returnUrl);

        var result = _service.GetSafeReturnUrl(context, NullLogger.Instance);

        await Assert.That(result).IsEqualTo(returnUrl);
    }

    [Arguments("https://evil.example")]
    [Arguments("http://evil.example")]
    [Arguments("//evil.example")]
    [Arguments("/\\evil")]
    [Arguments("javascript:alert(1)")]
    [Arguments("dashboard")]
    [Arguments("~/dashboard")]
    [Arguments("/\t/evil.example")]
    [Arguments("/\r/evil.example")]
    [Arguments("/\n/evil.example")]
    [Arguments("/\t\\evil.example")]
    [Arguments("/\0/evil.example")]
    [Arguments("/\u001f/evil.example")]
    [Arguments("/\u007f/evil.example")]
    [Arguments("/\u0085/evil.example")]
    [Arguments("/dashboard\t")]
    [Arguments("/dashboard?tab=one\ntwo")]
    [Test]
    public async Task GetSafeReturnUrl_UnsafeValue_ReturnsRoot(string returnUrl)
    {
        var context = CreateContext(returnUrl);

        var result = _service.GetSafeReturnUrl(context, NullLogger.Instance);

        await Assert.That(result).IsEqualTo("/");
    }

    [Test]
    public async Task GetSafeReturnUrl_RejectedValue_DoesNotLogRawInput()
    {
        const string rejectedUrl = "https://evil.example/rejected-input-sentinel";
        var context = CreateContext(rejectedUrl);
        var logger = new RecordingLogger();

        var result = _service.GetSafeReturnUrl(context, logger);

        await Assert.That(result).IsEqualTo("/");
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        var entry = logger.Entries.Single();
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Message).DoesNotContain("rejected-input-sentinel");
        foreach (var field in entry.Fields)
        {
            await Assert.That(field.Value?.ToString() ?? string.Empty).DoesNotContain("rejected-input-sentinel");
        }
    }

    [Test]
    public async Task BuildLoginRedirectUrl_EncodesReturnUrlAndProvider()
    {
        var result = _service.BuildLoginRedirectUrl("/admin/tenant settings", "key cloak");

        await Assert.That(result).IsEqualTo("/login?returnUrl=%2Fadmin%2Ftenant%20settings&provider=key%20cloak");
        await Assert.That(result).DoesNotContain("challengeError=1");
    }

    [Test]
    public async Task BuildLoginRedirectUrl_WithChallengeError_PreservesExistingFlag()
    {
        var result = _service.BuildLoginRedirectUrl("/setup", challengeError: true);

        await Assert.That(result).IsEqualTo("/login?returnUrl=%2Fsetup&challengeError=1");
    }

    [Test]
    public async Task BuildChallengeRedirectUrl_WithProvider_EncodesProviderAndReturnUrl()
    {
        var result = _service.BuildChallengeRedirectUrl("/dashboard?tab=one two", "key cloak");

        await Assert.That(result).IsEqualTo("/auth/challenge?provider=key%20cloak&returnUrl=%2Fdashboard%3Ftab%3Done%20two");
    }

    [Test]
    public async Task BuildChallengeRedirectUrl_WithoutProvider_ReturnsLoginRedirect()
    {
        var result = _service.BuildChallengeRedirectUrl("/dashboard", provider: null);

        await Assert.That(result).IsEqualTo("/login?returnUrl=%2Fdashboard");
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, KeyValuePair<string, object?>[] Fields)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> structuredState
                ? structuredState.ToArray()
                : [];
            Entries.Add((logLevel, formatter(state, exception), fields));
        }
    }

    private static DefaultHttpContext CreateContext(string? returnUrl)
    {
        var context = new DefaultHttpContext();
        if (returnUrl is not null)
        {
            context.Request.QueryString = QueryString.Create("returnUrl", returnUrl);
        }

        return context;
    }
}
