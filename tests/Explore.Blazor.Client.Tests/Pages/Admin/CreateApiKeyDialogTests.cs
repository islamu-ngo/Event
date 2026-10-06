using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Explore.Blazor.Client.Pages.Admin.Components;
using Explore.Blazor.Client.Pages.Admin.Dialogs;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

/// <summary>
/// Exercises one-time API key disclosure and metadata-only recovery through real dialog components,
/// the service, and the generated HTTP client. Event subscriptions bound async rendering without
/// sleeps; dynamic canaries distinguish intentional disclosure from unsafe remote diagnostics.
/// </summary>
public sealed class CreateApiKeyDialogTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Malformed acknowledgements cannot become success or leak remote material through diagnostics.</summary>
    [Test]
    [Arguments("missing-status")]
    [Arguments("missing-api-key")]
    [Arguments("null-status")]
    [Arguments("unknown-status")]
    [Arguments("numeric-status")]
    [Arguments("numeric-string-status")]
    [Arguments("empty-id")]
    [Arguments("empty-key-id")]
    [Arguments("recovery-with-secret")]
    [Arguments("issuance-without-secret")]
    public async Task MalformedSuccessIsRejectedByTheGeneratedClientBoundary(string variation)
    {
        using var fixture = new Fixture();
        string credential = NewCanary();
        var wire = new JsonObject
        {
            ["id"] = Guid.CreateVersion7().ToString(),
            ["keyId"] = "public-key-id",
            ["apiKey"] = credential,
            ["disclosureStatus"] = "Issued"
        };
        switch (variation)
        {
            case "missing-status": wire.Remove("disclosureStatus"); break;
            case "missing-api-key": wire.Remove("apiKey"); break;
            case "null-status": wire["disclosureStatus"] = null; break;
            case "unknown-status": wire["disclosureStatus"] = "Unknown"; break;
            case "numeric-status": wire["disclosureStatus"] = 0; break;
            case "numeric-string-status": wire["disclosureStatus"] = "0"; break;
            case "empty-id": wire["id"] = Guid.Empty.ToString(); break;
            case "empty-key-id": wire["keyId"] = ""; break;
            case "recovery-with-secret": wire["disclosureStatus"] = "PreviouslyIssued"; break;
            case "issuance-without-secret": wire["apiKey"] = null; break;
        }
        fixture.Transport.CreateReplies.Enqueue((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, wire.ToJsonString())));

        var response = await fixture.Context.Services.GetRequiredService<IExternalApiKeyService>()
            .CreateApiKeyAsync(new CreateExternalApiKeyDto(), Guid.CreateVersion7().ToString("N"));

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Issue is null).IsTrue();
        await Assert.That(response.ErrorMessage is not null).IsTrue();
        await Assert.That(fixture.Logs.Text.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(response.ToString().Contains(credential, StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// Submits whitespace-padded fields and reverse-selected scopes through the dialog and generated
    /// client. The recorded POST must contain one bounded operation key and canonical payload,
    /// while the first issued credential appears once in a readonly field and never in logs.
    /// </summary>
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

    /// <summary>
    /// Holds the first transport request with explicit entry/release signals while a subscribed
    /// render event observes frozen controls. After acknowledgement loss, editing remains frozen
    /// but retry is enabled; retry must send the identical operation key and byte-identical payload.
    /// Metadata-only recovery must not replace the key or expose the injected exception canary.
    /// </summary>
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

    /// <summary>
    /// A recovered issuance shows safe identity and a single completion action, with no credential
    /// input or copy affordance. Completing the real dialog returns success without another mutation.
    /// </summary>
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

    /// <summary>
    /// Sends two different canaries through each transport/HTTP failure class, including malformed
    /// success and non-JSON bodies. Neither markup nor logs may contain remote material; accessible
    /// errors must use the service's bounded safe guidance and remain identical for that class,
    /// rather than deriving text from response bodies or exception messages.
    /// </summary>
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
            /// <summary>Feeds secret canaries through transport faults and untrusted HTTP payloads without bypassing client deserialization.</summary>
            Task<HttpResponseMessage> Reject(RecordedRequest request, CancellationToken cancellationToken)
            {
                if (status == 0)
                    throw new HttpRequestException(canary, new InvalidOperationException(canary));

                var body = status switch
                {
                    -400 => canary,
                    200 => JsonSerializer.Serialize(new { success = false, message = canary, errors = new[] { canary } }),
                    _ => JsonSerializer.Serialize(new
                    {
                        type = "about:blank",
                        title = canary,
                        detail = canary,
                        status,
                        errors = new Dictionary<string, string[]> { ["Name"] = [canary] }
                    })
                };
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
                element.TextContent.Contains(expected.ErrorMessage!, StringComparison.Ordinal))).IsTrue();
            visibleErrors.Add(string.Join("\n", errors.Select(element => element.TextContent.Trim())));
        }

        // Error content must be fixed for this failure class, not derived from either remote body.
        await Assert.That(visibleErrors.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    /// <summary>
    /// First issuance replaces the form with one readonly credential field and copy affordance.
    /// Subscribing to dialog closure before Done proves successful completion removes the credential
    /// from rendered markup without logging it or sending another mutation.
    /// </summary>
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

    /// <summary>
    /// Closes a completed dialog and opens another, awaiting pre-registered render signals for
    /// both transitions. Distinct intended creations must allocate different operation keys rather
    /// than accidentally recovering the first issuance.
    /// </summary>
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

    /// <summary>
    /// Uses the actual parent section and dialog result instead of a mocked completion callback.
    /// Issuance or recovery alone must not reload the list; Done resolves the awaiting parent click
    /// and a subscribed row-render signal, requiring exactly one refresh and no second mutation.
    /// The parent list must never inherit a newly disclosed credential.
    /// </summary>
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

    /// <summary>
    /// Drives component change events with padded text, reverse scope order, and a fixed future
    /// expiry, making normalization observable in the generated client's recorded request.
    /// </summary>
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

    /// <summary>Locates the form's final MudButton, which becomes the retry action after an uncertain response.</summary>
    private static IElement SubmitButton(IRenderedComponent<MudDialogProvider> host) =>
        host.FindComponents<MudButton>().Last().Find("button");

    /// <summary>
    /// Recognizes direct and inherited disabling or readonly text entry as frozen policy inputs.
    /// Readonly does not freeze checkboxes, whose values could still change the intended operation.
    /// </summary>
    private static bool IsFrozen(IElement element) =>
        element.HasAttribute("disabled") || element.Closest("fieldset[disabled]") is not null ||
        (element.HasAttribute("readonly") && element.LocalName is "input" or "textarea" &&
         element.GetAttribute("type") != "checkbox");

    /// <summary>
    /// Requires exactly one operation-key header using the bounded ingress-safe character set,
    /// preventing ambiguous headers or an unbounded retry identity at the HTTP boundary.
    /// </summary>
    private static async Task AssertOperationKeyAsync(RecordedRequest request)
    {
        await Assert.That(request.OperationKeys.Length).IsEqualTo(1);
        var key = request.OperationKeys.Single();
        await Assert.That(key.Length is >= 1 and <= 128).IsTrue();
        await Assert.That(key.All(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or ':' or '-'))
            .IsTrue();
    }

    /// <summary>
    /// Requires accessible metadata-only completion with the committed public ID, no editable or
    /// secret-bearing fields, no copy affordance, and one enabled Done action rather than a
    /// replacement-creation path or an error state.
    /// </summary>
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

    /// <summary>
    /// Creates an explicit completion signal with asynchronous continuations so releasing a
    /// transport/render gate does not run awaiting test code inline on the notifying callback.
    /// </summary>
    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Subscribes before the triggering action and completes on the first markup update satisfying
    /// the requested state, then unsubscribes. Callers bound the returned task with EventTimeout,
    /// so render synchronization neither polls nor relies on elapsed-time sleeps.
    /// </summary>
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

    /// <summary>
    /// Produces dynamic opaque material for disclosure and diagnostic-leak assertions without
    /// embedding a reusable credential or binding expectations to a fixed secret string.
    /// </summary>
    private static string NewCanary() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Builds success JSON containing every generated-client required field, including apiKey
    /// when null for recovery. The generated deserializer and service still ingest the response;
    /// the fixture does not bypass wire validation with a preconstructed success result.
    /// </summary>
    private static HttpResponseMessage Issuance(Guid id, string? credential, string status) =>
        JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id,
            keyId = id.ToString("N"),
            apiKey = credential,
            disclosureStatus = status
        }));

    /// <summary>Provides an actual JSON HTTP response body for generated-client deserialization and failure ingestion.</summary>
    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Hosts real Blazor services, the generated API key client, and the issuance service over a
    /// recording HTTP transport. Captured logs include structured values and exceptions so leak
    /// assertions cover more than visible UI text.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        public BlazorTestContext Context { get; } = new();
        public RecordingTransport Transport { get; } = new();
        public CapturedLogs Logs { get; } = new();
        private readonly HttpClient _http;

        /// <summary>
        /// Replaces only the HTTP transport and logger sink, retaining real generated-client
        /// required-field ingestion, service guidance classification, and dialog lifecycle behavior.
        /// </summary>
        public Fixture()
        {
            _http = new HttpClient(Transport) { BaseAddress = new Uri("https://event.example/") };
            Context.Services.AddLogging(logging => logging.ClearProviders().AddProvider(Logs));
            Context.Services.AddSingleton<IExternalApiKeyClient>(new ExternalApiKeyClient(_http));
            Context.Services.AddSingleton<IExternalApiKeyService, ExternalApiKeyService>();
        }

        /// <summary>
        /// Renders the MudBlazor providers and registers a dialog-render signal before opening.
        /// Returns both the rendered host and real result reference after bounded event completion.
        /// </summary>
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

        /// <summary>Disposes rendered components and the HTTP client, releasing this fixture's transport ownership.</summary>
        public void Dispose()
        {
            Context.Dispose();
            _http.Dispose();
        }
    }

    /// <summary>
    /// Captures the exact HTTP intention for canonical payload and frozen-retry comparisons,
    /// including all operation-key header values rather than a normalized single value.
    /// </summary>
    private sealed record RecordedRequest(HttpMethod Method, string Path, string Body, string[] OperationKeys);

    /// <summary>
    /// Queues deterministic wire replies and counts actual list reads and mutations.
    /// Unexpected routes or replacement creations fail at the HTTP boundary, while response
    /// ingestion continues through the real generated client.
    /// </summary>
    private sealed class RecordingTransport : HttpMessageHandler
    {
        public Queue<Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>>> CreateReplies { get; } = new();
        public Queue<string> ListReplies { get; } = new();
        public List<RecordedRequest> Creates { get; } = [];
        public int ListRequests { get; private set; }
        public int Mutations { get; private set; }

        /// <summary>
        /// Records request bytes and operation headers before consuming one queued creation reply;
        /// forwards cancellation to body reading and reply gates. Only the expected list GET and
        /// creation POST are accepted, so hidden replacement mutations cannot pass unnoticed.
        /// </summary>
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

    /// <summary>
    /// Collects concurrent log output for boolean canary-absence assertions without requiring
    /// raw secret-bearing text to be printed when a leak assertion fails.
    /// </summary>
    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();
        public string Text => string.Join("\n", _entries);
        /// <summary>Shares the same capture queue across logger categories so no category escapes the leak check.</summary>
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(_entries);
        /// <summary>Requires no sink cleanup because entries are owned by this in-memory fixture.</summary>
        public void Dispose() { }

        /// <summary>Captures formatted messages, structured values, and exception text independently for leak detection.</summary>
        private sealed class CaptureLogger(ConcurrentQueue<string> entries) : ILogger
        {
            /// <summary>Leaves scopes unrecorded; the leakage contract here observes emitted log entries and exceptions.</summary>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            /// <summary>Enables every level so the fixture cannot hide unsafe output through log filtering.</summary>
            public bool IsEnabled(LogLevel logLevel) => true;
            /// <summary>
            /// Retains both rendered text and raw structured-value representations plus exceptions,
            /// detecting material that a safe message template alone might conceal.
            /// </summary>
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
