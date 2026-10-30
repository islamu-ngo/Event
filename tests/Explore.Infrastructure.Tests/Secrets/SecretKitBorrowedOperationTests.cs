using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitBorrowedOperationTests
{
    [Test]
    public async Task EveryRetirementCallerObservesUnexpectedCancellationCallbackFailure()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Build();
        var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        using var operation = owner.BeginOperation();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException();
        using var observation = operation.Token.Register(() =>
        {
            entered.TrySetResult();
            throw failure;
        });
        Task first = owner.DisposeAsync().AsTask();
        Task second = owner.DisposeAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            operation.Dispose();
        }
        Func<Task> firstObservation = () => first.WaitAsync(TimeSpan.FromSeconds(10));
        Func<Task> secondObservation = () => second.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(firstObservation).Throws<AggregateException>();
        await Assert.That(secondObservation).Throws<AggregateException>();
        await Assert.That(ReferenceEquals(first.Exception!.Flatten().InnerExceptions.Single(), failure)).IsTrue();
        await Assert.That(ReferenceEquals(second.Exception!.Flatten().InnerExceptions.Single(), failure)).IsTrue();
        await Assert.That(() => owner.BeginOperation()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task RetirementCancelsBorrowedWorkAndDrainsBeforeDisposal()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), configuration);
        var operation = owner.BeginOperation();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = operation.Token.Register(() => cancelled.TrySetResult());
        Task retirement = owner.DisposeAsync().AsTask();
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(retirement.IsCompleted).IsFalse();
            await Assert.That(() => owner.BeginOperation()).Throws<ObjectDisposedException>();
        }
        finally
        {
            operation.Dispose();
        }
        await retirement.WaitAsync(TimeSpan.FromSeconds(10));
        operation.Dispose();
    }
}
