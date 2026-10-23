using System.IO.Pipelines;
using Explore.API.Services.Calendar;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Handlers.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Calendar;

public sealed class PublicEventCalendarHttpResultTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedOrExactlyExpiredPreparationCannotWriteCalendarBytes(bool expire)
    {
        var owner = new Owner();
        var clock = new Clock();
        var builder = new Builder(() =>
        {
            if (expire) clock.UtcNow = owner.Lease.ValidUntil;
            else owner.Lease.Current = false;
        });
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        await (await Result(owner, clock, builder)).ExecuteResultAsync(new() { HttpContext = http });
        await Assert.That(http.Response.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(http.Response.Body.Length).IsEqualTo(0);
        await Assert.That(http.Response.Headers.ContentDisposition.Count).IsEqualTo(0);
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    public async Task NativeLeaseRemainsHeldUntilTheActualBodyWriteSettles()
    {
        var owner = new Owner();
        var http = new DefaultHttpContext();
        var body = new Body(owner.Lease);
        http.Response.Body = body;
        var running = (await Result(owner, new Clock(), new Builder())).ExecuteResultAsync(new() { HttpContext = http });
        await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(owner.Lease.Disposed).IsFalse();
        body.Release.TrySetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(body.NativeLeaseHeld).IsTrue();
        await Assert.That(body.Length > 0).IsTrue();
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    public async Task NativeLeaseRemainsHeldUntilBufferedResponseCompletionSettles()
    {
        var owner = new Owner();
        var http = new DefaultHttpContext();
        var completion = new Completion(owner.Lease);
        http.Features.Set<IHttpResponseBodyFeature>(completion);
        var running = (await Result(owner, new Clock(), new Builder())).ExecuteResultAsync(new() { HttpContext = http });

        await completion.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(completion.Stream.Length > 0).IsTrue();
        await Assert.That(owner.Lease.Disposed).IsFalse();
        completion.Release.TrySetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(completion.NativeLeaseHeld).IsTrue();
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TransportAbortRetainsNativeFencesUntilOutstandingResponseCompletionSettles(bool deadline)
    {
        var owner = new Owner();
        using var request = new CancellationTokenSource();
        var http = new DefaultHttpContext { RequestAborted = request.Token };
        var lifetime = new Lifetime();
        var completion = new Completion(owner.Lease);
        http.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        http.Features.Set<IHttpResponseBodyFeature>(completion);
        var running = (await Result(owner, new Clock(), new Builder())).ExecuteResultAsync(new() { HttpContext = http });

        await completion.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var written = completion.Stream.Length;
        if (deadline) owner.Lease.Deadline.Cancel();
        else request.Cancel();
        await Assert.That(lifetime.Aborted).IsTrue();
        await Assert.That(running.IsCompleted).IsFalse();
        await Assert.That(owner.Lease.Disposed).IsFalse();

        completion.Release.TrySetResult();
        if (deadline) await running.WaitAsync(TimeSpan.FromSeconds(5));
        else await Assert.That(async () => await running.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<OperationCanceledException>();
        await Assert.That(completion.NativeLeaseHeld).IsTrue();
        await Assert.That(completion.Stream.Length).IsEqualTo(written);
        await Assert.That(http.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    public async Task NativeDeadlineAbortsAStalledTransportAndRetainsCustodyUntilWriteCancellation()
    {
        var owner = new Owner();
        var http = new DefaultHttpContext();
        var lifetime = new Lifetime();
        http.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        var body = new Body(owner.Lease);
        http.Response.Body = body;
        var running = (await Result(owner, new Clock(), new Builder())).ExecuteResultAsync(new() { HttpContext = http });
        await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(owner.Lease.Disposed).IsFalse();
        owner.Lease.Deadline.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(lifetime.Aborted).IsTrue();
        await Assert.That(body.NativeLeaseHeld).IsTrue();
        await Assert.That(body.Length).IsEqualTo(0);
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    public async Task DeadlineAbortDoesNotReleaseNativeFencesWhileTheTransportWriteIsOutstanding()
    {
        var owner = new Owner();
        var http = new DefaultHttpContext();
        var lifetime = new Lifetime();
        http.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        var body = new Body(owner.Lease, ignoreCancellation: true, () => lifetime.Aborted);
        http.Response.Body = body;
        var running = (await Result(owner, new Clock(), new Builder())).ExecuteResultAsync(new() { HttpContext = http });
        await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        owner.Lease.Deadline.Cancel();
        await Assert.That(lifetime.Aborted).IsTrue();
        await Assert.That(running.IsCompleted).IsFalse();
        await Assert.That(owner.Lease.Disposed).IsFalse();
        body.Release.TrySetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(body.Length).IsEqualTo(0);
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    public async Task ClientCancellationDisposesTheHeldLeaseWithoutSendingAReplacementBody()
    {
        var owner = new Owner();
        using var request = new CancellationTokenSource();
        var http = new DefaultHttpContext { RequestAborted = request.Token };
        var body = new Body(owner.Lease);
        http.Response.Body = body;
        var running = (await Result(owner, new Clock(), new Builder())).ExecuteResultAsync(new() { HttpContext = http });
        await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        request.Cancel();
        await Assert.That(async () => await running).Throws<OperationCanceledException>();
        await Assert.That(body.Length).IsEqualTo(0);
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    private static async Task<PublicEventCalendarHttpResult> Result(Owner owner, Clock clock, Builder builder)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(owner.Lease.Event.TenantId);
        var projection = await new GetEventCalendarExportRequestHandler(owner, tenant,
            Substitute.For<IEventLocationDisclosureService>(), clock).QueryAsync(new(owner.Lease.Event.Id), CancellationToken.None);
        var urls = Substitute.For<IPublicUrlBuilder>();
        urls.GetEventUrl(owner.Lease.Event.Id).Returns($"https://example.invalid/events/{owner.Lease.Event.Id:D}");
        return new(projection!, builder, urls, _ => new StatusCodeResult(StatusCodes.Status503ServiceUnavailable));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class Builder(Action? beforeReturn = null) : IEventCalendarFileBuilder
    {
        public string Build(EventCalendarExportDto export, Uri url)
        {
            var result = new IcalNetEventCalendarFileBuilder().Build(export, url);
            beforeReturn?.Invoke();
            return result;
        }
        public string Build(AttendeeEventCalendarExportDto export, Uri url) => throw new InvalidOperationException();
    }

    private sealed class Body(Lease lease, bool ignoreCancellation = false, Func<bool>? transportAborted = null) : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool NativeLeaseHeld { get; private set; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            NativeLeaseHeld = !lease.Disposed;
            Entered.TrySetResult();
            if (ignoreCancellation) await Release.Task;
            else await Release.Task.WaitAsync(cancellationToken);
            if (transportAborted?.Invoke() == true) return;
            await base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class Completion(Lease lease) : IHttpResponseBodyFeature
    {
        private readonly StreamResponseBodyFeature body = new(new MemoryStream());
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool NativeLeaseHeld { get; private set; }
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
            NativeLeaseHeld = !lease.Disposed;
            await body.CompleteAsync();
        }
    }

    private sealed class Lifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        public bool Aborted { get; private set; }
        public void Abort() => Aborted = true;
    }

    private sealed class Owner : IModerationPublicEventCalendarRepository
    {
        public Lease Lease { get; } = new();
        public Task<T> ExecuteSerializedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
        public Task<ModerationPublicEventCalendarAcquisition> AcquireAsync(
            ModerationPublicEventCalendarRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ModerationPublicEventCalendarAcquisition(ModerationPublicEventCalendarOutcome.Allowed, Lease));
    }

    private sealed class Lease : IModerationPublicEventCalendarLease
    {
        public Explore.Domain.Event Event { get; } = new(EventStatusEnum.Published)
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Title = "Reviewed calendar title",
            Actor = null!, Tenant = null!, VisibilityType = null!, EventStatus = null!, EventFormat = null!
        };
        public EventSession PrimarySession { get; }
        public DateTimeOffset ValidUntil => Now.AddMinutes(1);
        public CancellationTokenSource Deadline { get; } = new();
        public CancellationToken ValidityCancellation => Deadline.Token;
        public bool Current { get; set; } = true;
        public bool Disposed { get; private set; }
        public Lease() => PrimarySession = new(EventSessionStatusEnum.Published)
        {
            Id = Guid.NewGuid(), EventId = Event.Id, Event = Event, TenantId = Event.TenantId, Tenant = null!,
            StartTime = Now.AddDays(1), EndTime = Now.AddDays(1).AddHours(1)
        };
        public Task<ModerationPublicEventCalendarOutcome> RevalidateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Current && !Disposed ? ModerationPublicEventCalendarOutcome.Allowed
                : ModerationPublicEventCalendarOutcome.Unavailable);
        public ValueTask DisposeAsync() { Disposed = true; Deadline.Dispose(); return ValueTask.CompletedTask; }
    }
}
