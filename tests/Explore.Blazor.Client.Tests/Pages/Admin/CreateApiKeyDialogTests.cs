using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Explore.Blazor.Client.Pages.Admin.Components;
using Explore.Blazor.Client.Pages.Admin.Dialogs;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class CreateApiKeyDialogTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task Create_SendsOneOperationKeyAndCanonicalGeneratedPayload()
    {
        using var fixture = new Fixture();
        var credential = NewCanary();
        var id = Guid.CreateVersion7();
        fixture.Transport.CreateReplies.Enqueue((_, _) =>
            Task.FromResult(Issuance(id, credential, "Issued")));
        var (host, _) = await fixture.OpenAsync();
        await FillAsync(host);

        await SubmitButton(host).ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);

        var request = fixture.Transport.Creates.Single();
        await AssertOperationKeyAsync(request);
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.Path).IsEqualTo("/api/externalapikey");
        using var payload = JsonDocument.Parse(request.Body);
        await Assert.That(payload.RootElement.GetProperty("name").GetString()).IsEqualTo("Deployment reader");
        await Assert.That(payload.RootElement.GetProperty("description").GetString()).IsEqualTo("Scheduled reports");
        await Assert.That(payload.RootElement.GetProperty("externalApiKeyOwnerTypeId").GetInt32()).IsEqualTo(1);
        await Assert.That(payload.RootElement.GetProperty("organizationId").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(payload.RootElement.GetProperty("groupId").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(string.Join(",", payload.RootElement.GetProperty("scopes").EnumerateArray()
            .Select(value => value.GetString()))).IsEqualTo("events:read,events:write");
        await Assert.That(payload.RootElement.GetProperty("expiresAt").GetDateTimeOffset())
            .IsEqualTo(new DateTimeOffset(2035, 6, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(host.FindAll("input[readonly]")
            .Count(input => input.GetAttribute("value") == credential)).IsEqualTo(1);
        await Assert.That(fixture.Logs.Text.Contains(credential, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task LostAcknowledgement_RetryKeepsOperationKeyAndFrozenPayload()
    {
        using var fixture = new Fixture();
        var entered = NewSignal();
        var release = NewSignal();
        var exceptionCanary = NewCanary();
        var id = Guid.CreateVersion7();
        fixture.Transport.CreateReplies.Enqueue(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(EventTimeout, cancellationToken);
            throw new HttpRequestException(exceptionCanary);
        });
        fixture.Transport.CreateReplies.Enqueue((_, _) =>
            Task.FromResult(Issuance(id, null, "PreviouslyIssued")));
        var (host, _) = await fixture.OpenAsync();
        await FillAsync(host);
        var pendingRendered = OnRender(host, () =>
            host.FindAll("fieldset[disabled]").Count != 0);

        var firstClick = SubmitButton(host).ClickAsync(new MouseEventArgs());
        try
        {
            await Task.WhenAll(entered.Task, pendingRendered).WaitAsync(EventTimeout);
            await Assert.That(host.FindAll("form input:not([type='hidden']), form textarea, form button")
                .All(IsFrozen)).IsTrue();
            await Assert.That(fixture.Transport.Creates.Count).IsEqualTo(1);
        }
        finally
        {
            release.TrySetResult();
        }
        await firstClick.WaitAsync(EventTimeout);

        await Assert.That(host.FindAll("input:not([type='hidden']), textarea").Count > 0).IsTrue();
        await Assert.That(host.FindAll("form input:not([type='hidden']), form textarea, form button")
            .All(IsFrozen)).IsTrue();
        await Assert.That(host.Markup.Contains(exceptionCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(fixture.Logs.Text.Contains(exceptionCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(SubmitButton(host).HasAttribute("disabled")).IsFalse();

        await SubmitButton(host).ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);

        await Assert.That(fixture.Transport.Creates.Count).IsEqualTo(2);
        var first = fixture.Transport.Creates[0];
        var retry = fixture.Transport.Creates[1];
        await AssertOperationKeyAsync(first);
        await AssertOperationKeyAsync(retry);
        await Assert.That(retry.OperationKeys.Single()).IsEqualTo(first.OperationKeys.Single());
        await Assert.That(retry.Body).IsEqualTo(first.Body);
        await AssertRecoveryAsync(host, id);
    }

    [Test]
    public async Task PreviouslyIssued_ShowsMetadataWithoutSecretCopyOrReplacement()
    {
        using var fixture = new Fixture();
        var id = Guid.CreateVersion7();
        fixture.Transport.CreateReplies.Enqueue((_, _) =>
            Task.FromResult(Issuance(id, null, "PreviouslyIssued")));
        var (host, reference) = await fixture.OpenAsync();
        await FillAsync(host);

        await SubmitButton(host).ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);

        await AssertRecoveryAsync(host, id);
        await Assert.That(fixture.Transport.Creates.Count).IsEqualTo(1);
        var resultTask = reference.Result;
        await host.FindAll("button").Single().ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);
        var result = await resultTask.WaitAsync(EventTimeout);
        await Assert.That(result?.Canceled).IsEqualTo(false);
        await Assert.That(result?.Data).IsEqualTo(true);
        await Assert.That(fixture.Transport.Creates.Count).IsEqualTo(1);
        await Assert.That(fixture.Transport.Mutations).IsEqualTo(1);
    }

    [Test]
    [Arguments(0)]
    [Arguments(200)]
    [Arguments(400)]
    [Arguments(-400)]
    [Arguments(401)]
    [Arguments(403)]
    [Arguments(404)]
    [Arguments(409)]
    [Arguments(500)]
    public async Task UntrustedFailures_NeverRenderOrLogRawBodiesOrExceptions(int status)
    {
        var visibleErrors = new List<string>();
        foreach (var canary in new[] { NewCanary(), NewCanary() })
        {
            using var fixture = new Fixture();
            Task<HttpResponseMessage> Reject(RecordedRequest request, CancellationToken cancellationToken)
            {
                if (status == 0)
                    throw new HttpRequestException(canary, new InvalidOperationException(canary));

                var body = status == -400
                    ? canary
                    : status == 200
                    ? JsonSerializer.Serialize(new { success = false, message = canary, errors = new[] { canary } })
                    : JsonSerializer.Serialize(new
                    {
                        type = "about:blank",
                        title = canary,
                        detail = canary,
                        status,
                        errors = new Dictionary<string, string[]> { ["Name"] = [canary] }
                    });
                return Task.FromResult(JsonResponse((HttpStatusCode)Math.Abs(status), body));
            }
            using var guidanceFixture = new Fixture();
            guidanceFixture.Transport.CreateReplies.Enqueue(Reject);
            var expected = await guidanceFixture.Context.Services.GetRequiredService<IExternalApiKeyService>()
                .CreateApiKeyAsync(new CreateExternalApiKeyDto(), "guidance-comparison");
            fixture.Transport.CreateReplies.Enqueue(Reject);
            var (host, _) = await fixture.OpenAsync();
            await FillAsync(host);

            await SubmitButton(host).ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);

            await Assert.That(fixture.Transport.Creates.Count).IsEqualTo(1);
            await Assert.That(host.Markup.Contains(canary, StringComparison.Ordinal)).IsFalse();
            await Assert.That(fixture.Logs.Text.Contains(canary, StringComparison.Ordinal)).IsFalse();
            var errors = host.FindAll("[role='alert'], [aria-live='assertive']")
                .Where(element => !string.IsNullOrWhiteSpace(element.TextContent)).ToArray();
            await Assert.That(errors.Length > 0).IsTrue();
            await Assert.That(errors.Any(element =>
                element.TextContent.Contains(expected!.Message!, StringComparison.Ordinal))).IsTrue();
            visibleErrors.Add(string.Join("\n", errors.Select(element => element.TextContent.Trim())));
        }

        // Error content must be fixed for this failure class, not derived from either remote body.
        await Assert.That(visibleErrors.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Issued_RevealsOneDynamicCredentialAndDoneReturnsSuccess()
    {
        using var fixture = new Fixture();
        var credential = NewCanary();
        fixture.Transport.CreateReplies.Enqueue((_, _) =>
            Task.FromResult(Issuance(Guid.CreateVersion7(), credential, "Issued")));
        var (host, reference) = await fixture.OpenAsync();
        await FillAsync(host);

        await SubmitButton(host).ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);

        await Assert.That(host.FindAll("form").Count).IsEqualTo(0);
        var secret = host.FindAll("input[readonly]").Single();
        await Assert.That(secret.GetAttribute("value")).IsEqualTo(credential);
        await Assert.That(host.FindComponents<MudTextField<string>>().Count).IsEqualTo(1);
        await Assert.That(host.FindComponents<MudTextField<string>>().Single()
            .Instance.Adornment).IsEqualTo(Adornment.End);
        await Assert.That(host.FindAll("button").Count).IsEqualTo(2);
        await Assert.That(fixture.Transport.Creates.Count).IsEqualTo(1);
        await Assert.That(fixture.Logs.Text.Contains(credential, StringComparison.Ordinal)).IsFalse();
        var resultTask = reference.Result;
        var closed = OnRender(host, () => host.FindComponents<CreateApiKeyDialog>().Count == 0);

        await host.FindComponents<MudButton>().Single().Find("button")
            .ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);

        var result = await resultTask.WaitAsync(EventTimeout);
        await closed.WaitAsync(EventTimeout);
        await Assert.That(result?.Canceled).IsEqualTo(false);
        await Assert.That(result?.Data).IsEqualTo(true);
        await Assert.That(host.Markup.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(fixture.Transport.Mutations).IsEqualTo(1);
    }

    [Test]
    public async Task SeparateIntendedCreations_UseNewOperationKeys()
    {
        using var fixture = new Fixture();
        fixture.Transport.CreateReplies.Enqueue((_, _) =>
            Task.FromResult(Issuance(Guid.CreateVersion7(), NewCanary(), "Issued")));
        fixture.Transport.CreateReplies.Enqueue((_, _) =>
            Task.FromResult(Issuance(Guid.CreateVersion7(), NewCanary(), "Issued")));
        var (host, reference) = await fixture.OpenAsync();
        await FillAsync(host);
        await SubmitButton(host).ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);
        var closed = OnRender(host, () => host.FindComponents<CreateApiKeyDialog>().Count == 0);
        var resultTask = reference.Result;
        await host.FindComponents<MudButton>().Single().Find("button")
            .ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);
        await Task.WhenAll(closed, resultTask).WaitAsync(EventTimeout);

        var reopened = OnRender(host, () => host.FindComponents<CreateApiKeyDialog>().Count == 1);
        await fixture.Context.Services.GetRequiredService<IDialogService>()
            .ShowAsync<CreateApiKeyDialog>(string.Empty, new DialogParameters(), DialogOptionsFactory.Small())
            .WaitAsync(EventTimeout);
        await reopened.WaitAsync(EventTimeout);
        await FillAsync(host);
        await SubmitButton(host).ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);

        await Assert.That(fixture.Transport.Creates.Count).IsEqualTo(2);
        await AssertOperationKeyAsync(fixture.Transport.Creates[0]);
        await AssertOperationKeyAsync(fixture.Transport.Creates[1]);
        await Assert.That(fixture.Transport.Creates[0].OperationKeys.Single() ==
            fixture.Transport.Creates[1].OperationKeys.Single()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Done_RefreshesRealParentOnlyAfterIssuedOrRecoveredCompletion(bool recovered)
    {
        using var fixture = new Fixture();
        var id = Guid.CreateVersion7();
        var credential = recovered ? null : NewCanary();
        fixture.Transport.CreateReplies.Enqueue((_, _) =>
            Task.FromResult(Issuance(id, credential, recovered ? "PreviouslyIssued" : "Issued")));
        fixture.Transport.ListReplies.Enqueue("[]");
        fixture.Transport.ListReplies.Enqueue(JsonSerializer.Serialize(new[]
        {
            new ExternalApiKeyListDto
            {
                Id = id,
                Name = "Deployment reader",
                KeyId = id.ToString("N"),
                ExternalApiKeyOwnerTypeId = 1,
                ExternalApiKeyStatusId = 1,
                Scopes = ["events:read", "events:write"]
            }
        }));
        fixture.Context.Render<MudPopoverProvider>();
        var host = fixture.Context.Render<MudDialogProvider>();
        var parent = fixture.Context.Render<ApiKeysSection>();
        var dialogRendered = OnRender(host, () =>
            host.FindComponents<CreateApiKeyDialog>().Count == 1);

        // The parent's click awaits the real dialog result; do not await it before completing the dialog.
        var openClick = parent.FindComponents<MudButton>().Single().Find("button")
            .ClickAsync(new MouseEventArgs());
        await dialogRendered.WaitAsync(EventTimeout);
        await FillAsync(host);
        await SubmitButton(host).ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);
        if (recovered)
            await AssertRecoveryAsync(host, id);
        await Assert.That(fixture.Transport.ListRequests).IsEqualTo(1);
        var parentUpdated = OnRender(parent, () =>
            parent.FindAll("tbody tr").Any(row => row.TextContent.Contains(id.ToString("N"), StringComparison.Ordinal)));

        await host.FindComponents<MudButton>().Single().Find("button")
            .ClickAsync(new MouseEventArgs()).WaitAsync(EventTimeout);
        await Task.WhenAll(openClick, parentUpdated).WaitAsync(EventTimeout);

        await Assert.That(fixture.Transport.ListRequests).IsEqualTo(2);
        await Assert.That(parent.FindAll("tbody tr").Count).IsEqualTo(1);
        await Assert.That(fixture.Transport.Creates.Count).IsEqualTo(1);
        await Assert.That(fixture.Transport.Mutations).IsEqualTo(1);
        if (credential is not null)
            await Assert.That(parent.Markup.Contains(credential, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task FillAsync(IRenderedComponent<MudDialogProvider> host)
    {
        await host.Find("input[type='text']").ChangeAsync(new ChangeEventArgs { Value = "  Deployment reader  " });
        await host.Find("textarea").ChangeAsync(new ChangeEventArgs { Value = "  Scheduled reports  " });
        foreach (var scope in new[] { "events:write", "events:read" })
        {
            var checkbox = host.FindComponents<MudCheckBox<bool>>()
                .Single(component => component.Instance.Label?.StartsWith(scope + " ", StringComparison.Ordinal) == true);
            await host.InvokeAsync(() => checkbox.Instance.ValueChanged.InvokeAsync(true));
        }
        await host.InvokeAsync(() => host.FindComponent<MudDatePicker>().Instance.DateChanged
            .InvokeAsync(new DateTime(2035, 6, 1)));
    }

    private static IElement SubmitButton(IRenderedComponent<MudDialogProvider> host) =>
        host.FindComponents<MudButton>().Last().Find("button");

    private static bool IsFrozen(IElement element) =>
        element.HasAttribute("disabled") || element.Closest("fieldset[disabled]") is not null ||
        (element.HasAttribute("readonly") && element.LocalName is "input" or "textarea" &&
         element.GetAttribute("type") != "checkbox");

    private static async Task AssertOperationKeyAsync(RecordedRequest request)
    {
        await Assert.That(request.OperationKeys.Length).IsEqualTo(1);
        var key = request.OperationKeys.Single();
        await Assert.That(key.Length is >= 1 and <= 128).IsTrue();
        await Assert.That(key.All(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or ':' or '-'))
            .IsTrue();
    }

    private static async Task AssertRecoveryAsync(IRenderedComponent<MudDialogProvider> host, Guid id)
    {
        await Assert.That(host.FindAll("form").Count).IsEqualTo(0);
        await Assert.That(host.FindAll("input, textarea").Count).IsEqualTo(0);
        await Assert.That(host.FindComponents<MudTextField<string>>().Count).IsEqualTo(0);
        await Assert.That(host.FindComponents<MudAlert>().Any(alert => alert.Instance.Severity == Severity.Error))
            .IsFalse();
        await Assert.That(host.FindAll("[role='status'], [aria-live='polite']")
            .Any(region => !string.IsNullOrWhiteSpace(region.TextContent))).IsTrue();
        await Assert.That(host.FindAll("[role='dialog']")
            .Any(dialog => dialog.TextContent.Contains(id.ToString("N"), StringComparison.Ordinal))).IsTrue();
        await Assert.That(host.FindAll("button").Count).IsEqualTo(1);
        await Assert.That(host.FindAll("button").Single().HasAttribute("disabled")).IsFalse();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task OnRender<T>(IRenderedComponent<T> component, Func<bool> condition)
        where T : IComponent
    {
        var rendered = NewSignal();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (!condition()) return;
            component.OnMarkupUpdated -= handler;
            rendered.TrySetResult();
        };
        component.OnMarkupUpdated += handler;
        return rendered.Task;
    }

    private static string NewCanary() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static HttpResponseMessage Issuance(Guid id, string? credential, string status) =>
        JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            success = true,
            id,
            keyId = id.ToString("N"),
            apiKey = credential,
            disclosureStatus = status
        }));

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Fixture : IDisposable
    {
        public BlazorTestContext Context { get; } = new();
        public RecordingTransport Transport { get; } = new();
        public CapturedLogs Logs { get; } = new();
        private readonly HttpClient _http;

        public Fixture()
        {
            _http = new HttpClient(Transport) { BaseAddress = new Uri("https://event.example/") };
            Context.Services.AddLogging(logging => logging.ClearProviders().AddProvider(Logs));
            Context.Services.AddSingleton<IExternalApiKeyClient>(new ExternalApiKeyClient(_http));
            Context.Services.AddSingleton<IExternalApiKeyService, ExternalApiKeyService>();
        }

        public async Task<(IRenderedComponent<MudDialogProvider> Host, IDialogReference Reference)> OpenAsync()
        {
            Context.Render<MudPopoverProvider>();
            var host = Context.Render<MudDialogProvider>();
            var rendered = OnRender(host, () => host.FindComponents<CreateApiKeyDialog>().Count == 1);
            var reference = await Context.Services.GetRequiredService<IDialogService>()
                .ShowAsync<CreateApiKeyDialog>(string.Empty, new DialogParameters(), DialogOptionsFactory.Small())
                .WaitAsync(EventTimeout);
            await rendered.WaitAsync(EventTimeout);
            return (host, reference);
        }

        public void Dispose()
        {
            Context.Dispose();
            _http.Dispose();
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Body, string[] OperationKeys);

    private sealed class RecordingTransport : HttpMessageHandler
    {
        public Queue<Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>>> CreateReplies { get; } = new();
        public Queue<string> ListReplies { get; } = new();
        public List<RecordedRequest> Creates { get; } = [];
        public int ListRequests { get; private set; }
        public int Mutations { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Get && path == "/api/externalapikey")
            {
                ListRequests++;
                return JsonResponse(HttpStatusCode.OK, ListReplies.Dequeue());
            }
            Mutations++;
            var recorded = new RecordedRequest(
                request.Method,
                path,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.ToArray() : []);
            Creates.Add(recorded);
            if (request.Method != HttpMethod.Post || path != "/api/externalapikey")
                throw new InvalidOperationException("Unexpected mutation at the HTTP boundary.");
            if (CreateReplies.Count == 0)
                throw new InvalidOperationException("Unexpected replacement creation at the HTTP boundary.");
            return await CreateReplies.Dequeue()(recorded, cancellationToken);
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();
        public string Text => string.Join("\n", _entries);
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(_entries);
        public void Dispose() { }

        private sealed class CaptureLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue(formatter(state, exception));
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                    foreach (var value in values) entries.Enqueue(value.Value?.ToString() ?? string.Empty);
                if (exception is not null) entries.Enqueue(exception.ToString());
            }
        }
    }
}
