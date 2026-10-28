using System.IO.Pipelines;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>
/// Native empty-slot authoring reads through the actual terminal HTTP result. These witnesses
/// neither publish an active policy nor supply fabricated conformance or qualification evidence.
/// </summary>
public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentPolicyHttpCompletionRetainsOriginalNativeFencesEvenAfterRequestAbort(bool cancel)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var (handler, accessor, _) = CurrentAuthoringHandler(database, context);
        var pending = await handler.QueryAsync(new("tenant"));
        using var request = new CancellationTokenSource();
        var lifetime = new CurrentPolicyRequestLifetime { RequestAborted = request.Token };
        var http = accessor.HttpContext!;
        http.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        http.Items[HateoasConstants.MinimalResponseKey] = true;
        var completion = new CurrentPolicyResponseCompletion(context);
        await using var body = completion.Stream;
        http.Features.Set<IHttpResponseBodyFeature>(completion);
        var result = new ModerationCurrentPolicyHttpResult(pending, new CurrentPolicyTerminalAssembler(),
            Options.Create(new JsonOptions()), _ => throw new InvalidOperationException("No replacement payload is allowed."));
        var running = result.ExecuteResultAsync(new() { HttpContext = http });
        var cancellationObserved = false;
        long written = 0;
        try
        {
            await completion.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            written = body.Length;
            await Assert.That(written).IsGreaterThan(0L);
            await Assert.That(completion.OriginalTransactionId.HasValue).IsTrue();
            await Assert.That(context.Database.CurrentTransaction!.TransactionId)
                .IsEqualTo(completion.OriginalTransactionId!.Value);
            await Assert.That(context.ModerationTrustDomainId).IsEqualTo(database.Scope.TrustDomainId);
            await Assert.That(running.IsCompleted).IsFalse();
            if (cancel)
            {
                request.Cancel();
                await Assert.That(lifetime.Aborted).IsTrue();
                await Assert.That(running.IsCompleted).IsFalse();
                await Assert.That(context.Database.CurrentTransaction!.TransactionId)
                    .IsEqualTo(completion.OriginalTransactionId.Value);
                await Assert.That(context.ModerationTrustDomainId).IsEqualTo(database.Scope.TrustDomainId);
            }
        }
        finally
        {
            completion.Release.TrySetResult();
            try
            {
                await running.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException) when (cancel && request.IsCancellationRequested)
            {
                cancellationObserved = true;
            }
        }
        await Assert.That(cancellationObserved).IsEqualTo(cancel);
        await Assert.That(completion.TransactionAtCompletion).IsEqualTo(completion.OriginalTransactionId);
        await Assert.That(completion.TrustDomainAtCompletion).IsEqualTo(database.Scope.TrustDomainId);
        await Assert.That(body.Length).IsEqualTo(written);
        await Assert.That(http.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(http.Response.ContentLength).IsEqualTo(written);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await database.AssertCountsAsync(0, 0, 1);
    }

    private sealed class CurrentPolicyTerminalAssembler()
        : ResourceAssemblerBase<ModerationCurrentPolicyView>(null!,
            new ModerationCurrentPolicyLinkPolicy(), new ModerationCurrentPolicyCollectionLinkPolicy());

    private sealed class CurrentPolicyResponseCompletion(ExploreDbContext context) : IHttpResponseBodyFeature
    {
        private readonly StreamResponseBodyFeature body = new(new MemoryStream());
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid? OriginalTransactionId { get; private set; }
        public Guid? TransactionAtCompletion { get; private set; }
        public Guid? TrustDomainAtCompletion { get; private set; }
        public Stream Stream => body.Stream;
        public PipeWriter Writer => body.Writer;
        public void DisableBuffering() => body.DisableBuffering();
        public Task StartAsync(CancellationToken cancellationToken = default) => body.StartAsync(cancellationToken);
        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
            body.SendFileAsync(path, offset, count, cancellationToken);
        public async Task CompleteAsync()
        {
            OriginalTransactionId = context.Database.CurrentTransaction?.TransactionId;
            Entered.TrySetResult();
            await Release.Task;
            TransactionAtCompletion = context.Database.CurrentTransaction?.TransactionId;
            TrustDomainAtCompletion = context.ModerationTrustDomainId;
            await body.CompleteAsync();
        }
    }

    private sealed class CurrentPolicyRequestLifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        public bool Aborted { get; private set; }
        public void Abort() => Aborted = true;
    }
}
