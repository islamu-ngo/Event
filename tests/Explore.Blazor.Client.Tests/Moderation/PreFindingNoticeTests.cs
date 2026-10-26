using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Pages.Moderation;
using Explore.Blazor.Client.Services.Accessibility;
using Explore.Blazor.Client.Services.Moderation;
using System.Net;

namespace Explore.Blazor.Client.Tests.Moderation;

public sealed class PreFindingNoticeTests
{
    [Test]
    public async Task RecipientRendersSafeTextConnectedInputAndHalActionsWithoutAcknowledging()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        using var http = Register(context, transport);
        var component = await RenderAsync(context, transport);
        await Assert.That(component.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(component.FindAll("[data-action='acknowledge']").Count).IsEqualTo(1);
        await Assert.That(component.FindAll("[data-action='submit-response']").Count).IsEqualTo(1);
        var input = component.Find("textarea");
        await Assert.That(component.Find("label").GetAttribute("for")).IsEqualTo(input.Id);
        await Assert.That(input.GetAttribute("aria-describedby"))
            .IsEqualTo("pre-finding-response-help pre-finding-response-error");
        await Assert.That(component.Markup.Contains("Never display this")).IsFalse();
        await Assert.That(transport.Requests.Single().Method).IsEqualTo("GET");
    }

