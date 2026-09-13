using System.Net;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSeries.Requests.Commands;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventSeriesDisclosureHttpTests
{
    [Test]
    public async Task ConcurrentHttpPatches_KeepOneDurableWinnerForTheSameRevision()
    {
        var barrier = new SeriesWriteBarrier();
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = barrier };
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        var before = await DetailAsync(admin, data.PublicId);
        var stamp = before.GetProperty("concurrencyStamp").GetGuid();
        barrier.Enabled = true;
        var first = PatchAsync(admin, data.PublicId, new { title = new { value = "First" } }, stamp);
        var second = PatchAsync(admin, data.PublicId, new { title = new { value = "Second" } }, stamp);
        var responses = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            await Assert.That(responses.Select(response => response.StatusCode))
                .IsEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            var after = await DetailAsync(admin, data.PublicId);
            await Assert.That(after.GetProperty("title").GetString()).IsEqualTo(responses[0].IsSuccessStatusCode ? "First" : "Second");
            await Assert.That(after.GetProperty("concurrencyStamp").GetGuid()).IsNotEqualTo(stamp);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Test]
    public async Task OwnershipChangeDuringPolicyDecision_RejectsTheOldRevisionWithoutMutation()
    {
        var entered = new TaskCompletionSource<AuthorizationRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var request = call.Arg<AuthorizationRequest>();
            ArgumentNullException.ThrowIfNull(request);
            entered.TrySetResult(request);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), call.Arg<CancellationToken>());
            return AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos);
        });
        await using var factory = new NativeEventSeriesFactory { AuthorizationProviderOverride = provider };
        var data = await SeedAsync(factory);
        using var scope = Scope(factory, data.AdminId);
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSeriesDetailRequest, EventSeriesDto?>>();
        var original = (await query.QueryAsync(new(data.PublicId), default))!;
        var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventSeriesCommand, BaseCommandResponse<Guid>>>();
        var signal = entered.Task;
        var operation = port.ExecuteAsync(new()
        {
            EventSeriesId = data.PublicId, ExpectedConcurrencyStamp = original.ConcurrencyStamp,
            EventSeriesDto = new() { Title = new() { Value = "Must not persist" } }
        }, default);
        Guid newActorId;
        try
        {
            var request = await signal.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(request.Facts).IsEqualTo(new ActorAuthorizationFacts(PlatformDefaults.DefaultTenantId, data.ActorId));
            using var writeScope = Scope(factory);
            var db = writeScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var user = new UserBuilder().WithEmail($"new-owner-{Guid.CreateVersion7():N}@example.test").Build();
            var actor = new ActorBuilder().WithUserId(user.Id).WithDisplayName("New series owner").Build();
            db.AddRange(user, actor);
            var series = await db.EventSeries.SingleAsync(row => row.Id == data.PublicId);
            series.ActorId = actor.Id;
            await db.SaveChangesAsync();
            newActorId = actor.Id;
        }
        finally
        {
            release.TrySetResult();
        }
        await Assert.That(async () => await operation).Throws<ConcurrencyConflictException>();
        var after = (await query.QueryAsync(new(data.PublicId), default))!;
        await Assert.That(after.Title).IsEqualTo(original.Title);
        await Assert.That(after.ActorId).IsEqualTo(newActorId);
        await Assert.That(after.ConcurrencyStamp).IsNotEqualTo(original.ConcurrencyStamp);
    }

    private sealed class SeriesWriteBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public bool Enabled { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<EventSeries>().Any(entry => entry.State == EntityState.Modified))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _bothArrived.TrySetResult();
                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
