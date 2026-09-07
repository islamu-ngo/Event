using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventLocations.Handlers.Commands;
using Explore.Application.Features.EventLocations.Requests.Commands;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using NSubstitute;

namespace ApplicationUnitTests.Features.EventLocations.Commands;

[Category("EventLocationPrivacy")]
public sealed class EventLocationPolicyExactCacheInvalidationTests
{
    [Test]
    public async Task SuccessfulWriteEvictsOnlyExactEventLocationEvaluatorKey()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        EventLocation placement = EventLocation.CreatePhysical(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            actorId,
            DateTime.UtcNow.AddDays(-1));
        var eventLocations = Substitute.For<IEventLocationRepository>();
        eventLocations.GetForUpdateAsync(placement.Id, Arg.Any<CancellationToken>()).Returns(placement);
        var audits = Substitute.For<IEventLocationDisclosureAuditRepository>();
        audits.AppendAsync(Arg.Any<EventLocationDisclosureAudit>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<EventLocationDisclosureAudit>(0));
        var cache = new RecordingHybridCache();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var userContext = Substitute.For<IUserContext>();
        userContext.GetRequiredUserId().Returns(actorId);
        var handler = new UpdateEventLocationPolicyCommandHandler(
            eventLocations,
            audits,
            new PassThroughUnitOfWork(),
            cache,
            tenantContext,
            userContext,
            TimeProvider.System);
        var command = new UpdateEventLocationPolicyCommand
        {
            EventId = eventId,
            EventLocationId = placement.Id,
            ExpectedConcurrencyStamp = placement.ConcurrencyStamp,
            ExpectedPolicyVersion = placement.PolicyVersion,
            Fields = new UpdateEventLocationDisclosureFieldsDto { ShowCountry = true },
            NeedsPrivacyReview = false
        };

        var response = await handler.Handle(command, CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(cache.RemovedTags).IsEquivalentTo([CacheTags.EventLocation(placement.Id)]);
    }

    [Test]
    public async Task CallerCancellationAfterCommitCannotSkipExactEvaluatorEviction()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        EventLocation placement = EventLocation.CreatePhysical(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            actorId,
            DateTime.UtcNow.AddDays(-1));
        var eventLocations = Substitute.For<IEventLocationRepository>();
        eventLocations.GetForUpdateAsync(placement.Id, Arg.Any<CancellationToken>()).Returns(placement);
        var audits = Substitute.For<IEventLocationDisclosureAuditRepository>();
        audits.AppendAsync(Arg.Any<EventLocationDisclosureAudit>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<EventLocationDisclosureAudit>(0));
        var cache = new RecordingHybridCache();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var userContext = Substitute.For<IUserContext>();
        userContext.GetRequiredUserId().Returns(actorId);
        using var cancellation = new CancellationTokenSource();
        var handler = new UpdateEventLocationPolicyCommandHandler(
            eventLocations,
            audits,
            new PostCommitCancellingUnitOfWork(cancellation),
            cache,
            tenantContext,
            userContext,
            TimeProvider.System);
        var command = new UpdateEventLocationPolicyCommand
        {
            EventId = eventId,
            EventLocationId = placement.Id,
            ExpectedConcurrencyStamp = placement.ConcurrencyStamp,
            ExpectedPolicyVersion = placement.PolicyVersion,
            Fields = new UpdateEventLocationDisclosureFieldsDto { ShowCity = true }
        };

        var response = await handler.Handle(command, cancellation.Token);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(cache.RemovedTags).IsEquivalentTo([CacheTags.EventLocation(placement.Id)]);
    }

    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);
    }

    private sealed class PostCommitCancellingUnitOfWork(CancellationTokenSource cancellation) : IUnitOfWork
    {
        public async Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default)
        {
            await operation(ct);
            await cancellation.CancelAsync();
        }

        public async Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            T result = await operation(ct);
            await cancellation.CancelAsync();
            return result;
        }

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);
    }

    private sealed class RecordingHybridCache : HybridCache
    {
        public List<string> RemovedTags { get; } = [];

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => factory(state, cancellationToken);

        public override ValueTask RemoveAsync(
            string key,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(
            string tag,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemovedTags.Add(tag);
            return ValueTask.CompletedTask;
        }

        public override ValueTask SetAsync<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
