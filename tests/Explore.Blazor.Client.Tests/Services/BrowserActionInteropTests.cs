using Explore.Blazor.Client.Services;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class BrowserActionInteropTests
{
    [Test]
    public async Task ShareAsync_ImportsBrowserActionsModuleAndPassesStructuredArguments()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Module.Results["share"] = true;
        await using var interop = CreateInterop(jsRuntime);

        var result = await interop.ShareAsync("Dangerous <script>alert(1)</script>", "https://example.test/events/123");

        var invocation = jsRuntime.Module.SingleInvocation("share");
        await Assert.That(result).IsTrue();
        await Assert.That(jsRuntime.ImportedModulePath).IsEqualTo("/js/browser-actions.js");
        await Assert.That(invocation.Arguments[0]).IsEqualTo("Dangerous <script>alert(1)</script>");
        await Assert.That(invocation.Arguments[1]).IsEqualTo("https://example.test/events/123");
    }

    [Test]
    public async Task CopyTextAsync_UsesBrowserActionsModuleInsteadOfGlobalClipboardIdentifier()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Module.Results["copyText"] = true;
        await using var interop = CreateInterop(jsRuntime);

        var result = await interop.CopyTextAsync("https://example.test/events/123");

        var invocation = jsRuntime.Module.SingleInvocation("copyText");
        await Assert.That(result).IsTrue();
        await Assert.That(invocation.Identifier).IsEqualTo("copyText");
        await Assert.That(invocation.Arguments[0]).IsEqualTo("https://example.test/events/123");
    }

    [Test]
    public async Task DownloadBase64FileAsync_UsesModuleDownloadWithSanitizedBoundaryArguments()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Module.Results["downloadBase64File"] = true;
        await using var interop = CreateInterop(jsRuntime);

        var result = await interop.DownloadBase64FileAsync("QkVHSU46VkNBTEVOREFS", "event.ics", "text/calendar");

        var invocation = jsRuntime.Module.SingleInvocation("downloadBase64File");
        await Assert.That(result).IsTrue();
        await Assert.That(invocation.Arguments[0]).IsEqualTo("QkVHSU46VkNBTEVOREFS");
        await Assert.That(invocation.Arguments[1]).IsEqualTo("event.ics");
        await Assert.That(invocation.Arguments[2]).IsEqualTo("text/calendar");
    }

    [Test]
    public async Task ShareAsync_WhenJavaScriptInteropUnavailable_ReturnsFalse()
    {
        await using var interop = CreateInterop(new ThrowingJsRuntime(new InvalidOperationException(
            "JavaScript interop calls cannot be issued at this time.")));

        var result = await interop.ShareAsync("Event", "https://example.test/events/123");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task BrowserActions_WithBlankRequiredValues_ReturnFalseWithoutImportingModule()
    {
        var jsRuntime = new RecordingJsRuntime();
        await using var interop = CreateInterop(jsRuntime);

        var shareResult = await interop.ShareAsync("Event", "");
        var copyResult = await interop.CopyTextAsync("");
        var scrollResult = await interop.ScrollToElementByIdAsync("");
        var downloadResult = await interop.DownloadBase64FileAsync("", "event.ics", "text/calendar");

        await Assert.That(shareResult).IsFalse();
        await Assert.That(copyResult).IsFalse();
        await Assert.That(scrollResult).IsFalse();
        await Assert.That(downloadResult).IsFalse();
        await Assert.That(jsRuntime.ImportedModulePath).IsNull();
    }

    [Test]
    public async Task FocusOpenerAndCloseAsync_UsesTypedBrowserModuleAction()
    {
        var jsRuntime = new RecordingJsRuntime();
        jsRuntime.Module.Results["focusOpenerAndClose"] = true;
        await using var interop = CreateInterop(jsRuntime);

        var result = await interop.FocusOpenerAndCloseAsync();

        await Assert.That(result).IsTrue();
        await Assert.That(jsRuntime.Module.SingleInvocation("focusOpenerAndClose").Arguments).IsEmpty();
    }


    private static BrowserActionInterop CreateInterop(IJSRuntime jsRuntime)
        => new(jsRuntime, Substitute.For<ILogger<BrowserActionInterop>>());

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public RecordingJsModule Module { get; } = new();

        public string? ImportedModulePath { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier != "import")
            {
                throw new InvalidOperationException($"Unexpected JS runtime invocation '{identifier}'.");
            }

            ImportedModulePath = args?.FirstOrDefault() as string;
            return ValueTask.FromResult((TValue)(object)Module);
        }
    }

    private sealed class RecordingJsModule : IJSObjectReference
    {
        private readonly List<JsInvocation> _invocations = [];

        public Dictionary<string, object?> Results { get; } = [];

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            _invocations.Add(new JsInvocation(identifier, args ?? []));

            if (Results.TryGetValue(identifier, out var result))
            {
                return ValueTask.FromResult((TValue)result!);
            }

            if (typeof(TValue) == typeof(bool))
            {
                return ValueTask.FromResult((TValue)(object)false);
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public JsInvocation SingleInvocation(string identifier)
            => _invocations.Single(invocation => invocation.Identifier == identifier);
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

    private sealed record JsInvocation(string Identifier, object?[] Arguments);
}
