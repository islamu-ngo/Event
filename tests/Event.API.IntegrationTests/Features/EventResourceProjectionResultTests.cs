using System.IO.Pipelines;
using System.Text;
using Explore.API.Models;
using Explore.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Exercises terminal transport and prepared-metadata custody only. The controlled entry outcome
/// does not qualify native moderation authority, source fences, provider parity or conformance.
/// </summary>
public sealed class EventResourceProjectionResultTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task BufferedCompletionRetainsPreparationIncludingAfterOriginalOrRequestAbort(
        bool cancelOriginal, bool cancelRequest)
    {
        using var original = new CancellationTokenSource();
        using var request = new CancellationTokenSource();
        var owner = new PreparationOwner();
        var projection = new EventResourceReadProjection<string>("prepared resource metadata",
            _ => Task.FromResult(EventResourceAuthorityOutcome.Allowed), original.Token, owner);
        var http = new DefaultHttpContext();
        var lifetime = new RequestLifetime { RequestAborted = request.Token };
        var completion = new Completion(owner);
        await using var body = completion.Stream;
        http.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        http.Features.Set<IHttpResponseBodyFeature>(completion);
        var result = Result(projection);
        var running = result.ExecuteResultAsync(new() { HttpContext = http });
        long written = 0;
        var cancelled = false;
        try
        {
            await completion.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            written = body.Length;
            await Assert.That(written).IsGreaterThan(0L);
            await Assert.That(owner.Disposed).IsFalse();
            await Assert.That(running.IsCompleted).IsFalse();
            if (cancelOriginal) original.Cancel();
            if (cancelRequest) request.Cancel();
            if (cancelOriginal || cancelRequest)
            {
                await Assert.That(lifetime.Aborted).IsTrue();
                await Assert.That(running.IsCompleted).IsFalse();
                await Assert.That(owner.Disposed).IsFalse();
            }
        }
        finally
        {
            completion.Release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (cancelOriginal || cancelRequest) { cancelled = true; }
        }
        await Assert.That(cancelled).IsEqualTo(cancelOriginal || cancelRequest);
        await Assert.That(completion.PreparationHeldAtCompletion).IsTrue();
        await Assert.That(owner.Disposed).IsTrue();
        await Assert.That(body.Length).IsEqualTo(written);
        await Assert.That(http.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
    }

    [Test]
    public async Task FailedResponseCompletionAbortsWithoutReplacementAndReleasesPreparation()
    {
        var owner = new PreparationOwner();
        var projection = new EventResourceReadProjection<string>("prepared resource metadata",
            _ => Task.FromResult(EventResourceAuthorityOutcome.Allowed), default, owner);
        var http = new DefaultHttpContext();
        var lifetime = new RequestLifetime();
        var completion = new Completion(owner, fail: true);
        await using var body = completion.Stream;
        http.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        http.Features.Set<IHttpResponseBodyFeature>(completion);
        completion.Release.TrySetResult();
        await Assert.That(async () => await Result(projection).ExecuteResultAsync(new() { HttpContext = http }))
            .Throws<IOException>();
        await Assert.That(lifetime.Aborted).IsTrue();
        await Assert.That(completion.PreparationHeldAtCompletion).IsTrue();
        await Assert.That(owner.Disposed).IsTrue();
        await Assert.That(body.Length).IsEqualTo(Encoding.UTF8.GetByteCount("prepared resource metadata"));
        await Assert.That(http.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
    }

    private static EventResourceProjectionResult<string> Result(EventResourceReadProjection<string> projection) =>
        new(projection, (value, _) => Task.FromResult<ActionResult>(new MetadataResult(value)),
            _ => throw new InvalidOperationException("A terminal output attempt must not append another payload."));

    private sealed class MetadataResult(string value) : ActionResult
    {
        public override async Task ExecuteResultAsync(ActionContext context) =>
            await context.HttpContext.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(value),
                context.HttpContext.RequestAborted);
    }

    private sealed class PreparationOwner : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Completion(PreparationOwner owner, bool fail = false) : IHttpResponseBodyFeature
    {
        private readonly StreamResponseBodyFeature body = new(new MemoryStream());
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool PreparationHeldAtCompletion { get; private set; }
        public Stream Stream => body.Stream;
        public PipeWriter Writer => body.Writer;
        public void DisableBuffering() => body.DisableBuffering();
        public Task StartAsync(CancellationToken cancellationToken = default) => body.StartAsync(cancellationToken);
        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
            body.SendFileAsync(path, offset, count, cancellationToken);
        public async Task CompleteAsync()
        {
            Entered.TrySetResult();
            await Release.Task;
            PreparationHeldAtCompletion = !owner.Disposed;
            if (fail) throw new IOException("Response completion failed.");
            await body.CompleteAsync();
        }
    }

    private sealed class RequestLifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        public bool Aborted { get; private set; }
        public void Abort() => Aborted = true;
    }
}
