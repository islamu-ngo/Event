using System.Net;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventDayHttpTests
{
    [Test]
    public async Task ConcurrentPatches_WithTheSameStamp_KeepOneDurableWinner()
    {
        await using var factory = await DayFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        var before = await DetailAsync(owner, factory.PublicDayId);
        var stamp = before.GetProperty("concurrencyStamp").GetGuid();
        factory.DayWrites.Enabled = true;
        var first = PatchAsync(owner, factory.PublicDayId, new { sortOrder = new { value = 7 } }, $"\"{stamp}\"");
        var second = PatchAsync(owner, factory.PublicDayId, new { sortOrder = new { value = 9 } }, $"\"{stamp}\"");
        var responses = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            await Assert.That(responses.Select(response => response.StatusCode))
                .IsEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            await ProblemAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict);
            var after = await DetailAsync(owner, factory.PublicDayId);
            await Assert.That(after.GetProperty("sortOrder").GetInt32()).IsEqualTo(responses[0].IsSuccessStatusCode ? 7 : 9);
            await Assert.That(after.GetProperty("concurrencyStamp").GetGuid()).IsNotEqualTo(stamp);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    private sealed partial class DayFactory
    {
        public DayWriteBarrier DayWrites { get; } = new();
    }

    private sealed class DayWriteBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public bool Enabled { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<EventDay>().Any(entry => entry.State == EntityState.Modified))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _bothArrived.TrySetResult();
                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