    [Test]
    public async Task MissingHalActionsRemainHiddenEvenWithoutLocalReceipt()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            PreFindingNoticeTransport.Notice(actions: false)));
        using var http = Register(context, transport);
        var component = await RenderAsync(context, transport);
        await Assert.That(component.FindAll("[data-action='acknowledge'], [data-action='submit-response'], textarea")).IsEmpty();
        await Assert.That(component.FindAll("dd").First().TextContent).IsEqualTo("Not acknowledged");
    }

    [Test]
    public async Task ReviewerResponseShowsNoRecipientControls()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            PreFindingNoticeTransport.Notice(responded: true)));
        using var http = Register(context, transport);
        var component = await RenderAsync(context, transport, review: true);
        await Assert.That(component.FindAll("textarea, [data-action='acknowledge'], [data-action='submit-response']")).IsEmpty();
        await Assert.That(component.FindAll("p").Any(node => node.TextContent == "My permitted response")).IsTrue();
        await Assert.That(transport.Requests.Single().Path.Contains("reviewResponse=true")).IsTrue();
    }

    [Test]
    public async Task AcknowledgementDoesNotInventReceiptWhileFreshReadIsPending()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        using var http = Register(context, transport);
        var component = await RenderAsync(context, transport);
        var freshRead = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Post)
                return Task.FromResult(PreFindingNoticeTransport.Json("""{"success":true}"""));
            readStarted.TrySetResult();
            return freshRead.Task;
        };
        var rendered = NextRender(component, () => component.FindAll("dd").FirstOrDefault()?.TextContent == "Acknowledged");
        var cleared = NextRender(component, () => component.FindAll("dd").Count == 0);
        var click = component.Find("[data-action='acknowledge']").ClickAsync(new MouseEventArgs());
        await Task.WhenAll(readStarted.Task, cleared).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(component.FindAll("dd")).IsEmpty();
        freshRead.SetResult(PreFindingNoticeTransport.Json(
            PreFindingNoticeTransport.Notice(actions: false, acknowledged: true)));
        await Task.WhenAll(click, rendered).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(transport.Requests.Count).IsEqualTo(3);
    }

    [Test]
    public async Task FailedResponseClearsPrivateContentAndDoesNotRetry()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        using var http = Register(context, transport);
        var component = await RenderAsync(context, transport);
        await component.Find("textarea").InputAsync(new ChangeEventArgs { Value = "My confidential account" });
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            """{"title":"Must not surface diagnostics"}""", HttpStatusCode.Conflict));
        await component.Find("form").SubmitAsync();
        await Assert.That(component.FindAll("textarea, dl")).IsEmpty();
        await Assert.That(component.FindAll("[role='alert']").Count).IsEqualTo(1);
        await Assert.That(component.Markup.Contains("My confidential account")).IsFalse();
        await Assert.That(component.Markup.Contains("Must not surface diagnostics")).IsFalse();
        var body = PreFindingNoticeTransport.Body(transport.Requests[1].Body!);
        await Assert.That(body.GetProperty("statement").GetString()).IsEqualTo("My confidential account");
        await Assert.That(body.GetProperty("operationId").GetGuid().Version).IsEqualTo(7);
        await Assert.That(transport.Requests.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResponseValidationFocusesConnectedErrorWithoutLosingDraftOrWriting(bool exceedsByteLimit)
    {
        using var context = new BlazorTestContext();
        context.Services.AddScoped<IAccessibilityFocusService, AccessibilityFocusService>();
        var focus = context.JSInterop.SetupModule("/js/accessibility.js");
        focus.SetupVoid("setFocusById", _ => true).SetVoidResult();
        using var transport = new PreFindingNoticeTransport();
        using var http = Register(context, transport);
        var component = await RenderAsync(context, transport);
        var statement = exceedsByteLimit ? new string('\u0627', 32769) : "   ";
        await component.Find("textarea").InputAsync(new ChangeEventArgs { Value = statement });
        await component.Find("form").SubmitAsync();
        await Assert.That(component.Find("textarea").GetAttribute("aria-invalid")).IsEqualTo("true");
        await Assert.That(component.Find("#pre-finding-response-error").TextContent).IsNotEmpty();
        await Assert.That(((AngleSharp.Html.Dom.IHtmlTextAreaElement)component.Find("textarea")).Value).IsEqualTo(statement);
        await Assert.That((string)focus.Invocations.Last(invocation => invocation.Identifier == "setFocusById")
            .Arguments[0]!).IsEqualTo("pre-finding-response");
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SubmittedResponseComesFromFreshOwnerViewAndRemovesActions()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        using var http = Register(context, transport);
        var component = await RenderAsync(context, transport);
        await component.Find("textarea").InputAsync(new ChangeEventArgs { Value = "My submitted account" });
        transport.Respond = (request, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            request.Method == HttpMethod.Post ? """{"success":true}""" :
            PreFindingNoticeTransport.Notice(actions: false, responded: true)));
        await component.Find("form").SubmitAsync();
        await Assert.That(component.FindAll("textarea, [data-action='acknowledge'], [data-action='submit-response']")).IsEmpty();
        await Assert.That(component.FindAll("p").Any(node => node.TextContent == "My permitted response")).IsTrue();
        await Assert.That(transport.Requests.Select(request => request.Method).SequenceEqual(["GET", "POST", "GET"])).IsTrue();
    }

    [Test]
    public async Task RouteChangeCancelsOldRequestAndLateReplyCannotReplaceCurrentNotice()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        var oldReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Respond = (_, token) =>
        {
            token.Register(() => cancelled.TrySetResult());
            // Deliberately ignores cancellation to prove stale-response exclusion independently.
            return oldReply.Task;
        };
        using var http = Register(context, transport);
        var component = context.Render<PreFindingNotice>(parameters => parameters
            .Add(page => page.NoticeId, PreFindingNoticeTransport.NoticeId));
        var nextId = Guid.CreateVersion7();
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            PreFindingNoticeTransport.Notice(nextId).Replace("Approved allegation", "New safe allegation")));
        await component.InvokeAsync(() => component.Render(parameters => parameters.Add(page => page.NoticeId, nextId)));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var oldFinished = NextRender(component, () => component.Find("section").GetAttribute("aria-busy") == "false");
        oldReply.SetResult(PreFindingNoticeTransport.Json(PreFindingNoticeTransport.Notice()));
        await oldFinished.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(component.FindAll("p").Any(node => node.TextContent == "New safe allegation")).IsTrue();
        await Assert.That(component.FindAll("p").Any(node => node.TextContent == "Approved allegation")).IsFalse();
    }

    [Test]
    public async Task DisposalCancelsOutstandingPrivateRead()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Respond = (_, token) =>
        {
            token.Register(() =>
            {
                cancelled.TrySetResult();
                pending.TrySetCanceled(token);
            });
            return pending.Task;
        };
        using var http = Register(context, transport);
        var component = context.Render<PreFindingNotice>(parameters => parameters
            .Add(page => page.NoticeId, PreFindingNoticeTransport.NoticeId));
        component.Dispose();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExpiryClearsNoticeAndDraftWithoutRequestingAnotherRead()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        var clock = new NoticeExpiryClock();
        using var http = Register(context, transport, clock);
        var component = await RenderAsync(context, transport);
        await component.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Private unsent draft" });
        var expired = NextRender(component, () => component.FindAll("textarea, dl").Count == 0);
        clock.Expire();
        await expired.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(component.FindAll("textarea, dl, [data-action='reload']")).IsEmpty();
        await Assert.That(component.Markup.Contains("Private unsent draft")).IsFalse();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task LongFiniteRetentionUsesBoundedTimerAndStillClearsAtOriginalExpiry()
    {
        using var context = new BlazorTestContext();
        using var transport = new PreFindingNoticeTransport();
        transport.Respond = (_, _) => Task.FromResult(PreFindingNoticeTransport.Json(
            PreFindingNoticeTransport.Notice(expires: PreFindingNoticeTransport.Now.AddDays(100))));
        var clock = new NoticeExpiryClock();
        using var http = Register(context, transport, clock);
        var component = await RenderAsync(context, transport);
        await Assert.That(clock.DueTime.TotalMilliseconds).IsLessThanOrEqualTo(uint.MaxValue - 1d);
        var expired = NextRender(component, () => component.FindAll("textarea, dl").Count == 0);
        clock.Advance(TimeSpan.FromDays(100));
        await expired.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(component.FindAll("textarea, dl")).IsEmpty();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    private static HttpClient Register(BlazorTestContext context, PreFindingNoticeTransport transport,
        TimeProvider? clock = null)
    {
        var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example/") };
        clock ??= new FixedTimeProvider(PreFindingNoticeTransport.Now);
        context.Services.AddSingleton(clock);
        context.Services.AddSingleton<Explore.Blazor.Client.Contracts.Services.IPreFindingNoticeService>(new PreFindingNoticeService(new ModerationPreFindingNoticesClient(http), clock));
        return http;
    }

    private static async Task<IRenderedComponent<PreFindingNotice>> RenderAsync(
        BlazorTestContext context, PreFindingNoticeTransport transport, bool review = false)
    {
        var respond = transport.Respond;
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<(HttpRequestMessage Request, CancellationToken Token)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Respond = (request, token) =>
        {
            started.TrySetResult((request, token));
            return release.Task;
        };
        var component = context.Render<PreFindingNotice>(parameters => parameters
            .Add(page => page.NoticeId, PreFindingNoticeTransport.NoticeId)
            .Add(page => page.ReviewResponse, review));
        var rendered = NextRender(component, () => component.Find("section").GetAttribute("aria-busy") == "false");
        var pending = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        transport.Respond = respond;
        release.SetResult(await respond(pending.Request, pending.Token));
        await rendered.WaitAsync(TimeSpan.FromSeconds(5));
        return component;
    }

    private static Task NextRender(IRenderedComponent<PreFindingNotice> component, Func<bool> condition)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (!condition())
                return;
            component.OnAfterRender -= handler;
            signal.TrySetResult();
        };
        component.OnAfterRender += handler;
        return signal.Task;
    }

    private sealed class NoticeExpiryClock : TimeProvider
    {
        private DateTimeOffset _now = PreFindingNoticeTransport.Now;
        private NoticeTimer? _timer;
        internal TimeSpan DueTime { get; private set; }
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime.TotalMilliseconds > uint.MaxValue - 1d)
                throw new ArgumentOutOfRangeException(nameof(dueTime));
            DueTime = dueTime;
            return _timer = new NoticeTimer(callback, state);
        }
        internal void Expire() => Advance(TimeSpan.FromHours(2));
        internal void Advance(TimeSpan elapsed)
        {
            _now += elapsed;
            _timer!.Fire();
        }

        private sealed class NoticeTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            internal void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
