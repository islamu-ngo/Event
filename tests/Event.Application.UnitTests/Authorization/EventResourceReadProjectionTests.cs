using System.Text.Json;
using Explore.Application.Services;

namespace Event.Application.UnitTests.Authorization;

public sealed class EventResourceReadProjectionTests
{
    [Test]
    public async Task ConcurrentConsumersCannotTakeTheSamePrivateProjection()
    {
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuePreparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var projection = new EventResourceReadProjection<string>("private metadata",
            _ => Task.FromResult(EventResourceAuthorityOutcome.Allowed), default);
        int delivered = 0;
        var first = projection.DiscloseAsync(async (value, token) =>
        {
            preparing.SetResult();
            await continuePreparation.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            return value;
        }, (_, _) => { delivered++; return Task.CompletedTask; });
        await preparing.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var duplicate = await projection.DiscloseAsync((value, _) => Task.FromResult(value),
            (_, _) => { delivered++; return Task.CompletedTask; });
        continuePreparation.SetResult();
        await Assert.That(duplicate).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(await first).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(delivered).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RevocationOrOriginalCancellationDuringPreparationPreventsDelivery(bool cancel)
    {
        using var original = new CancellationTokenSource();
        bool allowed = true, delivered = false;
        var projection = new EventResourceReadProjection<string>("private metadata",
            _ => Task.FromResult(allowed ? EventResourceAuthorityOutcome.Allowed : EventResourceAuthorityOutcome.Forbidden),
            original.Token);
        async Task<EventResourceAuthorityOutcome> DiscloseAsync() => await projection.DiscloseAsync((value, _) =>
        {
            if (cancel) original.Cancel();
            else allowed = false;
            return Task.FromResult(value);
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        if (cancel)
            await Assert.That(DiscloseAsync).Throws<OperationCanceledException>();
        else
            await Assert.That(await DiscloseAsync()).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
        await Assert.That(delivered).IsFalse();
        await Assert.That(await projection.DiscloseAsync((value, _) => Task.FromResult(value),
            (_, _) => { delivered = true; return Task.CompletedTask; }))
            .IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    public async Task SerializingTheNativeProjectionCannotDiscloseItsPayload()
    {
        var projection = new EventResourceReadProjection<string>("private metadata",
            _ => Task.FromResult(EventResourceAuthorityOutcome.Allowed), default);
        await Assert.That(JsonSerializer.SerializeToElement(projection).EnumerateObject().Count()).IsEqualTo(0);
        await Assert.That(projection.ToString()).DoesNotContain("private metadata");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PrivatePreparationOwnerIsReleasedWhenDeliveryIsDeniedOrPreparationFails(bool failPreparation)
    {
        var owner = new PreparationOwner();
        await using var projection = new EventResourceReadProjection<string>("private metadata",
            _ => Task.FromResult(EventResourceAuthorityOutcome.Forbidden), default, owner);
        bool delivered = false;
        Task<EventResourceAuthorityOutcome> ConsumeAsync() => projection.DiscloseAsync(
            (value, _) => failPreparation
                ? Task.FromException<string>(new InvalidOperationException("Preparation failed."))
                : Task.FromResult(value),
            (_, _) => { delivered = true; return Task.CompletedTask; });
        if (failPreparation)
            await Assert.That(ConsumeAsync).Throws<InvalidOperationException>();
        else
            await Assert.That(await ConsumeAsync()).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
        await Assert.That(owner.Disposals).IsEqualTo(1);
        await projection.DisposeAsync();
        await Assert.That(owner.Disposals).IsEqualTo(1);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    public async Task AbandonedProjectionDisposesItsOwnerAndCannotBeConsumedLater()
    {
        var owner = new PreparationOwner();
        var projection = new EventResourceReadProjection<string>("private metadata",
            _ => Task.FromResult(EventResourceAuthorityOutcome.Allowed), default, owner);
        await projection.DisposeAsync();
        bool delivered = false;
        await Assert.That(await projection.DiscloseAsync((value, _) => Task.FromResult(value),
            (_, _) => { delivered = true; return Task.CompletedTask; }))
            .IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(owner.Disposals).IsEqualTo(1);
        await Assert.That(delivered).IsFalse();
    }

    private sealed class PreparationOwner : IAsyncDisposable
    {
        internal int Disposals { get; private set; }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
