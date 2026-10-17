using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class OutboxProcessorDeadLetterTests
{
    [Test]
    public async Task CurrentFinalClaim_ReconcilesAfterPersistedDeadLetterExactlyOnce()
    {
        await using var scenario = await Scenario.CreateAsync(maxRetries: 1);
        int attempts = 0;
        scenario.Cache.OnSubjectRemove = () => ++attempts == 1
                ? ValueTask.FromException(new InvalidOperationException("cache unavailable"))
                : ValueTask.CompletedTask;
        await scenario.ProcessAsync();
        var row = await scenario.ReadAsync();
        await Assert.That(row.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(row.RetryCount).IsEqualTo(1);
        await Assert.That(row.NextRetryAt).IsNull();
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ProcessAsync());
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task TransientAttempt_RetainsNativeRetryInsteadOfReconciling()
    {
        await using var scenario = await Scenario.CreateAsync(maxRetries: 3);
        int attempts = 0;
        scenario.Cache.OnSubjectRemove = () =>
        {
            attempts++;
            return ValueTask.FromException(new InvalidOperationException("cache unavailable"));
        };
        await scenario.ProcessAsync();
        var row = await scenario.ReadAsync();
        await Assert.That(row.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(row.RetryCount).IsEqualTo(1);
        await Assert.That(row.LastError).IsEqualTo("dispatch_failed");
        await Assert.That(row.NextRetryAt).IsNotNull();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task ReplacedDuringDispatch_StaleFailureCannotReconcileOrSettleSuccessor()
    {
        await using var scenario = await Scenario.CreateAsync(maxRetries: 1);
        DateTime? replacement = null;
        int attempts = 0;
        scenario.Cache.OnSubjectRemove = () => new ValueTask(ReplaceThenFailAsync());
        async Task ReplaceThenFailAsync()
        {
            attempts++;
            await using var scope = scenario.Services.CreateAsyncScope();
            replacement = await scope.ServiceProvider.GetRequiredService<IOutboxRepository>()
                .TryClaimForProcessing(scenario.Claim.MessageId, scenario.Claim.LeaseExpiresAt);
            throw new InvalidOperationException("cache failed after replacement");
        }
        await scenario.ProcessAsync();
        var row = await scenario.ReadAsync();
        await Assert.That(replacement).IsNotNull();
        await Assert.That(row.Status).IsEqualTo(OutboxMessageStatus.Processing);
        await Assert.That(row.NextRetryAt).IsEqualTo(replacement);
        await Assert.That(row.RetryCount).IsEqualTo(0);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task FailedReconciliation_RecoversOnlyUnderNewPersistedDeadLetterClaim()
    {
        await using var scenario = await Scenario.CreateAsync(maxRetries: 1);
        int attempts = 0;
        scenario.Cache.OnSubjectRemove = () =>
        {
            attempts++;
            return attempts <= 2
                ? ValueTask.FromException(new InvalidOperationException("cache unavailable"))
                : ValueTask.CompletedTask;
        };
        await scenario.ProcessAsync();
        var failed = await scenario.ReadAsync();
        await Assert.That(failed.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(failed.NextRetryAt).IsEqualTo(scenario.Claim.LeaseExpiresAt);
        await using var scope = scenario.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        DateTime? replacement = await repository.TryClaimDeadLetterReconciliation(
            failed.Id, scenario.Claim.LeaseExpiresAt);
        var successor = await scope.ServiceProvider.GetRequiredService<IGenericOutboxAdmission>()
            .ReadClaimAsync(failed.Id, replacement!.Value, OutboxMessageStatus.DeadLettered, CancellationToken.None);
        await Assert.That(successor).IsNotNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ProcessAsync());
        await GenericOutboxWorkerAdmissionTests.CreateProcessor(scenario.Services)
            .ProcessGenericClaimAsync(successor!, CancellationToken.None);
        await Assert.That((await scenario.ReadAsync()).NextRetryAt).IsNull();
        await Assert.That(attempts).IsEqualTo(3);
    }

    private sealed class Scenario(
        LocalAdmissionWebApplicationFactory original,
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> hosted,
        HttpClient client,
        FaultingCache cache,
        GenericOutboxClaim claim) : IAsyncDisposable
    {
        internal IServiceProvider Services => hosted.Services;
        internal FaultingCache Cache => cache;
        internal GenericOutboxClaim Claim => claim;

        internal static async Task<Scenario> CreateAsync(int maxRetries)
        {
            var original = await LocalAdmissionWebApplicationFactory.CreateAsync();
            var cache = new FaultingCache();
            var hosted = original.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                GenericOutboxWorkerTestServices.Configure(services);
                // Only the external cache transport is controlled; admission, Composite and persistence stay native.
                services.RemoveAll<HybridCache>();
                services.AddSingleton<HybridCache>(cache);
            }));
            var client = hosted.CreateClient();
            await using var scope = hosted.Services.CreateAsyncScope();
            var message = PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);
            message.MaxRetries = maxRetries;
            await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().Create(message);
            var claim = (await scope.ServiceProvider.GetRequiredService<IGenericOutboxAdmission>()
                .ClaimDueAsync(1000, DateTime.UtcNow, CancellationToken.None)).Single(row => row.MessageId == message.Id);
            cache.SubjectKey = $"user:detail:{claim.AggregateId}";
            return new Scenario(original, hosted, client, cache, claim);
        }

        internal Task ProcessAsync() => GenericOutboxWorkerAdmissionTests.CreateProcessor(Services)
            .ProcessGenericClaimAsync(Claim, CancellationToken.None);

        internal async Task<OutboxMessage> ReadAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().OutboxMessages
                .AsNoTracking().SingleAsync(row => row.Id == Claim.MessageId);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await hosted.DisposeAsync();
            await original.DisposeAsync();
        }
    }

    private sealed class FaultingCache : HybridCache
    {
        internal string? SubjectKey { get; set; }
        internal Func<ValueTask> OnSubjectRemove { get; set; } = () => ValueTask.CompletedTask;
        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => factory(state, cancellationToken);
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            key == SubjectKey ? OnSubjectRemove() : ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
