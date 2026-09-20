using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventAgendaItemHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentUpdates_WithTwoAuthorizedSnapshots_RollBackTheStaleWriter(bool move)
    {
        await using var factory = await AgendaFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        var before = await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId));
        var stamp = before.GetProperty("concurrencyStamp").GetGuid();
        factory.AuthorizationGate.ItemId = factory.ItemId;
        var first = PatchAsync(owner, factory.ItemId, new { title = new { value = "Winner" } }, $"\"{stamp}\"");
        Task<HttpResponseMessage>? second = null;
        try
        {
            await factory.AuthorizationGate.FirstArrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
            object change = move
                ? new { @event = new { eventId = factory.PrivateId } }
                : new { title = new { value = "Loser" } };
            second = PatchAsync(owner, factory.ItemId, change, $"\"{stamp}\"");
            await factory.AuthorizationGate.SecondArrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
            factory.AuthorizationGate.ReleaseFirst.TrySetResult();
            using var winner = await first.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(winner.StatusCode).IsEqualTo(HttpStatusCode.OK);
            factory.AuthorizationGate.ReleaseSecond.TrySetResult();
            using var loser = await second.WaitAsync(TimeSpan.FromSeconds(20));
            await ProblemAsync(loser, HttpStatusCode.Conflict);
            var after = await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId));
            await Assert.That(after.GetProperty("title").GetString()).IsEqualTo("Winner");
            await Assert.That(after.GetProperty("eventId").GetGuid()).IsEqualTo(factory.PublicId);
            await Assert.That(after.GetProperty("concurrencyStamp").GetGuid()).IsNotEqualTo(stamp);
            await Assert.That(Items(await GetAsync(owner, Managed(factory.PrivateId))).Length).IsEqualTo(1);
            await Assert.That(await PlacementIdsAsync(factory, factory.PrivateId)).IsEmpty();
        }
        finally
        {
            factory.AuthorizationGate.ReleaseFirst.TrySetResult();
            factory.AuthorizationGate.ReleaseSecond.TrySetResult();
            (await first.WaitAsync(TimeSpan.FromSeconds(20))).Dispose();
            if (second is not null)
                (await second.WaitAsync(TimeSpan.FromSeconds(20))).Dispose();
        }
    }

    [Test]
    public async Task StorageFailure_RollsBackNewPlacementCreateAndMove()
    {
        await using var factory = await AgendaFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        var before = await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId));
        var placements = await PlacementIdsAsync(factory, factory.PublicId);
        factory.WriteFailure.Enabled = true;
        using (var failed = await owner.PostAsJsonAsync("/api/eventagendaitem", Input(factory.PublicId)))
            await ProblemAsync(failed, HttpStatusCode.InternalServerError);
        using (var failed = await PatchAsync(owner, factory.ItemId,
            new { @event = new { eventId = factory.PrivateId } }, $"\"{before.GetProperty("concurrencyStamp").GetGuid()}\""))
            await ProblemAsync(failed, HttpStatusCode.InternalServerError);
        factory.WriteFailure.Enabled = false;
        await Assert.That(factory.WriteFailure.Failures).IsEqualTo(2);
        await Assert.That(JsonElement.DeepEquals(await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId)), before)).IsTrue();
        await Assert.That(Items(await GetAsync(owner, Managed(factory.PublicId))).Length).IsEqualTo(1);
        await Assert.That(Items(await GetAsync(owner, Managed(factory.PrivateId))).Length).IsEqualTo(1);
        await Assert.That(await PlacementIdsAsync(factory, factory.PublicId)).IsEquivalentTo(placements);
        await Assert.That(await PlacementIdsAsync(factory, factory.PrivateId)).IsEmpty();
    }

    private static async Task<Guid[]> PlacementIdsAsync(AgendaFactory factory, Guid eventId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var placements = await scope.ServiceProvider.GetRequiredService<IEventLocationRepository>().GetByEventIdAsync(eventId, default);
        return placements.Select(location => location.Id).ToArray();
    }

    private sealed partial class AgendaFactory
    {
        public AgendaWriteFailure WriteFailure { get; } = new();
        public AgendaAuthorizationGate AuthorizationGate { get; } = new();

        private void ConfigureMutationControls(IServiceCollection services)
        {
            // Only schedule completion of actual policy decisions; never replace their outcome
            // or the production RequestAuthorization/enricher/native-port composition.
            services.RemoveAll<IAuthorizationProvider>();
            services.AddScoped<IAuthorizationProvider>(provider =>
                new CoordinatedAuthorizationProvider(provider.GetRequiredService<RuntimeAuthorizationProvider>(), AuthorizationGate));
        }
    }

    private sealed class AgendaAuthorizationGate
    {
        public Guid ItemId { get; set; }
        public TaskCompletionSource FirstArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            int arrival = Interlocked.Increment(ref _arrivals);
            if (arrival == 1)
            {
                FirstArrived.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            else if (arrival == 2)
            {
                SecondArrived.TrySetResult();
                await ReleaseSecond.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    private sealed class CoordinatedAuthorizationProvider(RuntimeAuthorizationProvider inner, AgendaAuthorizationGate gate) : IAuthorizationProvider
    {
        public async Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            var decision = await inner.AuthorizeAsync(request, cancellationToken);
            if (decision.IsAllowed && gate.ItemId != Guid.Empty && request.ResourceId == gate.ItemId.ToString()
                && request.Capability.ResourceKind == ResourceKinds.EventAgendaItem && request.Capability.Action == AuthorizationActions.Update)
                await gate.ArriveAsync(cancellationToken);
            return decision;
        }

        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default) =>
            inner.AuthorizeBatchAsync(requests, cancellationToken);
    }

    private sealed class AgendaWriteFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<EventAgendaItem>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                Failures++;
                throw new InvalidOperationException("Injected agenda storage failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
